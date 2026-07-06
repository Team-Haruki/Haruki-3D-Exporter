using System.Text.Json.Nodes;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class VrmSpringBoneCandidateBuilder
{
    private const float DefaultColliderRadius = 0.01f;
    private const float DefaultJointRadius = 0.05f;
    private const float DefaultCapsuleHeight = 0.05f;
    private static readonly RuntimeColliderFlagBinding[] RuntimeColliderFlagBindings =
    {
        new(0x01, "Hip", "CL_Hip"),
        new(0x02, "Chest", "CL_Chest"),
        new(0x04, "L_Arm", "CL_Left_Arm"),
        new(0x08, "R_Arm", "CL_Right_Arm"),
        new(0x10, "L_Elbow", "CL_Left_Elbow"),
        new(0x20, "R_Elbow", "CL_Right_Elbow"),
    };

    public VrmSpringBoneCandidate Build(CombinedSpringBoneExport raw)
    {
        var warnings = new List<string>();
        var colliders = new List<VrmSpringBoneColliderCandidate>();
        var colliderGroups = new List<VrmSpringBoneColliderGroupCandidate>();
        var springs = new List<VrmSpringBoneSpringCandidate>();
        var colliderIndexByKey = new Dictionary<SpringColliderKey, int>();
        var springBonePivots = BuildSpringBonePivots(raw.Body, raw.Head);

        AddColliders(raw.Body, colliders, colliderIndexByKey, warnings);
        AddColliders(raw.Head, colliders, colliderIndexByKey, warnings);
        var allForceProviders = raw.Body.ForceProviders
            .Concat(raw.Head.ForceProviders)
            .ToList();

        var bodySpringCount = AddSprings(
            raw.Body,
            colliderIndexByKey,
            null,
            allForceProviders,
            colliderGroups,
            springs,
            warnings
        );
        var headSpringCount = AddSprings(
            raw.Head,
            colliderIndexByKey,
            raw.Body,
            allForceProviders,
            colliderGroups,
            springs,
            warnings
        );

        return new VrmSpringBoneCandidate(
            Version: 1,
            Source: new VrmSpringBoneCandidateSource(
                BodyBundlePath: raw.Body.BundlePath,
                HeadBundlePath: raw.Head.BundlePath,
                NodeIndexStatus: "pending_final_gltf_node_indices",
                MappingStatus: "candidate_uses_source_transform_paths_and_path_ids"
            ),
            Normalization: new VrmSpringBoneNormalizationProfile(
                StiffnessFormula: "stiffness = clamp(raw.stiffnessForce / 300, 0, 4); raw values are preserved per joint",
                HitRadiusFormula: "hitRadius = raw.radius",
                DragForceFormula: "dragForce = clamp(raw.dragForce, 0, 1)",
                GravityFormula: "gravityPower = length(raw.springForce); gravityDir = normalize(raw.springForce), fallback [0,-1,0] when zero",
                CapsuleTailFormula: "tail = [0, raw.height, 0]; AssetStudio/SharpGLTF conversion maps Sekai capsule length to GLB local Y for arm colliders"
            ),
            VrmExtensionDraft: new VrmSpringBoneExtensionDraft(
                SpecVersion: "VRMC_springBone-1.0-draft-candidate",
                SpringBonePivots: springBonePivots,
                Colliders: colliders,
                ColliderGroups: colliderGroups,
                Springs: springs
            ),
            PartSummaries: new[]
            {
                BuildPartSummary(raw.Body, bodySpringCount),
                BuildPartSummary(raw.Head, headSpringCount),
            },
            Warnings: warnings
        );
    }

    private static IReadOnlyList<VrmSpringBonePivotCandidate> BuildSpringBonePivots(
        params SpringBoneExport[] parts
    )
    {
        var pivots = new List<VrmSpringBonePivotCandidate>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in parts)
        {
            foreach (var pivot in part.SpringBonePivots)
            {
                AddSpringBonePivot(
                    pivots,
                    keys,
                    new VrmSpringBonePivotCandidate(
                        PartKind: part.PartKind,
                        SourcePathId: pivot.GameObject?.PathId ?? pivot.PathId,
                        ScriptName: string.IsNullOrWhiteSpace(pivot.ScriptName)
                            ? "SpringBonePivot"
                            : pivot.ScriptName,
                        NodeName: pivot.GameObject?.Name,
                        NodePath: pivot.GameObject?.TransformPath
                    )
                );
            }

            foreach (var pivotNode in part.Bones.Select(bone => bone.PivotNode))
            {
                if (pivotNode is null)
                {
                    continue;
                }

                AddSpringBonePivot(
                    pivots,
                    keys,
                    new VrmSpringBonePivotCandidate(
                        PartKind: part.PartKind,
                        SourcePathId: pivotNode.PathId,
                        ScriptName: "SpringBonePivot",
                        NodeName: pivotNode.Name,
                        NodePath: pivotNode.TransformPath
                    )
                );
            }
        }

        return pivots
            .OrderBy(pivot => pivot.PartKind, StringComparer.Ordinal)
            .ThenBy(pivot => pivot.NodePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(pivot => pivot.SourcePathId)
            .ToList();
    }

    private static void AddSpringBonePivot(
        List<VrmSpringBonePivotCandidate> pivots,
        HashSet<string> keys,
        VrmSpringBonePivotCandidate pivot
    )
    {
        if (string.IsNullOrWhiteSpace(pivot.NodePath) && string.IsNullOrWhiteSpace(pivot.NodeName))
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(pivot.NodePath)
            ? $"{pivot.PartKind}:pathId:{pivot.SourcePathId}"
            : $"{pivot.PartKind}:path:{pivot.NodePath}";
        if (!keys.Add(key))
        {
            return;
        }

        pivots.Add(pivot);
    }

    private static void AddColliders(
        SpringBoneExport part,
        List<VrmSpringBoneColliderCandidate> colliders,
        Dictionary<SpringColliderKey, int> colliderIndexByKey,
        List<string> warnings
    )
    {
        foreach (var collider in part.SphereColliders)
        {
            AddCollider(
                part.PartKind,
                collider,
                BuildSphereShape(collider),
                colliders,
                colliderIndexByKey
            );
        }

        foreach (var collider in part.CapsuleColliders)
        {
            AddCollider(
                part.PartKind,
                collider,
                BuildCapsuleShape(collider),
                colliders,
                colliderIndexByKey
            );
        }

        foreach (var collider in part.PanelColliders)
        {
            AddCollider(
                part.PartKind,
                collider,
                BuildPanelShape(collider),
                colliders,
                colliderIndexByKey
            );
            warnings.Add(
                $"{part.PartKind}: SpringPanelCollider {BuildObjectName(collider.GameObject, collider.ScriptName, collider.PathId)} was preserved for PJSK runtime; VRMC_springBone has no direct panel collider shape."
            );
        }
    }

    private static void AddCollider(
        string partKind,
        SpringColliderEntry collider,
        VrmSpringBoneColliderShapeCandidate shape,
        List<VrmSpringBoneColliderCandidate> colliders,
        Dictionary<SpringColliderKey, int> colliderIndexByKey
    )
    {
        var key = new SpringColliderKey(partKind, collider.PathId);
        if (colliderIndexByKey.ContainsKey(key))
        {
            return;
        }

        var index = colliders.Count;
        var name = BuildObjectName(collider.GameObject, collider.ScriptName, collider.PathId);
        colliders.Add(new VrmSpringBoneColliderCandidate(
            Index: index,
            PartKind: partKind,
            Name: $"{partKind}:{name}",
            SourcePathId: collider.PathId,
            ScriptName: collider.ScriptName,
            Enabled: (ReadBool(collider.Raw, "m_Enabled") ?? true) &&
                collider.LinkedRendererEnabled != false,
            LinkedRenderer: ToCandidateObjectRef(collider.LinkedRenderer),
            LinkedRendererEnabled: collider.LinkedRendererEnabled,
            Node: null,
            NodeName: collider.GameObject?.Name,
            NodePath: collider.GameObject?.TransformPath,
            PoseRoot: ExtractPoseRoot(collider.GameObject?.TransformPath),
            Shape: shape
        ));
        colliderIndexByKey[key] = index;
    }

    private static int AddSprings(
        SpringBoneExport part,
        IReadOnlyDictionary<SpringColliderKey, int> colliderIndexByKey,
        SpringBoneExport? bodyPartForInferredColliders,
        IReadOnlyList<SpringMonoBehaviourEntry> fallbackForceProviders,
        List<VrmSpringBoneColliderGroupCandidate> colliderGroups,
        List<VrmSpringBoneSpringCandidate> springs,
        List<string> warnings
    )
    {
        var springCountBefore = springs.Count;
        var bonesByPathId = part.Bones.ToDictionary(bone => bone.PathId);

        foreach (var manager in part.Managers)
        {
            var managerBones = ResolveRuntimeManagerBones(part, manager, bonesByPathId, warnings);

            foreach (var chain in BuildChains(managerBones))
            {
                var jointColliderGroups = new Dictionary<long, IReadOnlyList<int>>();
                var jointColliderGroupsByNodePath = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
                var springColliderGroups = new SortedSet<int>();
                var joints = new List<VrmSpringBoneJointCandidate>();

                foreach (var bone in chain.Bones)
                {
                    var colliderPathIds = ReadColliderPathIds(bone.Raw).ToList();
                    var colliderIndexes = new List<int>();

                    foreach (var colliderPathId in colliderPathIds)
                    {
                        if (colliderIndexByKey.TryGetValue(
                                new SpringColliderKey(part.PartKind, colliderPathId),
                                out var colliderIndex))
                        {
                            colliderIndexes.Add(colliderIndex);
                        }
                        else
                        {
                            warnings.Add(
                                $"{part.PartKind}: SpringBone {BuildObjectName(bone.GameObject, bone.ScriptName, bone.PathId)} references unsupported or missing collider PathID {colliderPathId}."
                            );
                        }
                    }

                    if (colliderIndexes.Count > 0)
                    {
                        var groupIndex = colliderGroups.Count;
                        colliderGroups.Add(new VrmSpringBoneColliderGroupCandidate(
                            Index: groupIndex,
                            Name: $"{part.PartKind}:{BuildObjectName(bone.GameObject, bone.ScriptName, bone.PathId)}:colliders",
                            PartKind: part.PartKind,
                            SourceSpringBonePathId: bone.PathId,
                            Colliders: colliderIndexes,
                            SourceColliderPathIds: colliderPathIds,
                            SourceKind: "direct",
                            ColliderFlag: null,
                            MatchedPrefixes: null,
                            CollidersByRoot: null,
                            DefaultRoot: ExtractPoseRoot(bone.GameObject?.TransformPath)
                        ));
                        springColliderGroups.Add(groupIndex);
                        SetJointColliderGroups(
                            jointColliderGroups,
                            jointColliderGroupsByNodePath,
                            bone,
                            new[] { groupIndex }
                        );
                    }
                    else
                    {
                        SetJointColliderGroups(
                            jointColliderGroups,
                            jointColliderGroupsByNodePath,
                            bone,
                            Array.Empty<int>()
                        );
                    }

                    joints.Add(BuildJoint(bone));
                }

                if (joints.Count == 0)
                {
                    continue;
                }

                foreach (var bone in chain.Bones)
                {
                    var flag = ReadInt(bone.Raw, "colliderFlag") ?? 0;
                    if (flag == 0)
                    {
                        continue;
                    }

                    var runtimeColliderFlagGroup = BuildRuntimeColliderFlagGroup(
                        part,
                        chain,
                        bone,
                        flag,
                        bodyPartForInferredColliders,
                        colliderIndexByKey,
                        colliderGroups.Count,
                        warnings
                    );
                    if (runtimeColliderFlagGroup is null)
                    {
                        continue;
                    }

                    colliderGroups.Add(runtimeColliderFlagGroup);
                    springColliderGroups.Add(runtimeColliderFlagGroup.Index);
                    var updatedJointGroups = jointColliderGroups[bone.PathId]
                        .Concat(new[] { runtimeColliderFlagGroup.Index })
                        .ToList();
                    SetJointColliderGroups(
                        jointColliderGroups,
                        jointColliderGroupsByNodePath,
                        bone,
                        updatedJointGroups
                    );
                }

                var springIndex = springs.Count;
                springs.Add(new VrmSpringBoneSpringCandidate(
                    Index: springIndex,
                    Name: $"{part.PartKind}:{chain.RootName}",
                    PartKind: part.PartKind,
                    SourceManagerPathId: manager.PathId,
                    Enabled: ReadBool(manager.Raw, "m_Enabled") ?? true,
                    AutomaticUpdates: ReadBool(manager.Raw, "automaticUpdates") ?? true,
                    EnableLengthLimits: ReadBool(manager.Raw, "enableLengthLimits") ?? true,
                    EnableAngleLimits: ReadBool(manager.Raw, "enableAngleLimits") ?? true,
                    EnableCollision: ReadBool(manager.Raw, "enableCollision") ?? true,
                    CollideWithGround: ReadBool(manager.Raw, "collideWithGround") ?? true,
                    GroundHeight: ReadFloat(manager.Raw, "groundHeight") ?? 0f,
                    IsSumOfForcesOnBone: ReadBool(manager.Raw, "isSumOfForcesOnBone") ?? true,
                    IsPaused: ReadBool(manager.Raw, "isPaused") ?? false,
                    DynamicRatio: Clamp01(ReadFloat(manager.Raw, "dynamicRatio") ?? 0.5f),
                    SimulationFrameRate: Math.Max(0, ReadInt(manager.Raw, "simulationFrameRate") ?? 60),
                    SlowMotionScale: ReadFloat(manager.Raw, "slowMotionScale") ?? 1f,
                    Bounce: ReadFloat(manager.Raw, "bounce") ?? 0f,
                    Friction: ReadFloat(manager.Raw, "friction") ?? 1f,
                    AnimatedBoneNames: ReadStringList(manager.Raw, "animatedBoneNames"),
                    RawGravity: ReadVector3(manager.Raw, "gravity"),
                    ForceProviders: BuildForceProviders(part, fallbackForceProviders, manager),
                    Center: null,
                    CenterName: manager.GameObject?.Name,
                    CenterPath: manager.GameObject?.TransformPath,
                    Joints: joints,
                    ColliderGroups: springColliderGroups.ToList(),
                    JointColliderGroups: jointColliderGroups,
                    JointColliderGroupsByNodePath: jointColliderGroupsByNodePath
                ));
            }
        }

        return springs.Count - springCountBefore;
    }

    private static void SetJointColliderGroups(
        Dictionary<long, IReadOnlyList<int>> jointColliderGroups,
        Dictionary<string, IReadOnlyList<int>> jointColliderGroupsByNodePath,
        SpringBoneEntry bone,
        IReadOnlyList<int> groups
    )
    {
        jointColliderGroups[bone.PathId] = groups;
        var nodePath = bone.GameObject?.TransformPath;
        if (!string.IsNullOrWhiteSpace(nodePath))
        {
            jointColliderGroupsByNodePath[nodePath] = groups;
        }
    }

    private static IReadOnlyList<SpringBoneEntry> ResolveRuntimeManagerBones(
        SpringBoneExport part,
        SpringMonoBehaviourEntry manager,
        IReadOnlyDictionary<long, SpringBoneEntry> bonesByPathId,
        List<string> warnings
    )
    {
        var managerPath = manager.GameObject?.TransformPath;
        if (!string.IsNullOrWhiteSpace(managerPath))
        {
            var runtimeBones = part.Bones
                .Where(bone => IsSameOrDescendant(bone.GameObject?.TransformPath, managerPath))
                .OrderBy(bone => PathDepth(bone.GameObject?.TransformPath))
                .ThenBy(bone => bone.GameObject?.TransformPath, StringComparer.Ordinal)
                .ToList();

            if (runtimeBones.Count == 0)
            {
                warnings.Add(
                    $"{part.PartKind}: SpringManager {manager.PathId} has no SpringBone under {managerPath}; runtime FindSpringBones(true) would return none."
                );
            }

            return runtimeBones;
        }

        warnings.Add(
            $"{part.PartKind}: SpringManager {manager.PathId} has no transform path; falling back to serialized springBones."
        );
        return ReadSerializedManagerBones(part, manager, bonesByPathId, warnings);
    }

    private static IReadOnlyList<SpringBoneEntry> ReadSerializedManagerBones(
        SpringBoneExport part,
        SpringMonoBehaviourEntry manager,
        IReadOnlyDictionary<long, SpringBoneEntry> bonesByPathId,
        List<string> warnings
    )
    {
        return ReadObjectPathIds(manager.Raw, "springBones")
            .Select(pathId =>
            {
                if (bonesByPathId.TryGetValue(pathId, out var bone))
                {
                    return bone;
                }
                warnings.Add(
                    $"{part.PartKind}: SpringManager {manager.PathId} references missing SpringBone PathID {pathId}."
                );
                return null;
            })
            .Where(bone => bone is not null)
            .Cast<SpringBoneEntry>()
            .ToList();
    }

    private static VrmSpringBoneColliderGroupCandidate? BuildRuntimeColliderFlagGroup(
        SpringBoneExport part,
        SpringBoneChain chain,
        SpringBoneEntry bone,
        int colliderFlag,
        SpringBoneExport? bodyPart,
        IReadOnlyDictionary<SpringColliderKey, int> colliderIndexByKey,
        int groupIndex,
        List<string> warnings
    )
    {
        var colliderSourcePart = bodyPart ?? part;

        var matchedBindings = RuntimeColliderFlagBindings
            .Where(binding => (colliderFlag & binding.Mask) != 0)
            .ToList();
        var sourceColliders = colliderSourcePart.CapsuleColliders
            .Concat(colliderSourcePart.SphereColliders)
            .Where(collider => collider.GameObject?.ActiveInHierarchy != false)
            .Where(collider => MatchesRuntimeColliderFlag(collider, matchedBindings))
            .ToList();
        var colliderIndexesByCollider = sourceColliders
            .Select(collider => new
            {
                Collider = collider,
                Index = colliderIndexByKey.TryGetValue(
                    new SpringColliderKey(colliderSourcePart.PartKind, collider.PathId),
                    out var index)
                    ? index
                    : (int?)null
            })
            .Where(item => item.Index is not null)
            .ToList();
        var collidersByRoot = colliderIndexesByCollider
            .GroupBy(
                item => ExtractPoseRoot(item.Collider.GameObject?.TransformPath) ?? "unknown",
                StringComparer.Ordinal
            )
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group
                    .Select(item => item.Index!.Value)
                    .Distinct()
                    .ToList(),
                StringComparer.Ordinal
            );
        var defaultRoot = PreferredRuntimeColliderRoot(part, bone);
        var colliderIndexes = defaultRoot is not null &&
            collidersByRoot.TryGetValue(defaultRoot, out var defaultRootIndexes) &&
            defaultRootIndexes.Count > 0
                ? defaultRootIndexes.ToList()
                : colliderIndexesByCollider
                    .Select(item => item.Index!.Value)
                    .Distinct()
                    .ToList();

        if (colliderIndexes.Count == 0)
        {
            var prefixes = string.Join(", ", matchedBindings.Select(binding => binding.Prefix));
            warnings.Add(
                $"{part.PartKind}: Spring chain {chain.RootName} has colliderFlag {colliderFlag}, but no body colliders matched runtime CL_* prefixes [{prefixes}]."
            );
            return null;
        }

        var sourcePathIds = sourceColliders
            .Select(collider => collider.PathId)
            .ToList();

        return new VrmSpringBoneColliderGroupCandidate(
            Index: groupIndex,
            Name: $"{part.PartKind}:{chain.RootName}:{BuildObjectName(bone.GameObject, bone.ScriptName, bone.PathId)}:colliderFlag:{colliderFlag}:runtime_body",
            PartKind: colliderSourcePart.PartKind,
            SourceSpringBonePathId: bone.PathId,
            Colliders: colliderIndexes,
            SourceColliderPathIds: sourcePathIds,
            SourceKind: "colliderFlag",
            ColliderFlag: colliderFlag,
            MatchedPrefixes: matchedBindings.Select(binding => binding.Prefix).ToList(),
            CollidersByRoot: collidersByRoot.Count == 0 ? null : collidersByRoot,
            DefaultRoot: defaultRoot
        );
    }

    private static List<SpringColliderEntry> PreferMatchingPoseColliders(
        SpringBoneExport part,
        SpringBoneEntry bone,
        List<SpringColliderEntry> colliders
    )
    {
        var preferredRoot = PreferredColliderRoot(part, bone);
        if (preferredRoot is null)
        {
            return colliders;
        }

        var collidersByName = colliders
            .GroupBy(collider => collider.GameObject?.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        return colliders
            .Where(collider =>
            {
                var name = collider.GameObject?.Name ?? string.Empty;
                if (!collidersByName.TryGetValue(name, out var sameNameColliders) ||
                    sameNameColliders.Count <= 1)
                {
                    return true;
                }

                var hasPreferredPose = sameNameColliders.Any(item =>
                    item.GameObject?.TransformPath?.StartsWith(preferredRoot, StringComparison.Ordinal) == true);
                return !hasPreferredPose ||
                    collider.GameObject?.TransformPath?.StartsWith(preferredRoot, StringComparison.Ordinal) == true;
            })
            .ToList();
    }

    private static string? PreferredColliderRoot(SpringBoneExport part, SpringBoneEntry bone)
    {
        var bonePath = bone.GameObject?.TransformPath;
        if (bonePath?.StartsWith("sit_body/", StringComparison.Ordinal) == true)
        {
            return "sit_body/";
        }
        if (bonePath?.StartsWith("body/", StringComparison.Ordinal) == true)
        {
            return "body/";
        }
        return null;
    }

    private static string? PreferredRuntimeColliderRoot(SpringBoneExport part, SpringBoneEntry bone)
    {
        var poseRoot = ExtractPoseRoot(bone.GameObject?.TransformPath);
        if (poseRoot is "body" or "sit_body" or "guitar_body")
        {
            return poseRoot;
        }
        return string.Equals(part.PartKind, "Head", StringComparison.OrdinalIgnoreCase) ||
            poseRoot == "face"
                ? "body"
                : null;
    }

    private static string? ExtractPoseRoot(string? transformPath)
    {
        if (string.IsNullOrWhiteSpace(transformPath))
        {
            return null;
        }
        var slashIndex = transformPath.IndexOf('/');
        return slashIndex < 0
            ? transformPath
            : transformPath[..slashIndex];
    }

    private static bool MatchesRuntimeColliderFlag(
        SpringColliderEntry collider,
        IReadOnlyList<RuntimeColliderFlagBinding> matchedBindings
    )
    {
        var name = collider.GameObject?.Name ?? string.Empty;
        return matchedBindings.Any(binding =>
            name.StartsWith(binding.Prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static VrmSpringBonePartSummary BuildPartSummary(
        SpringBoneExport part,
        int springCount
    )
    {
        return new VrmSpringBonePartSummary(
            PartKind: part.PartKind,
            ManagerCount: part.Managers.Count,
            SpringBoneCount: part.Bones.Count,
            SphereColliderCount: part.SphereColliders.Count,
            CapsuleColliderCount: part.CapsuleColliders.Count,
            PanelColliderCount: part.PanelColliders.Count,
            SpringCount: springCount
        );
    }

    private static VrmSpringBoneJointCandidate BuildJoint(SpringBoneEntry bone)
    {
        var springForce = bone.SpringForce ?? new SpringVector3(0f, 0f, 0f);
        var gravityPower = Magnitude(springForce);
        var gravityDir = gravityPower > 0.00001f
            ? new[]
            {
                springForce.X / gravityPower,
                springForce.Y / gravityPower,
                springForce.Z / gravityPower,
            }
            : new[] { 0f, -1f, 0f };

        return new VrmSpringBoneJointCandidate(
            Node: null,
            NodeName: bone.GameObject?.Name,
            NodePath: bone.GameObject?.TransformPath,
            SourcePathId: bone.PathId,
            PivotNodeName: bone.PivotNode?.Name,
            PivotNodePath: bone.PivotNode?.TransformPath,
            PivotSourcePathId: bone.PivotNode?.PathId,
            HitRadius: MathF.Max(0f, bone.Radius ?? DefaultJointRadius),
            Stiffness: Clamp((bone.StiffnessForce ?? 300f) / 300f, 0f, 4f),
            DragForce: Clamp(bone.DragForce ?? 0.4f, 0f, 1f),
            GravityPower: gravityPower,
            GravityDir: gravityDir,
            Enabled: ReadBool(bone.Raw, "m_Enabled") ?? true,
            RawStiffnessForce: bone.StiffnessForce,
            RawDragForce: bone.DragForce,
            RawSpringForce: bone.SpringForce,
            RawWindInfluence: bone.WindInfluence,
            RawAngularStiffness: ReadFloat(bone.Raw, "angularStiffness"),
            RawSpringConstant: ReadFloat(bone.Raw, "SpringConstant") ??
                ReadFloat(bone.Raw, "springConstant"),
            LengthLimitTargets: bone.LengthLimitTargets
                .Select(target => new VrmSpringBoneLengthLimitTargetCandidate(
                    NodeName: target.Name,
                    NodePath: target.TransformPath,
                    SourcePathId: target.PathId
                ))
                .ToList(),
            RawAngleLimits: new VrmSpringBoneAngleLimitsCandidate(
                Y: ReadAxisLimit(bone.Raw, "yAngleLimits"),
                Z: ReadAxisLimit(bone.Raw, "zAngleLimits")
            )
        );
    }

    private static IReadOnlyList<VrmSpringBoneForceProviderCandidate> BuildForceProviders(
        SpringBoneExport part,
        IReadOnlyList<SpringMonoBehaviourEntry> fallbackForceProviders,
        SpringMonoBehaviourEntry manager
    )
    {
        _ = part;
        _ = manager;
        return fallbackForceProviders
            .Where(provider => provider is not null)
            .Cast<SpringMonoBehaviourEntry>()
            .Select(provider => new VrmSpringBoneForceProviderCandidate(
                SourcePathId: provider.PathId,
                ScriptName: provider.ScriptName,
                NodeName: provider.GameObject?.Name,
                NodePath: provider.GameObject?.TransformPath,
                ActiveSelf: provider.GameObject?.ActiveSelf,
                ActiveInHierarchy: provider.GameObject?.ActiveInHierarchy,
                SpringManagerPathId: ReadWindVolumeSpringManagerPathId(provider.Raw),
                Raw: provider.Raw
            ))
            .ToList();
    }

    private static long? ReadWindVolumeSpringManagerPathId(JsonObject raw)
    {
        foreach (var key in new[]
                 {
                     "<SpringManager>k__BackingField",
                     "_SpringManager_k__BackingField",
                     "SpringManager",
                     "springManager",
                 })
        {
            var pathId = ReadObjectPathId(raw, key);
            if (pathId is not null)
            {
                return pathId;
            }
        }
        return null;
    }

    private static VrmSpringBoneObjectRefCandidate? ToCandidateObjectRef(SpringObjectRef? source)
    {
        return source is null
            ? null
            : new VrmSpringBoneObjectRefCandidate(
                FileId: source.FileId,
                PathId: source.PathId,
                Name: source.Name,
                TransformPath: source.TransformPath
            );
    }

    private static IReadOnlyList<SpringBoneChain> BuildChains(
        IReadOnlyList<SpringBoneEntry> bones
    )
    {
        if (bones.Count == 0)
        {
            return Array.Empty<SpringBoneChain>();
        }

        var orderByPathId = bones
            .Select((bone, index) => new { bone.PathId, index })
            .ToDictionary(entry => entry.PathId, entry => entry.index);
        var nodePaths = bones
            .SelectMany(bone => new[] { bone.PivotNode?.TransformPath, bone.GameObject?.TransformPath })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var rootPaths = nodePaths
            .Where(path =>
            {
                var parent = ParentPath(path);
                return parent is null || !nodePaths.Contains(parent);
            })
            .OrderBy(PathDepth)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (rootPaths.Count == 0)
        {
            return new[]
            {
                new SpringBoneChain(
                    RootPath: "unresolved",
                    RootName: "unresolved",
                    Bones: OrderChainBones(bones, orderByPathId).ToList()
                ),
            };
        }

        var assigned = new HashSet<long>();
        var chains = new List<SpringBoneChain>();
        foreach (var rootPath in rootPaths)
        {
            var chainBones = bones
                .Where(bone => !assigned.Contains(bone.PathId))
                .Where(bone =>
                    IsSameOrDescendant(bone.GameObject?.TransformPath, rootPath) ||
                    IsSameOrDescendant(bone.PivotNode?.TransformPath, rootPath))
                .ToList();

            if (chainBones.Count == 0)
            {
                continue;
            }

            foreach (var bone in chainBones)
            {
                assigned.Add(bone.PathId);
            }

            chains.Add(new SpringBoneChain(
                RootPath: rootPath,
                RootName: Path.GetFileName(rootPath),
                Bones: OrderChainBones(chainBones, orderByPathId).ToList()
            ));
        }

        var leftovers = bones
            .Where(bone => !assigned.Contains(bone.PathId))
            .ToList();
        if (leftovers.Count > 0)
        {
            chains.Add(new SpringBoneChain(
                RootPath: "unresolved",
                RootName: "unresolved",
                Bones: OrderChainBones(leftovers, orderByPathId).ToList()
            ));
        }

        return chains;
    }

    private static IEnumerable<SpringBoneEntry> OrderChainBones(
        IEnumerable<SpringBoneEntry> bones,
        IReadOnlyDictionary<long, int> orderByPathId
    )
    {
        return bones
            .OrderBy(bone => PathDepth(bone.GameObject?.TransformPath))
            .ThenBy(bone => orderByPathId.TryGetValue(bone.PathId, out var order) ? order : int.MaxValue);
    }

    private static VrmSpringBoneColliderShapeCandidate BuildSphereShape(
        SpringColliderEntry collider
    )
    {
        return new VrmSpringBoneColliderShapeCandidate(
            Sphere: new VrmSpringBoneSphereColliderCandidate(
                Offset: ToArray(collider.Center),
                Radius: MathF.Max(0f, collider.Radius ?? DefaultColliderRadius)
            ),
            Capsule: null,
            Panel: null
        );
    }

    private static VrmSpringBoneColliderShapeCandidate BuildCapsuleShape(
        SpringColliderEntry collider
    )
    {
        var height = MathF.Max(0f, collider.Height ?? DefaultCapsuleHeight);
        return new VrmSpringBoneColliderShapeCandidate(
            Sphere: null,
            Capsule: new VrmSpringBoneCapsuleColliderCandidate(
                Offset: ToArray(collider.Center),
                Radius: MathF.Max(0f, collider.Radius ?? DefaultColliderRadius),
                Tail: new[] { 0f, height, 0f }
            ),
            Panel: null
        );
    }

    private static VrmSpringBoneColliderShapeCandidate BuildPanelShape(
        SpringColliderEntry collider
    )
    {
        return new VrmSpringBoneColliderShapeCandidate(
            Sphere: null,
            Capsule: null,
            Panel: new VrmSpringBonePanelColliderCandidate(
                Width: MathF.Max(0f, ReadFloat(collider.Raw, "width") ?? collider.Radius ?? 0f),
                Height: MathF.Max(0f, ReadFloat(collider.Raw, "height") ?? collider.Height ?? 0f)
            )
        );
    }

    private static IEnumerable<long> ReadColliderPathIds(JsonObject raw)
    {
        foreach (var pathId in ReadObjectPathIds(raw, "colliders"))
        {
            yield return pathId;
        }
        foreach (var pathId in ReadObjectPathIds(raw, "sphereColliders"))
        {
            yield return pathId;
        }
        foreach (var pathId in ReadObjectPathIds(raw, "capsuleColliders"))
        {
            yield return pathId;
        }
        foreach (var pathId in ReadObjectPathIds(raw, "panelColliders"))
        {
            yield return pathId;
        }
    }

    private static IEnumerable<long> ReadObjectPathIds(JsonObject raw, string key)
    {
        if (!TryGetProperty(raw, key, out var value) || value is not JsonArray array)
        {
            yield break;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject obj)
            {
                continue;
            }
            var pathId = ReadLong(obj, "m_PathID") ?? 0;
            if (pathId != 0)
            {
                yield return pathId;
            }
        }
    }

    private static long? ReadObjectPathId(JsonObject raw, string key)
    {
        if (!TryGetProperty(raw, key, out var value) || value is not JsonObject obj)
        {
            return null;
        }
        var pathId = ReadLong(obj, "m_PathID") ?? 0;
        return pathId != 0 ? pathId : null;
    }

    private static VrmSpringBoneAxisLimitCandidate? ReadAxisLimit(
        JsonObject raw,
        string key
    )
    {
        if (!TryGetProperty(raw, key, out var value) || value is not JsonObject obj)
        {
            return null;
        }

        return new VrmSpringBoneAxisLimitCandidate(
            Active: ReadBool(obj, "active") ?? false,
            Min: ReadFloat(obj, "min"),
            Max: ReadFloat(obj, "max")
        );
    }

    private static float? ReadFloat(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is null)
        {
            return null;
        }
        if (value.GetValueKind() == System.Text.Json.JsonValueKind.Number &&
            value.AsValue().TryGetValue<float>(out var result))
        {
            return result;
        }
        return null;
    }

    private static SpringVector3? ReadVector3(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is not JsonObject vector)
        {
            return null;
        }

        var x = ReadFloat(vector, "x") ?? ReadFloat(vector, "X");
        var y = ReadFloat(vector, "y") ?? ReadFloat(vector, "Y");
        var z = ReadFloat(vector, "z") ?? ReadFloat(vector, "Z");
        return x is null || y is null || z is null
            ? null
            : new SpringVector3(x.Value, y.Value, z.Value);
    }

    private static IReadOnlyList<string> ReadStringList(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is not JsonArray array)
        {
            return Array.Empty<string>();
        }

        return array
            .Select(item => item?.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? item.GetValue<string>()
                : null)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();
    }

    private static bool? ReadBool(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is null)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number when value.AsValue().TryGetValue<int>(out var result) => result != 0,
            System.Text.Json.JsonValueKind.Number when value.AsValue().TryGetValue<long>(out var result) => result != 0,
            System.Text.Json.JsonValueKind.Number when value.AsValue().TryGetValue<float>(out var result) => MathF.Abs(result) > 0.00001f,
            _ => null,
        };
    }

    private static long? ReadLong(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is null)
        {
            return null;
        }
        return value.GetValueKind() == System.Text.Json.JsonValueKind.Number &&
            value.AsValue().TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static bool TryGetProperty(JsonObject obj, string key, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(key, out value))
        {
            return true;
        }
        var pair = obj.FirstOrDefault(entry =>
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(pair.Key))
        {
            value = pair.Value;
            return true;
        }
        value = null;
        return false;
    }

    private static int? ReadInt(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is null)
        {
            return null;
        }
        if (value.GetValueKind() != System.Text.Json.JsonValueKind.Number)
        {
            return null;
        }

        if (value.AsValue().TryGetValue<int>(out var intResult))
        {
            return intResult;
        }
        if (value.AsValue().TryGetValue<long>(out var longResult))
        {
            return longResult >= int.MinValue && longResult <= int.MaxValue
                ? (int)longResult
                : null;
        }
        if (value.AsValue().TryGetValue<float>(out var floatResult))
        {
            return floatResult >= int.MinValue && floatResult <= int.MaxValue
                ? (int)floatResult
                : null;
        }
        return null;
    }

    private static float Clamp01(float value)
    {
        return value < 0f ? 0f : value > 1f ? 1f : value;
    }

    private static string? ParentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var index = path.LastIndexOf('/');
        return index <= 0 ? null : path[..index];
    }

    private static int PathDepth(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? int.MaxValue
            : path.Count(character => character == '/');
    }

    private static bool IsSameOrDescendant(string? path, string rootPath)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            (string.Equals(path, rootPath, StringComparison.Ordinal) ||
             path.StartsWith($"{rootPath}/", StringComparison.Ordinal));
    }

    private static string BuildObjectName(
        SpringObjectRef? reference,
        string fallbackPrefix,
        long pathId
    )
    {
        return !string.IsNullOrWhiteSpace(reference?.Name)
            ? reference.Name
            : $"{fallbackPrefix}:{pathId}";
    }

    private static float[] ToArray(SpringVector3? vector)
    {
        return vector is null
            ? new[] { 0f, 0f, 0f }
            : new[] { vector.X, vector.Y, vector.Z };
    }

    private static float Magnitude(SpringVector3 vector)
    {
        return MathF.Sqrt(
            vector.X * vector.X +
            vector.Y * vector.Y +
            vector.Z * vector.Z
        );
    }

    private static float Clamp(float value, float min, float max)
    {
        return MathF.Min(max, MathF.Max(min, value));
    }

    private sealed record SpringColliderKey(string PartKind, long PathId);

    private sealed record RuntimeColliderFlagBinding(int Mask, string Label, string Prefix);

    private sealed record SpringBoneChain(
        string RootPath,
        string RootName,
        IReadOnlyList<SpringBoneEntry> Bones
    );
}
