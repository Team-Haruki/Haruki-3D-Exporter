using System.Text.Json.Nodes;
using PjskBundle2Parts.Models;
namespace PjskBundle2Parts.Services;

public sealed class VrmcSpringBoneExtensionBuilder
{
    public void PrepareRuntimeGlb(
        VrmSpringBoneCandidate candidate,
        string inputGlbPath,
        string outputGlbPath
    )
    {
        var glb = File.ReadAllBytes(inputGlbPath);
        var document = GltfJsonEditor.ReadDocument(glb);
        var root = document.Root;
        var nodes = root["nodes"] as JsonArray
            ?? throw new InvalidDataException($"GLB has no nodes array: {inputGlbPath}");

        var parentByIndex = BuildParentMap(nodes);
        var nodeIndexByPath = BuildNodePathMap(nodes, parentByIndex);

        foreach (var spring in candidate.VrmExtensionDraft.Springs)
        {
            var lastJoint = spring.Joints.Count > 0
                ? spring.Joints[spring.Joints.Count - 1]
                : null;
            var lastNodePath = lastJoint?.NodePath;
            if (string.IsNullOrWhiteSpace(lastNodePath))
            {
                continue;
            }

            if (!TryResolveRuntimePath(nodeIndexByPath, lastNodePath, out var nodeIndex))
            {
                continue;
            }

            var node = nodes[nodeIndex] as JsonObject;
            if (node is null || HasAnyChild(node))
            {
                continue;
            }

            var tailName = BuildSpringTailName(lastJoint?.NodeName);
            var tailIndex = nodes.Count;
            nodes.Add(new JsonObject
            {
                ["name"] = tailName,
                // Matches UTJ.SpringBone.ComputeChildPosition's no-child fallback:
                // localTransform.position - localTransform.right * 0.1.
                ["translation"] = new JsonArray(-0.1f, 0f, 0f),
                ["rotation"] = new JsonArray(0f, 0f, 0f, 1f),
                ["scale"] = new JsonArray(1f, 1f, 1f),
            });
            EnsureArray(node, "children").Add(tailIndex);
        }

        GltfJsonEditor.WriteDocumentToGlb(root, document.BinaryChunk, outputGlbPath);
    }

