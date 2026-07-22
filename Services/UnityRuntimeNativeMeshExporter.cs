using AssetStudio;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class UnityRuntimeNativeMeshExporter
{
    public PjskUnityRuntimeNativeMeshSet ExportSinglePart(
        string partKind,
        IImported imported,
        SpringPrefabGraph graph,
        IReadOnlyCollection<string> activeRoots
    )
    {
        var warnings = new List<string>();
        var meshes = ExportPart(partKind, imported, graph, activeRoots, warnings);
        return new PjskUnityRuntimeNativeMeshSet(
            Version: "0414",
            CoordinateSpace: "assetstudio-modelconverter-viewer-space",
            Meshes: meshes,
            Warnings: warnings
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToList()
        );
    }

    public PjskUnityRuntimeNativeMeshSet Export(
        IImported bodyImported,
        IImported headImported,
        PjskSpringBoneRuntimeUnitySetup runtimeUnitySetup,
        IImported? accessoryImported = null,
        string? accessoryAttachNodeName = null
    )
    {
        var warnings = new List<string>();
        var meshes = new List<PjskUnityRuntimeNativeMesh>();
        var bodyGraph = runtimeUnitySetup.PrefabGraphs
            .FirstOrDefault(graph => string.Equals(graph.PartKind, "Body", StringComparison.OrdinalIgnoreCase));
        var headGraph = runtimeUnitySetup.PrefabGraphs
            .FirstOrDefault(graph => string.Equals(graph.PartKind, "Head", StringComparison.OrdinalIgnoreCase));

        if (bodyGraph is null)
        {
            warnings.Add("Body prefab graph is missing; native body meshes were not exported.");
        }
        else
        {
            meshes.AddRange(ExportPart("Body", bodyImported, bodyGraph, runtimeUnitySetup.ActiveRootProfile.ActiveRoots, warnings));
        }

        if (headGraph is null)
        {
            warnings.Add("Head prefab graph is missing; native head meshes were not exported.");
        }
        else
        {
            meshes.AddRange(ExportPart("Head", headImported, headGraph, runtimeUnitySetup.ActiveRootProfile.ActiveRoots, warnings));
        }

        if (accessoryImported is not null)
        {
            meshes.AddRange(ExportAccessoryPart(
                accessoryImported,
                runtimeUnitySetup,
                accessoryAttachNodeName,
                warnings
            ));
        }

        return new PjskUnityRuntimeNativeMeshSet(
            Version: "0414",
            CoordinateSpace: "assetstudio-modelconverter-viewer-space",
            Meshes: meshes,
            Warnings: warnings
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToList()
        );
    }

    private static IReadOnlyList<PjskUnityRuntimeNativeMesh> ExportAccessoryPart(
        IImported imported,
        PjskSpringBoneRuntimeUnitySetup runtimeUnitySetup,
        string? attachNodeName,
        List<string> warnings
    )
    {
        var transformPaths = runtimeUnitySetup.PrefabGraphs
            .SelectMany(graph => graph.Transforms)
            .Select(transform => transform.TransformPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var attachPath = ResolveAccessoryAttachPath(transformPaths, attachNodeName);
        if (attachPath is null)
        {
            warnings.Add($"Accessory meshes skipped: attach node '{attachNodeName ?? "<none>"}' was not found in runtime prefab transforms.");
            return Array.Empty<PjskUnityRuntimeNativeMesh>();
        }

        var morphMap = BuildMorphMap(imported.MorphList);
        var result = new List<PjskUnityRuntimeNativeMesh>();
        foreach (var mesh in imported.MeshList
            .Where(mesh => !string.IsNullOrWhiteSpace(mesh.Path))
            .OrderBy(mesh => mesh.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveSkinBinding(mesh, transformPaths, transformPaths, out var skinBinding, out var skinFailure))
            {
                warnings.Add($"Accessory mesh '{mesh.Path}' exported without skin binding: {skinFailure}");
                skinBinding = EmptySkinBinding;
            }

            var renderer = new SpringPrefabRenderer(
                PathId: -100000 - result.Count,
                TypeName: "ImportedAccessoryMesh",
                GameObjectPathId: null,
                TransformPathId: null,
                Name: Path.GetFileName(mesh.Path),
                TransformPath: attachPath,
                PoseRoot: FirstPathSegment(attachPath),
                ActiveSelf: true,
                ActiveInHierarchy: true,
                Enabled: true,
                MeshPathId: null,
                MeshName: Path.GetFileName(mesh.Path),
                SkinnedMeshBones: Array.Empty<long>(),
                RootBonePathId: null,
                MaterialFileIds: Array.Empty<long>(),
                MaterialPathIds: Array.Empty<long>()
            );

            result.Add(BuildNativeMesh(
                "Accessory",
                mesh,
                renderer,
                attachPath,
                rootBonePath: null,
                skinBinding,
                ResolveMorphTargets(mesh.Path, morphMap)
            ));
        }

        if (result.Count == 0)
        {
            warnings.Add("Accessory imported model had no mesh paths; native accessory meshes were not exported.");
        }

        return result;
    }

    private static IReadOnlyList<PjskUnityRuntimeNativeMesh> ExportPart(
        string partKind,
        IImported imported,
        SpringPrefabGraph graph,
        IReadOnlyCollection<string> activeRoots,
        List<string> warnings
    )
    {
        var transformPathByPathId = graph.Transforms
            .Where(transform => !string.IsNullOrWhiteSpace(transform.TransformPath))
            .ToDictionary(transform => transform.PathId, transform => transform.TransformPath!, EqualityComparer<long>.Default);
        var transformPaths = transformPathByPathId.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var activeRenderers = graph.Renderers
            .Where(renderer => renderer.Enabled && IsActiveRenderer(renderer, activeRoots))
            .ToList();
        var morphMap = BuildMorphMap(imported.MorphList);
        var result = new List<PjskUnityRuntimeNativeMesh>();

        var meshLookup = BuildImportedMeshLookupMap(imported.MeshList
            .Where(mesh => !string.IsNullOrWhiteSpace(mesh.Path)));

        foreach (var renderer in activeRenderers)
        {
            if (string.IsNullOrWhiteSpace(renderer.TransformPath))
            {
                warnings.Add($"{partKind} renderer {renderer.PathId} skipped: renderer has no transform path.");
                continue;
            }

            if (!TryResolveImportedMesh(renderer, meshLookup, transformPaths, transformPathByPathId, out var mesh, out var failure))
            {
                warnings.Add($"{partKind} renderer '{renderer.TransformPath}' skipped: {failure}");
                continue;
            }

            var rendererBonePaths = new List<string>();
            var missingBone = false;
            foreach (var pathId in renderer.SkinnedMeshBones)
            {
                if (!transformPathByPathId.TryGetValue(pathId, out var bonePath))
                {
                    missingBone = true;
                    break;
                }
                rendererBonePaths.Add(bonePath);
            }

            if (missingBone)
            {
                warnings.Add($"{partKind} mesh '{mesh.Path}' skipped: renderer {renderer.PathId} has unresolved skinned bone PathIDs.");
                continue;
            }

            if (!TryResolveSkinBinding(mesh, rendererBonePaths, transformPaths, out var skinBinding, out var skinFailure))
            {
                warnings.Add($"{partKind} mesh '{mesh.Path}' skipped: {skinFailure}");
                continue;
            }

            var rootBonePath = renderer.RootBonePathId is long rootBonePathId &&
                transformPathByPathId.TryGetValue(rootBonePathId, out var resolvedRootBonePath)
                ? resolvedRootBonePath
                : null;

            result.Add(BuildNativeMesh(
                partKind,
                mesh,
                renderer,
                renderer.TransformPath,
                rootBonePath,
                skinBinding,
                ResolveMorphTargets(mesh.Path, morphMap)
            ));
        }

        return result;
    }

    private static bool IsActiveRenderer(
        SpringPrefabRenderer renderer,
        IReadOnlyCollection<string> activeRoots
    )
    {
        var poseRoot = renderer.PoseRoot ?? FirstPathSegment(renderer.TransformPath);
        return !string.IsNullOrWhiteSpace(poseRoot) &&
            activeRoots.Contains(poseRoot, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveAccessoryAttachPath(
        IReadOnlyList<string> transformPaths,
        string? attachNodeName
    )
    {
        if (!string.IsNullOrWhiteSpace(attachNodeName) &&
            TryResolveTransformPathByNameOrPath(transformPaths, attachNodeName!, out var explicitPath))
        {
            return explicitPath;
        }

        foreach (var fallback in new[]
        {
            "Head",
            "Neck",
            "face",
        })
        {
            if (TryResolveTransformPathByNameOrPath(transformPaths, fallback, out var path))
            {
                return path;
            }
        }

        return transformPaths.FirstOrDefault();
    }

    private static bool TryResolveTransformPathByNameOrPath(
        IReadOnlyList<string> transformPaths,
        string nameOrPath,
        out string path
    )
    {
        var direct = transformPaths.FirstOrDefault(candidate =>
            string.Equals(candidate, nameOrPath, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            path = direct;
            return true;
        }

        var suffix = "/" + nameOrPath;
        var bySuffix = transformPaths.FirstOrDefault(candidate =>
            candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (bySuffix is not null)
        {
            path = bySuffix;
            return true;
        }

        var byLeaf = transformPaths.FirstOrDefault(candidate =>
            string.Equals(LastPathSegment(candidate), nameOrPath, StringComparison.OrdinalIgnoreCase));
        if (byLeaf is not null)
        {
            path = byLeaf;
            return true;
        }

        path = string.Empty;
        return false;
    }

    private static bool TryResolveImportedMesh(
        SpringPrefabRenderer renderer,
        IReadOnlyDictionary<string, IReadOnlyList<ImportedMesh>> meshLookup,
        IReadOnlyList<string> transformPaths,
        IReadOnlyDictionary<long, string> transformPathByPathId,
        out ImportedMesh mesh,
        out string failure
    )
    {
        var candidates = BuildPrefabMeshLookupKeys(renderer)
            .SelectMany(key => meshLookup.TryGetValue(key, out var meshes)
                ? meshes
                : Array.Empty<ImportedMesh>())
            .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(candidate => new
            {
                Mesh = candidate,
                Score = ScoreImportedMesh(renderer, candidate),
            })
            .ToList();

        var compatible = candidates
            .Where(candidate => IsSkinCompatible(renderer, candidate.Mesh, transformPaths, transformPathByPathId))
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Mesh.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (compatible.Count > 0)
        {
            mesh = compatible[0].Mesh;
            failure = string.Empty;
            return true;
        }

        mesh = null!;
        if (candidates.Count == 0)
        {
            failure = "no imported mesh matched prefab renderer path/name.";
            return false;
        }

        var rendererBonePaths = renderer.SkinnedMeshBones
            .Select(pathId => transformPathByPathId.TryGetValue(pathId, out var path) ? path : null)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateSummary = candidates
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Mesh.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate =>
            {
                var importedBoneCount = candidate.Mesh.BoneList?.Count ?? 0;
                var resolvedBoneCount = ResolveImportedBonePathsByIndex(candidate.Mesh, transformPaths, rendererBonePaths).Count;
                var usedBoneCount = CollectUsedBoneIndices(candidate.Mesh).Count;
                return $"{candidate.Mesh.Path}:imported={importedBoneCount},used={usedBoneCount},resolved={resolvedBoneCount}";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8);
        failure = $"imported mesh candidates matched by path/name but their skin bones did not resolve to the renderer prefab bones; renderer bones={renderer.SkinnedMeshBones.Count}; candidates={string.Join(", ", candidateSummary)}.";
        return false;
    }

    private static int ScoreImportedMesh(SpringPrefabRenderer renderer, ImportedMesh mesh)
    {
        if (string.Equals(renderer.TransformPath, mesh.Path, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var rendererWithoutRoot = DropFirstPathSegment(renderer.TransformPath);
        var meshWithoutRoot = DropFirstPathSegment(mesh.Path);
        if (!string.IsNullOrWhiteSpace(rendererWithoutRoot) &&
            string.Equals(rendererWithoutRoot, mesh.Path, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(meshWithoutRoot) &&
            string.Equals(renderer.TransformPath, meshWithoutRoot, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(rendererWithoutRoot) &&
            !string.IsNullOrWhiteSpace(meshWithoutRoot) &&
            string.Equals(rendererWithoutRoot, meshWithoutRoot, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        var meshLeaf = Path.GetFileName(mesh.Path);
        if (!string.IsNullOrWhiteSpace(renderer.MeshName) &&
            string.Equals(renderer.MeshName, meshLeaf, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        var rendererLeaf = LastPathSegment(renderer.TransformPath);
        if (!string.IsNullOrWhiteSpace(rendererLeaf) &&
            string.Equals(rendererLeaf, meshLeaf, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return 1000;
    }

    private sealed record NativeSkinBinding(
        IReadOnlyList<string> BonePaths,
        IReadOnlyDictionary<int, int> BoneIndexRemap,
        IReadOnlyList<float> BoneInverseBindMatrices
    );

    private static readonly NativeSkinBinding EmptySkinBinding = new(
        Array.Empty<string>(),
        new Dictionary<int, int>(),
        Array.Empty<float>()
    );

    private static bool IsSkinCompatible(
        SpringPrefabRenderer renderer,
        ImportedMesh mesh,
        IReadOnlyList<string> transformPaths,
        IReadOnlyDictionary<long, string> transformPathByPathId
    )
    {
        var importedBoneCount = mesh.BoneList?.Count ?? 0;
        if (importedBoneCount == 0)
        {
            return renderer.SkinnedMeshBones.Count == 0;
        }

        if (MaxSkinBoneIndex(mesh) >= importedBoneCount)
        {
            return false;
        }

        var rendererBonePaths = renderer.SkinnedMeshBones
            .Select(pathId => transformPathByPathId.TryGetValue(pathId, out var path) ? path : null)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (rendererBonePaths.Count != renderer.SkinnedMeshBones.Count)
        {
            return false;
        }

        return TryResolveSkinBinding(
            mesh,
            rendererBonePaths.ToList(),
            transformPaths,
            out _,
            out _
        );
    }

    private static bool TryResolveSkinBinding(
        ImportedMesh mesh,
        IReadOnlyList<string> rendererBonePaths,
        IReadOnlyList<string> transformPaths,
        out NativeSkinBinding binding,
        out string failure
    )
    {
        var importedBoneCount = mesh.BoneList?.Count ?? 0;
        if (importedBoneCount == 0)
        {
            var rigidBonePaths = rendererBonePaths.Count == 0
                ? Array.Empty<string>()
                : rendererBonePaths;
            binding = new NativeSkinBinding(
                rigidBonePaths,
                new Dictionary<int, int>(),
                Array.Empty<float>()
            );
            failure = string.Empty;
            return true;
        }

        var maxSkinBoneIndex = MaxSkinBoneIndex(mesh);
        if (maxSkinBoneIndex >= importedBoneCount)
        {
            binding = EmptySkinBinding;
            failure = $"vertex skin index {maxSkinBoneIndex} exceeds imported skin bone count {importedBoneCount}.";
            return false;
        }

        var rendererBonePathSet = rendererBonePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolvedBonePathsByImportedIndex = ResolveImportedBonePathsByIndex(mesh, transformPaths, rendererBonePathSet);
        var usedBoneIndices = CollectUsedBoneIndices(mesh);
        var unresolvedUsedBoneIndices = usedBoneIndices
            .Where(index => !resolvedBonePathsByImportedIndex.TryGetValue(index, out var path) ||
                string.IsNullOrWhiteSpace(path) ||
                !rendererBonePathSet.Contains(path))
            .Take(8)
            .ToList();
        if (unresolvedUsedBoneIndices.Count > 0)
        {
            var missing = unresolvedUsedBoneIndices
                .Select(index => $"{index}:{mesh.BoneList![index].Path}");
            binding = EmptySkinBinding;
            failure = $"used imported skin bones did not resolve to renderer m_Bones; imported={importedBoneCount}, used={usedBoneIndices.Count}, resolved={resolvedBonePathsByImportedIndex.Count}, sampleMissing=[{string.Join(", ", missing)}].";
            return false;
        }

        var orderedUsedBoneIndices = usedBoneIndices
            .OrderBy(index => index)
            .ToList();
        if (orderedUsedBoneIndices.Count == 0)
        {
            orderedUsedBoneIndices.Add(0);
        }

        var remap = new Dictionary<int, int>();
        var bonePaths = new List<string>(orderedUsedBoneIndices.Count);
        var inverseBindMatrices = new List<float>(orderedUsedBoneIndices.Count * 16);
        for (var newIndex = 0; newIndex < orderedUsedBoneIndices.Count; newIndex += 1)
        {
            var oldIndex = orderedUsedBoneIndices[newIndex];
            remap[oldIndex] = newIndex;
            bonePaths.Add(resolvedBonePathsByImportedIndex[oldIndex]);
            AddMatrix(inverseBindMatrices, mesh.BoneList![oldIndex].Matrix);
        }

        binding = new NativeSkinBinding(bonePaths, remap, inverseBindMatrices);
        failure = string.Empty;
        return true;
    }

    private static IReadOnlyDictionary<int, string> ResolveImportedBonePathsByIndex(
        ImportedMesh mesh,
        IReadOnlyList<string> transformPaths,
        IReadOnlySet<string> preferredPaths
    )
    {
        if (mesh.BoneList is not { Count: > 0 })
        {
            return new Dictionary<int, string>();
        }

        var result = new Dictionary<int, string>();
        for (var index = 0; index < mesh.BoneList.Count; index += 1)
        {
            var bone = mesh.BoneList[index];
            var resolvedPath = ResolveTransformPath(bone.Path, transformPaths, preferredPaths);
            if (resolvedPath is null)
            {
                continue;
            }
            result[index] = resolvedPath;
        }

        return result;
    }

    private static string? ResolveTransformPath(
        string? path,
        IReadOnlyList<string> transformPaths,
        IReadOnlySet<string> preferredPaths
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var directPreferred = preferredPaths.FirstOrDefault(candidate =>
            string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        if (directPreferred is not null)
        {
            return directPreferred;
        }

        var direct = transformPaths.FirstOrDefault(candidate =>
            string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return direct;
        }

        var withoutRoot = DropFirstPathSegment(path);
        if (!string.IsNullOrWhiteSpace(withoutRoot))
        {
            var preferredByRelativePath = preferredPaths.FirstOrDefault(candidate =>
                string.Equals(DropFirstPathSegment(candidate), withoutRoot, StringComparison.OrdinalIgnoreCase));
            if (preferredByRelativePath is not null)
            {
                return preferredByRelativePath;
            }

            var byRelativePath = transformPaths.FirstOrDefault(candidate =>
                string.Equals(DropFirstPathSegment(candidate), withoutRoot, StringComparison.OrdinalIgnoreCase));
            if (byRelativePath is not null)
            {
                return byRelativePath;
            }
        }

        var preferredBySuffix = preferredPaths.FirstOrDefault(candidate =>
            candidate.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase));
        if (preferredBySuffix is not null)
        {
            return preferredBySuffix;
        }

        return transformPaths.FirstOrDefault(candidate =>
            candidate.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase));
    }

    private static int MaxSkinBoneIndex(ImportedMesh mesh)
    {
        var max = -1;
        foreach (var index in CollectUsedBoneIndices(mesh))
        {
            max = Math.Max(max, index);
        }

        return max;
    }

    private static IReadOnlySet<int> CollectUsedBoneIndices(ImportedMesh mesh)
    {
        var result = new HashSet<int>();
        foreach (var vertex in mesh.VertexList)
        {
            if (vertex.BoneIndices is null || vertex.Weights is null)
            {
                result.Add(0);
                continue;
            }

            for (var index = 0; index < Math.Min(vertex.BoneIndices.Length, vertex.Weights.Length); index += 1)
            {
                if (vertex.Weights[index] <= 0)
                {
                    continue;
                }
                result.Add(vertex.BoneIndices[index]);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ImportedMesh>> BuildImportedMeshLookupMap(
        IEnumerable<ImportedMesh> meshes
    )
    {
        var result = new Dictionary<string, List<ImportedMesh>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mesh in meshes)
        {
            foreach (var key in new[]
            {
                mesh.Path,
                DropFirstPathSegment(mesh.Path),
                LastPathSegment(mesh.Path),
                Path.GetFileName(mesh.Path),
            })
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!result.TryGetValue(key, out var bucket))
                {
                    bucket = new List<ImportedMesh>();
                    result[key] = bucket;
                }
                bucket.Add(mesh);
            }
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ImportedMesh>)pair.Value,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static IEnumerable<string> BuildPrefabMeshLookupKeys(SpringPrefabRenderer renderer)
    {
        foreach (var key in new[]
        {
            renderer.TransformPath,
            DropFirstPathSegment(renderer.TransformPath),
            LastPathSegment(renderer.TransformPath),
            renderer.MeshName,
            renderer.Name,
        })
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                yield return key;
            }
        }
    }

    private static PjskUnityRuntimeNativeMesh BuildNativeMesh(
        string partKind,
        ImportedMesh mesh,
        SpringPrefabRenderer renderer,
        string rendererTransformPath,
        string? rootBonePath,
        NativeSkinBinding skinBinding,
        IReadOnlyList<ImportedMorph> morphs
    )
    {
        var positions = new List<float>(mesh.VertexList.Count * 3);
        var normals = new List<float>(mesh.VertexList.Count * 3);
        var tangents = new List<float>(mesh.VertexList.Count * 4);
        var uv0 = new List<float>(mesh.VertexList.Count * 2);
        var uv1 = new List<float>(mesh.VertexList.Count * 2);
        var uv2 = new List<float>(mesh.VertexList.Count * 2);
        var colors = new List<float>(mesh.VertexList.Count * 4);
        var skinIndices = new List<ushort>(mesh.VertexList.Count * 4);
        var skinWeights = new List<float>(mesh.VertexList.Count * 4);
        var hasUv1 = mesh.VertexList.Count > 0 && mesh.VertexList.All(vertex => HasUv(vertex, 1));
        var hasUv2 = mesh.VertexList.Count > 0 && mesh.VertexList.All(vertex => HasUv(vertex, 2));

        foreach (var vertex in mesh.VertexList)
        {
            AddVector3(positions, vertex.Vertex);
            AddVector3(normals, mesh.hasNormal ? vertex.Normal : new Vector3(0, 1, 0));
            if (mesh.hasTangent)
            {
                AddTangent(tangents, vertex.Tangent);
            }
            AddUv(uv0, vertex, 0);
            if (hasUv1)
            {
                AddUv(uv1, vertex, 1);
            }
            if (hasUv2)
            {
                AddUv(uv2, vertex, 2);
            }
            if (mesh.hasColor)
            {
                colors.Add(vertex.Color.R);
                colors.Add(vertex.Color.G);
                colors.Add(vertex.Color.B);
                colors.Add(vertex.Color.A);
            }
            else
            {
                colors.Add(1);
                colors.Add(1);
                colors.Add(1);
                colors.Add(1);
            }
            AddSkin(vertex, skinBinding.BoneIndexRemap, skinIndices, skinWeights);
        }

        var indexCursor = 0;
        var submeshes = new List<PjskUnityRuntimeNativeSubmesh>();
        foreach (var submesh in mesh.SubmeshList.Select((value, slotIndex) => new { value, slotIndex }))
        {
            var indices = new List<int>(submesh.value.FaceList.Count * 3);
            foreach (var face in submesh.value.FaceList)
            {
                indices.Add(submesh.value.BaseVertex + face.VertexIndices[0]);
                indices.Add(submesh.value.BaseVertex + face.VertexIndices[1]);
                indices.Add(submesh.value.BaseVertex + face.VertexIndices[2]);
            }
            var materialPathId = submesh.slotIndex < renderer.MaterialPathIds.Count
                ? renderer.MaterialPathIds[submesh.slotIndex]
                : throw new InvalidOperationException(
                    $"Renderer {renderer.PathId} submesh {submesh.slotIndex} has no material path id."
                );
            var materialFileId = submesh.slotIndex < renderer.MaterialFileIds.Count
                ? renderer.MaterialFileIds[submesh.slotIndex]
                : throw new InvalidOperationException(
                    $"Renderer {renderer.PathId} submesh {submesh.slotIndex} has no material file id."
                );
            var materialIdentity = RuntimeMaterialIdentityResolver.Resolve(
                partKind,
                submesh.slotIndex,
                materialFileId,
                materialPathId,
                submesh.value.Material
            );
            submeshes.Add(new PjskUnityRuntimeNativeSubmesh(
                SlotIndex: submesh.slotIndex,
                MaterialKey: materialIdentity.MaterialKey,
                MaterialFileId: materialIdentity.MaterialFileId,
                MaterialPathId: materialIdentity.MaterialPathId,
                MaterialName: submesh.value.Material,
                Start: indexCursor,
                Count: indices.Count,
                Indices: indices
            ));
            indexCursor += indices.Count;
        }

        return new PjskUnityRuntimeNativeMesh(
            PartKind: partKind,
            MeshPath: mesh.Path,
            MeshName: Path.GetFileName(mesh.Path),
            RendererPathId: renderer.PathId,
            RendererTransformPath: rendererTransformPath,
            RootBonePath: rootBonePath,
            BonePaths: skinBinding.BonePaths,
            BoneInverseBindMatrices: skinBinding.BoneInverseBindMatrices,
            Submeshes: submeshes,
            Positions: positions,
            Normals: normals,
            Tangents: tangents,
            Uv0: uv0,
            Uv1: uv1,
            Uv2: uv2,
            Colors: colors,
            SkinIndices: skinIndices,
            SkinWeights: skinWeights,
            MorphTargets: BuildMorphTargets(mesh, morphs)
        );
    }

    private static IReadOnlyList<float> BuildBoneInverseBindMatrices(ImportedMesh mesh)
    {
        if (mesh.BoneList is not { Count: > 0 })
        {
            return Array.Empty<float>();
        }

        var result = new List<float>(mesh.BoneList.Count * 16);
        foreach (var bone in mesh.BoneList)
        {
            AddMatrix(result, bone.Matrix);
        }

        return result;
    }

    private static void AddMatrix(List<float> values, Matrix4x4 matrix)
    {
        values.Add(matrix.M00);
        values.Add(matrix.M01);
        values.Add(matrix.M02);
        values.Add(matrix.M03);
        values.Add(matrix.M10);
        values.Add(matrix.M11);
        values.Add(matrix.M12);
        values.Add(matrix.M13);
        values.Add(matrix.M20);
        values.Add(matrix.M21);
        values.Add(matrix.M22);
        values.Add(matrix.M23);
        values.Add(matrix.M30);
        values.Add(matrix.M31);
        values.Add(matrix.M32);
        values.Add(matrix.M33);
    }

    private static IReadOnlyList<PjskUnityRuntimeNativeMorphTarget> BuildMorphTargets(
        ImportedMesh mesh,
        IReadOnlyList<ImportedMorph> morphs
    )
    {
        if (morphs.Count == 0)
        {
            return Array.Empty<PjskUnityRuntimeNativeMorphTarget>();
        }

        var result = new List<PjskUnityRuntimeNativeMorphTarget>();
        foreach (var morph in morphs)
        {
            foreach (var channel in morph.Channels)
            {
                var positionDeltaByIndex = new Dictionary<int, Vector3>();
                var normalDeltaByIndex = new Dictionary<int, Vector3>();
                var hasNormals = false;
                foreach (var keyframe in channel.KeyframeList)
                {
                    foreach (var morphVertex in keyframe.VertexList)
                    {
                        var index = (int)morphVertex.Index;
                        if (index < 0 || index >= mesh.VertexList.Count)
                        {
                            continue;
                        }

                        var baseVertex = mesh.VertexList[index];
                        var positionDelta = morphVertex.Vertex.Vertex - baseVertex.Vertex;
                        positionDeltaByIndex[index] = positionDeltaByIndex.TryGetValue(index, out var existingPosition)
                            ? existingPosition + positionDelta
                            : positionDelta;

                        if (keyframe.hasNormals)
                        {
                            hasNormals = true;
                            var normalDelta = morphVertex.Vertex.Normal - baseVertex.Normal;
                            normalDeltaByIndex[index] = normalDeltaByIndex.TryGetValue(index, out var existingNormal)
                                ? existingNormal + normalDelta
                                : normalDelta;
                        }
                    }
                }

                var indices = positionDeltaByIndex.Keys.OrderBy(index => index).ToList();
                var positionDeltas = new List<float>(indices.Count * 3);
                var normalDeltas = hasNormals ? new List<float>(indices.Count * 3) : null;
                foreach (var index in indices)
                {
                    AddVector3(positionDeltas, positionDeltaByIndex[index]);
                    if (normalDeltas is not null)
                    {
                        AddVector3(normalDeltas, normalDeltaByIndex.TryGetValue(index, out var normalDelta)
                            ? normalDelta
                            : new Vector3());
                    }
                }

                result.Add(new PjskUnityRuntimeNativeMorphTarget(
                    Name: channel.Name,
                    Indices: indices,
                    PositionDeltas: positionDeltas,
                    NormalDeltas: normalDeltas
                ));
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, ImportedMorph> BuildMorphMap(
        IReadOnlyList<ImportedMorph> morphList
    )
    {
        return morphList
            .GroupBy(morph => morph.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ImportedMorph> ResolveMorphTargets(
        string meshPath,
        IReadOnlyDictionary<string, ImportedMorph> morphMap
    )
    {
        if (morphMap.TryGetValue(meshPath, out var morph))
        {
            return new[] { morph };
        }

        return morphMap
            .Where(pair => pair.Key.EndsWith(meshPath, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .ToList();
    }

    private static void AddVector3(List<float> values, Vector3 vector)
    {
        values.Add(vector.X);
        values.Add(vector.Y);
        values.Add(vector.Z);
    }

    private static void AddTangent(List<float> values, Vector4 tangent)
    {
        values.Add(tangent.X);
        values.Add(tangent.Y);
        values.Add(tangent.Z);
        values.Add(-tangent.W);
    }

    private static void AddUv(List<float> values, ImportedVertex vertex, int channel)
    {
        if (vertex.UV is { Length: > 0 } &&
            channel < vertex.UV.Length &&
            vertex.UV[channel] is { Length: >= 2 })
        {
            values.Add(vertex.UV[channel][0]);
            values.Add(vertex.UV[channel][1]);
            return;
        }

        values.Add(0);
        values.Add(0);
    }

    private static bool HasUv(ImportedVertex vertex, int channel)
    {
        return vertex.UV is { Length: > 0 } &&
            channel < vertex.UV.Length &&
            vertex.UV[channel] is { Length: >= 2 };
    }

    private static void AddSkin(
        ImportedVertex vertex,
        IReadOnlyDictionary<int, int> boneIndexRemap,
        List<ushort> skinIndices,
        List<float> skinWeights
    )
    {
        var weights = new List<(int Index, float Weight)>();
        if (vertex.BoneIndices is not null && vertex.Weights is not null)
        {
            for (var index = 0; index < Math.Min(vertex.BoneIndices.Length, vertex.Weights.Length); index += 1)
            {
                if (vertex.Weights[index] <= 0)
                {
                    continue;
                }
                var sourceIndex = vertex.BoneIndices[index];
                var targetIndex = boneIndexRemap.TryGetValue(sourceIndex, out var remappedIndex)
                    ? remappedIndex
                    : sourceIndex;
                weights.Add((targetIndex, vertex.Weights[index]));
            }
        }

        if (weights.Count == 0)
        {
            weights.Add((0, 1));
        }

        for (var index = 0; index < 4; index += 1)
        {
            if (index < weights.Count)
            {
                skinIndices.Add((ushort)Math.Clamp(weights[index].Index, 0, ushort.MaxValue));
                skinWeights.Add(weights[index].Weight);
            }
            else
            {
                skinIndices.Add(0);
                skinWeights.Add(0);
            }
        }
    }

    private static string? FirstPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        var slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }

    private static string? DropFirstPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var slash = path.IndexOf('/');
        return slash < 0 || slash + 1 >= path.Length
            ? path
            : path[(slash + 1)..];
    }

    private static string? LastPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var slash = path.LastIndexOf('/');
        return slash < 0 || slash + 1 >= path.Length
            ? path
            : path[(slash + 1)..];
    }
}