    public VrmcSpringBoneBuildResult Build(
        VrmSpringBoneCandidate candidate,
        string glbPath
    )
    {
        var nodeResolver = GltfNodePathResolver.FromGlb(glbPath);
        var warnings = new List<string>();
        var pathMatches = new Dictionary<string, VrmcSpringBonePathMatch>(StringComparer.OrdinalIgnoreCase);
        var skippedColliders = new List<VrmcSpringBoneSkippedNode>();
        var skippedJoints = new List<VrmcSpringBoneSkippedNode>();

        var colliderIndexMap = new Dictionary<int, int>();
        var resolvedColliderIndexByKey = new Dictionary<ResolvedColliderKey, int>();
        var resolvedColliders = new List<VrmcSpringBoneCollider>();
        foreach (var collider in candidate.VrmExtensionDraft.Colliders)
        {
            if (!TryResolveNode(
                    nodeResolver,
                    collider.NodePath,
                    out var resolved,
                    out var match,
                    out var reason))
            {
                skippedColliders.Add(new VrmcSpringBoneSkippedNode(
                    Kind: "collider",
                    SourceName: collider.NodeName,
                    SourcePath: collider.NodePath,
                    Reason: reason
                ));
                continue;
            }

            if (match is not null)
            {
                pathMatches.TryAdd(match.SourcePath, match);
            }

            var shape = new VrmcSpringBoneColliderShape(
                Sphere: collider.Shape.Sphere,
                Capsule: collider.Shape.Capsule
            );
            var resolvedColliderKey = BuildResolvedColliderKey(resolved.NodeIndex, shape);
            if (resolvedColliderIndexByKey.TryGetValue(resolvedColliderKey, out var existingIndex))
            {
                colliderIndexMap[collider.Index] = existingIndex;
                continue;
            }

            var extensionIndex = resolvedColliders.Count;
            resolvedColliderIndexByKey[resolvedColliderKey] = extensionIndex;
            colliderIndexMap[collider.Index] = extensionIndex;
            resolvedColliders.Add(new VrmcSpringBoneCollider(
                Node: resolved.NodeIndex,
                Shape: shape
            ));
        }

        var colliderGroupIndexMap = new Dictionary<int, int>();
        var resolvedColliderGroups = new List<VrmcSpringBoneColliderGroup>();
        foreach (var group in candidate.VrmExtensionDraft.ColliderGroups)
        {
            var resolvedColliderIndexes = group.Colliders
                .Select(index => colliderIndexMap.TryGetValue(index, out var resolvedIndex)
                    ? resolvedIndex
                    : (int?)null)
                .Where(index => index is not null)
                .Select(index => index!.Value)
                .Distinct()
                .ToList();

            if (resolvedColliderIndexes.Count == 0)
            {
                continue;
            }

            var extensionIndex = resolvedColliderGroups.Count;
            colliderGroupIndexMap[group.Index] = extensionIndex;
            resolvedColliderGroups.Add(new VrmcSpringBoneColliderGroup(
                Name: group.Name,
                Colliders: resolvedColliderIndexes
            ));
        }

        var candidateJointCount = 0;
        var resolvedJointCount = 0;
        var resolvedSprings = new List<VrmcSpringBoneSpring>();
        foreach (var spring in candidate.VrmExtensionDraft.Springs)
        {
            candidateJointCount += spring.Joints.Count;
            var resolvedJoints = new List<VrmcSpringBoneJoint>();
            foreach (var runtimeJoint in BuildRuntimeJointChain(spring.Joints))
            {
                if (!TryResolveSpringJointNode(
                        nodeResolver,
                        runtimeJoint,
                        out var resolved,
                        out var match,
                        out var reason))
                {
                    skippedJoints.Add(new VrmcSpringBoneSkippedNode(
                        Kind: runtimeJoint.Kind,
                        SourceName: runtimeJoint.NodeName,
                        SourcePath: runtimeJoint.PrimaryPath,
                        Reason: reason
                    ));
                    continue;
                }

                if (match is not null)
                {
                    pathMatches.TryAdd(match.SourcePath, match);
                }

                resolvedJointCount++;
                resolvedJoints.Add(new VrmcSpringBoneJoint(
                    Node: resolved.NodeIndex,
                    HitRadius: runtimeJoint.Source.HitRadius,
                    Stiffness: runtimeJoint.Source.Stiffness,
                    GravityPower: runtimeJoint.Source.GravityPower,
                    GravityDir: runtimeJoint.Source.GravityDir,
                    DragForce: runtimeJoint.Source.DragForce
                ));
            }

            if (resolvedJoints.Count == 0)
            {
                continue;
            }

            int? center = null;
            if (!string.IsNullOrWhiteSpace(spring.CenterPath))
            {
                if (TryResolveNode(
                        nodeResolver,
                        spring.CenterPath,
                        out var resolvedCenter,
                        out var centerMatch,
                        out _))
                {
                    center = resolvedCenter.NodeIndex;
                    if (centerMatch is not null)
                    {
                        pathMatches.TryAdd(centerMatch.SourcePath, centerMatch);
                    }
                }
                else
                {
                    warnings.Add(
                        $"Spring {spring.Name} center path was not resolved: {spring.CenterPath}"
                    );
                }
            }

            var resolvedGroups = spring.ColliderGroups
                .Select(index => colliderGroupIndexMap.TryGetValue(index, out var resolvedIndex)
                    ? resolvedIndex
                    : (int?)null)
                .Where(index => index is not null)
                .Select(index => index!.Value)
                .Distinct()
                .ToList();

            resolvedSprings.Add(new VrmcSpringBoneSpring(
                Name: spring.Name,
                Center: center,
                Joints: resolvedJoints,
                ColliderGroups: resolvedGroups
            ));
        }

        var extension = new VrmcSpringBoneExtension(
            SpecVersion: "1.0",
            Colliders: resolvedColliders,
            ColliderGroups: resolvedColliderGroups,
            Springs: resolvedSprings
        );
        var report = new VrmcSpringBoneResolveReport(
            Version: 1,
            GlbPath: glbPath,
            NodeCount: nodeResolver.NodeCount,
            CandidateColliderCount: candidate.VrmExtensionDraft.Colliders.Count,
            ResolvedColliderCount: resolvedColliders.Count,
            CandidateSpringCount: candidate.VrmExtensionDraft.Springs.Count,
            ResolvedSpringCount: resolvedSprings.Count,
            CandidateJointCount: candidateJointCount,
            ResolvedJointCount: resolvedJointCount,
            SkippedColliders: skippedColliders,
            SkippedJoints: skippedJoints,
            PathMatches: pathMatches.Values
                .OrderBy(match => match.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings: warnings
        );

        return new VrmcSpringBoneBuildResult(extension, report);
    }

    private static ResolvedColliderKey BuildResolvedColliderKey(
        int nodeIndex,
        VrmcSpringBoneColliderShape shape
    )
    {
        if (shape.Sphere is not null)
        {
            return new ResolvedColliderKey(
                Node: nodeIndex,
                Kind: "sphere",
                Radius: shape.Sphere.Radius,
                OffsetX: FloatAt(shape.Sphere.Offset, 0),
                OffsetY: FloatAt(shape.Sphere.Offset, 1),
                OffsetZ: FloatAt(shape.Sphere.Offset, 2),
                TailX: 0f,
                TailY: 0f,
                TailZ: 0f
            );
        }

        if (shape.Capsule is not null)
        {
            return new ResolvedColliderKey(
                Node: nodeIndex,
                Kind: "capsule",
                Radius: shape.Capsule.Radius,
                OffsetX: FloatAt(shape.Capsule.Offset, 0),
                OffsetY: FloatAt(shape.Capsule.Offset, 1),
                OffsetZ: FloatAt(shape.Capsule.Offset, 2),
                TailX: FloatAt(shape.Capsule.Tail, 0),
                TailY: FloatAt(shape.Capsule.Tail, 1),
                TailZ: FloatAt(shape.Capsule.Tail, 2)
            );
        }

        return new ResolvedColliderKey(
            Node: nodeIndex,
            Kind: "none",
            Radius: 0f,
            OffsetX: 0f,
            OffsetY: 0f,
            OffsetZ: 0f,
            TailX: 0f,
            TailY: 0f,
            TailZ: 0f
        );
    }

    private static float FloatAt(float[] values, int index)
    {
        return index >= 0 && index < values.Length
            ? values[index]
            : 0f;
    }

    private static IReadOnlyList<VrmcRuntimeSpringJoint> BuildRuntimeJointChain(
        IReadOnlyList<VrmSpringBoneJointCandidate> sourceJoints
    )
    {
        var runtimeJoints = new List<VrmcRuntimeSpringJoint>();
        foreach (var joint in sourceJoints)
        {
            AddRuntimeJointIfNew(runtimeJoints, new VrmcRuntimeSpringJoint(
                Source: joint,
                Kind: "joint",
                NodeName: joint.NodeName,
                PrimaryPath: joint.NodePath,
                FallbackPath: string.IsNullOrWhiteSpace(joint.PivotNodePath)
                    ? null
                    : joint.PivotNodePath
            ));
        }

        return runtimeJoints;
    }

    private static void AddRuntimeJointIfNew(
        List<VrmcRuntimeSpringJoint> runtimeJoints,
        VrmcRuntimeSpringJoint joint
    )
    {
        if (string.IsNullOrWhiteSpace(joint.PrimaryPath))
        {
            return;
        }
        if (runtimeJoints.Count > 0 &&
            string.Equals(runtimeJoints[^1].PrimaryPath, joint.PrimaryPath, StringComparison.Ordinal))
        {
            return;
        }
        runtimeJoints.Add(joint);
    }

    private static bool TryResolveSpringJointNode(
        GltfNodePathResolver nodeResolver,
        VrmcRuntimeSpringJoint joint,
        out ResolvedGltfNode resolved,
        out VrmcSpringBonePathMatch? match,
        out string reason
    )
    {
        if (TryResolveNode(
                nodeResolver,
                joint.PrimaryPath,
                out resolved,
                out match,
                out reason))
        {
            return true;
        }

        if (
            !string.IsNullOrWhiteSpace(joint.FallbackPath) &&
            !string.Equals(joint.PrimaryPath, joint.FallbackPath, StringComparison.Ordinal) &&
            TryResolveNode(
                nodeResolver,
                joint.FallbackPath,
                out resolved,
                out match,
                out reason)
        )
        {
            return true;
        }

        return false;
    }

    private static bool TryResolveNode(
        GltfNodePathResolver resolver,
        string? sourcePath,
        out ResolvedGltfNode resolved,
        out VrmcSpringBonePathMatch? match,
        out string reason
    )
    {
        resolved = default;
        match = null;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            reason = "missing_source_path";
            return false;
        }

        if (!resolver.TryResolvePath(sourcePath, out resolved, out var mode))
        {
            reason = sourcePath.StartsWith("sit_body/", StringComparison.OrdinalIgnoreCase)
                ? "current_character_glb_exports_body_root_not_sit_body"
                : "node_path_not_found_in_character_glb";
            return false;
        }

        match = new VrmcSpringBonePathMatch(
            SourcePath: sourcePath,
            ResolvedNode: resolved.NodeIndex,
            ResolvedPath: resolved.NodePath,
            MatchMode: mode
        );
        reason = string.Empty;
        return true;
    }

    private static IReadOnlyDictionary<int, int> BuildParentMap(JsonArray nodes)
    {
        var parentByIndex = new Dictionary<int, int>();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not JsonObject node ||
                node["children"] is not JsonArray children)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (child?.GetValue<int>() is int childIndex)
                {
                    parentByIndex[childIndex] = i;
                }
            }
        }
        return parentByIndex;
    }

    private static IReadOnlyDictionary<string, int> BuildNodePathMap(
        JsonArray nodes,
        IReadOnlyDictionary<int, int> parentByIndex
    )
    {
        var nodeIndexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Count; i++)
        {
            var path = BuildNodePath(nodes, parentByIndex, i);
            if (!string.IsNullOrWhiteSpace(path))
            {
                nodeIndexByPath.TryAdd(path, i);
            }
        }
        return nodeIndexByPath;
    }

    private static string BuildNodePath(
        JsonArray nodes,
        IReadOnlyDictionary<int, int> parentByIndex,
        int nodeIndex
    )
    {
        var names = new Stack<string>();
        var current = nodeIndex;
        while (current >= 0 && current < nodes.Count && nodes[current] is JsonObject node)
        {
            var name = node["name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Push(name);
            }

            if (!parentByIndex.TryGetValue(current, out current))
            {
                break;
            }
        }
        return string.Join('/', names);
    }

    private static bool TryResolveRuntimePath(
        IReadOnlyDictionary<string, int> nodeIndexByPath,
        string sourcePath,
        out int nodeIndex
    )
    {
        foreach (var candidate in EnumerateRuntimePathCandidates(sourcePath))
        {
            if (nodeIndexByPath.TryGetValue(candidate, out nodeIndex))
            {
                return true;
            }
        }

        nodeIndex = -1;
        return false;
    }

    private static IEnumerable<string> EnumerateRuntimePathCandidates(string sourcePath)
    {
        yield return sourcePath;

        if (sourcePath.StartsWith("sit_body/", StringComparison.OrdinalIgnoreCase))
        {
            yield return "body/" + sourcePath["sit_body/".Length..];
        }

        const string faceHumanoidPrefix = "face/Position/Hip/Waist/Spine/Chest/Neck/Head";
        const string bodyHumanoidPrefix = "body/Position/PositionOffset/Hip/Waist/Spine/Chest/Neck/Head";
        if (sourcePath.StartsWith(faceHumanoidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return bodyHumanoidPrefix + sourcePath[faceHumanoidPrefix.Length..];
        }

        const string facePositionPrefix = "face/Position/Hip";
        const string bodyPositionPrefix = "body/Position/PositionOffset/Hip";
        if (sourcePath.StartsWith(facePositionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return bodyPositionPrefix + sourcePath[facePositionPrefix.Length..];
        }
    }

    private static bool HasAnyChild(JsonObject node)
    {
        return node["children"] is JsonArray children && children.Count > 0;
    }

    private static JsonArray EnsureArray(JsonObject root, string key)
    {
        if (root.TryGetPropertyValue(key, out var existing) && existing is JsonArray array)
        {
            return array;
        }

        array = new JsonArray();
        root[key] = array;
        return array;
    }

    private static string BuildSpringTailName(string? nodeName)
    {
        return $"{(string.IsNullOrWhiteSpace(nodeName) ? "spring" : nodeName)}_spring_tail";
    }

    private sealed record VrmcRuntimeSpringJoint(
        VrmSpringBoneJointCandidate Source,
        string Kind,
        string? NodeName,
        string? PrimaryPath,
        string? FallbackPath
    );

    private sealed record ResolvedColliderKey(
        int Node,
        string Kind,
        float Radius,
        float OffsetX,
        float OffsetY,
        float OffsetZ,
        float TailX,
        float TailY,
        float TailZ
    );
}
