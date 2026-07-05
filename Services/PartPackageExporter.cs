using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AssetStudio;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class PartPackageExporter
{
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly BundleInputResolver resolver = new();
    private readonly AssetStudioBundleParser parser = new();
    private readonly AssetStudioImportedModelFactory modelFactory = new();
    private readonly SpringBoneExporter springBoneExporter = new();
    private readonly UnityRuntimeNativeMeshExporter nativeMeshExporter = new();
    private readonly UnityRuntimeTextureExporter textureExporter = new();
    private readonly IReadOnlyDictionary<string, float> characterHeightMetersById;

    public PartPackageExporter(
        IReadOnlyDictionary<string, float>? characterHeightMetersById = null
    )
    {
        this.characterHeightMetersById = characterHeightMetersById ?? DefaultCharacterHeightMetersById;
    }

    public IReadOnlyList<PartPackageExportResult> ExportAll(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        string? manifestPath = null,
        int shardCount = 1,
        int shardIndex = 0,
        string runtimeJsonOutput = RuntimeJsonWriter.Gzip
    )
    {
        var characterHeightMetersById = CharacterHeightResolver.LoadMetersByCharacterId(masterDirectory);
        var manifest = PartPackageExportManifest.Load(manifestPath);
        var partEntries = LoadPartEntries(masterDirectory, assetRoot)
            .Where(entry => entry.BundlePath is not null && entry.Status != "missing")
            .Where(HasRequiredBundleFiles)
            .ToList();
        var results = new List<PartPackageExportResult>();
        AssetStudioLoadedBundle? cachedBundle = null;
        try
        {
            foreach (var entry in SelectRepresentativePartEntries(partEntries)
                .Where(entry => IsInShard(ShardKey(entry), shardCount, shardIndex)))
            {
                var packageDirectory = Path.Combine(outputDirectory, entry.PackagePath.Replace('/', Path.DirectorySeparatorChar));
                var runtimePath = Path.Combine(packageDirectory, "part-runtime.json");
                var runtimeOutputPath = RuntimeJsonWriter.PrimaryPath(runtimePath, runtimeJsonOutput);
                var stamp = PartPackageInputStamp.From(entry);
                if (manifest.CanSkip(entry.PackagePath, runtimeOutputPath, stamp) &&
                    RuntimePackageHasCharacterHeight(runtimePath, runtimeJsonOutput))
                {
                    DeletePartExportError(packageDirectory);
                    results.Add(new PartPackageExportResult(entry, runtimeOutputPath, Array.Empty<string>()));
                    continue;
                }

                PartPackageExportResult result;
                try
                {
                    result = ExportWithMaterialDependencyRetry(
                        entry,
                        assetRoot,
                        outputDirectory,
                        characterHeightMetersById,
                        ref cachedBundle,
                        runtimeJsonOutput
                    );
                    manifest.Update(entry.PackagePath, stamp);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(packageDirectory);
                    var errorPath = Path.Combine(packageDirectory, "part-export-error.json");
                    File.WriteAllText(errorPath, JsonSerializer.Serialize(new
                    {
                        packagePath = entry.PackagePath,
                        sourcePackagePath = entry.SourcePackagePath,
                        costume3dId = entry.Costume3dId,
                        partType = entry.PartType,
                        unit = entry.Unit,
                        colorId = entry.ColorId,
                        bundlePath = entry.BundlePath,
                        colorVariationBundlePath = entry.ColorVariationBundlePath,
                        error = ex.Message,
                        exceptionType = ex.GetType().FullName,
                    }, WriteJsonOptions));
                    Console.Error.WriteLine($"Part package export skipped: {entry.PackagePath}: {ex.Message}");
                    result = new PartPackageExportResult(
                        entry,
                        errorPath,
                        new[] { $"export failed: {ex.Message}" },
                        Succeeded: false
                    );
                }
                results.Add(result);
            }
        }
        finally
        {
            cachedBundle?.Dispose();
        }
        manifest.Save();

        return results;
    }

    private static IReadOnlyList<PartRegistryEntry> SelectRepresentativePartEntries(IReadOnlyList<PartRegistryEntry> entries)
    {
        return entries
            .GroupBy(entry => entry.PackagePath, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(entry => entry.Costume3dId)
                .ThenBy(entry => entry.Unit ?? string.Empty, StringComparer.Ordinal)
                .First())
            .OrderBy(entry => entry.PartType, StringComparer.Ordinal)
            .ThenBy(entry => entry.BaseSourceKey ?? entry.SourceKey ?? entry.PackagePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.PackagePath, StringComparer.Ordinal)
            .ToList();
    }

    private static string ShardKey(PartRegistryEntry entry)
    {
        return entry.BaseSourceKey ?? entry.SourceKey ?? entry.PackagePath;
    }

    private static bool IsInShard(string shardKey, int shardCount, int shardIndex)
    {
        if (shardCount <= 1)
        {
            return true;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(shardKey));
        var value = BitConverter.ToUInt64(hash, 0);
        return (int)(value % (ulong)shardCount) == shardIndex;
    }

    public PartPackageExportResult ExportOne(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        int costume3dId,
        string partType,
        string? unit,
        string runtimeJsonOutput = RuntimeJsonWriter.Gzip
    )
    {
        var characterHeightMetersById = CharacterHeightResolver.LoadMetersByCharacterId(masterDirectory);
        var normalizedPartType = NormalizePartType(partType);
        var entry = LoadPartEntries(masterDirectory, assetRoot)
            .Where(entry => entry.Costume3dId == costume3dId)
            .Where(entry => string.Equals(ResolveRuntimePartType(entry), normalizedPartType, StringComparison.OrdinalIgnoreCase))
            .Where(entry => unit is null || string.Equals(entry.Unit, unit, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Unit ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No part registry entry matched costume3dId={costume3dId}, partType={partType}, unit={unit ?? "<any>"}.");

        if (entry.BundlePath is null)
        {
            throw new InvalidOperationException($"Matched part has no bundle path: costume3dId={costume3dId}, partType={partType}.");
        }

        return Export(entry, assetRoot, outputDirectory, characterHeightMetersById, runtimeJsonOutput);
    }

    public PartPackageExportResult Export(
        PartRegistryEntry entry,
        string assetRoot,
        string outputDirectory,
        IReadOnlyDictionary<string, float>? characterHeightMetersById = null,
        string runtimeJsonOutput = RuntimeJsonWriter.Gzip
    )
    {
        var input = ResolveInput(entry);
        using var loadedBundle = AssetStudioLoadedBundle.Load(input);
        try
        {
            return Export(entry, assetRoot, outputDirectory, characterHeightMetersById, loadedBundle, runtimeJsonOutput);
        }
        catch (MissingMaterialReferenceException ex)
        {
            using var fallbackBundle = AssetStudioLoadedBundle.Load(input, BundleLoadDependencyMode.FullDirectory);
            var result = Export(entry, assetRoot, outputDirectory, characterHeightMetersById, fallbackBundle, runtimeJsonOutput);
            return AddWarning(result, BuildMaterialDependencyFallbackWarning(ex));
        }
    }

    private PartPackageExportResult ExportWithMaterialDependencyRetry(
        PartRegistryEntry entry,
        string assetRoot,
        string outputDirectory,
        IReadOnlyDictionary<string, float> characterHeightMetersById,
        ref AssetStudioLoadedBundle? cachedBundle,
        string runtimeJsonOutput
    )
    {
        var input = ResolveInput(entry);
        if (cachedBundle is null || !IsSameLoadedInput(cachedBundle.Input, input))
        {
            cachedBundle?.Dispose();
            cachedBundle = AssetStudioLoadedBundle.Load(input);
        }

        try
        {
            return Export(entry, assetRoot, outputDirectory, characterHeightMetersById, cachedBundle, runtimeJsonOutput);
        }
        catch (MissingMaterialReferenceException ex) when (cachedBundle.DependencyMode != BundleLoadDependencyMode.FullDirectory)
        {
            cachedBundle.Dispose();
            cachedBundle = AssetStudioLoadedBundle.Load(input, BundleLoadDependencyMode.FullDirectory);
            PartPackageExportResult result;
            try
            {
                result = Export(entry, assetRoot, outputDirectory, characterHeightMetersById, cachedBundle, runtimeJsonOutput);
            }
            catch (MissingMaterialReferenceException fallbackEx)
            {
                throw new InvalidOperationException(
                    $"missing_after_fallback: {fallbackEx.Message}",
                    fallbackEx
                );
            }
            return AddWarning(result, BuildMaterialDependencyFallbackWarning(ex));
        }
    }

    private PartPackageExportResult Export(
        PartRegistryEntry entry,
        string assetRoot,
        string outputDirectory,
        IReadOnlyDictionary<string, float>? characterHeightMetersById,
        AssetStudioLoadedBundle loadedBundle,
        string runtimeJsonOutput
    )
    {
        var packageDirectory = Path.Combine(outputDirectory, entry.PackagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(packageDirectory);
        var normalizedType = ResolveRuntimePartType(entry);
        var input = loadedBundle.Input;

        var inventory = parser.Parse(input, loadedBundle.PrimaryObjects, loadedBundle.Objects, loadedBundle.AssetsFileCount);
        WriteBundleOpenSummary(packageDirectory, entry, input, loadedBundle.DependencyMode, inventory);
        var imported = modelFactory.CreateImportedModel(
            input,
            loadedBundle.PrimaryObjects,
            normalizedType is "head" or "hair"
                ? SelectHeadRootName(inventory)
                : normalizedType == "head_optional" ? SelectAccessoryRootName(inventory) : null
        );
        var overrideTextures = entry.ColorVariationBundlePath is not null
            ? modelFactory.CreateImportedTextures(entry.ColorVariationBundlePath)
            : Array.Empty<ImportedTexture>();
        var textures = textureExporter.ExportPartTextures(
            packageDirectory,
            normalizedType,
            inventory,
            overrideTextures
        );
        var springBone = springBoneExporter.Export(input, loadedBundle.PrimaryObjects);
        var runtimeSpringBone = BuildPartSpringBone(normalizedType, springBone);
        var nativeMeshes = ExportNativeMeshes(normalizedType, imported, runtimeSpringBone);
        var builtMaterialSlots = BuildMaterialSlots(normalizedType, inventory, textures, imported, nativeMeshes);
        var nativeDeduplication = DeduplicateNativeMeshes(nativeMeshes, builtMaterialSlots.Slots);
        nativeMeshes = nativeDeduplication.NativeMeshes;
        var materialSlots = FilterMaterialSlotsForNativeMeshes(
            normalizedType,
            builtMaterialSlots.Slots,
            nativeMeshes
        );
        var textureRoles = BuildTextureRoles(materialSlots);
        var package = new PartRuntimePackage(
            Version: "0414-part-2",
            Part: new PartRuntimeIdentity(
                Costume3dId: entry.Costume3dId,
                PartType: normalizedType,
                CharacterId: entry.CharacterId,
                Unit: entry.Unit,
                Name: entry.Name,
                ColorId: entry.ColorId,
                ColorName: entry.ColorName,
                Costume3dGroupId: entry.Costume3dGroupId,
                ModelAssetbundleName: entry.ModelAssetbundleName,
                HeadCostume3dAssetbundleType: entry.HeadCostume3dAssetbundleType
            ),
            Source: new PartRuntimeSource(
                BundlePath: input.ResolvedBundlePath,
                ColorVariationBundlePath: entry.ColorVariationBundlePath,
                AssetRootRelativeBundlePath: TryRelativePath(assetRoot, input.ResolvedBundlePath)
            ),
            Mount: BuildMount(entry, inventory, normalizedType),
            Manifest: BuildManifest(entry, input, inventory, normalizedType, characterHeightMetersById),
            NativeMeshes: nativeMeshes,
            MaterialSlots: materialSlots,
            TextureRoles: textureRoles,
            CharacterTextures: textures,
            SpringBone: runtimeSpringBone,
            MorphChannelBindings: normalizedType is "head" or "hair"
                ? ReadHeadMorphBindings(imported)
                : Array.Empty<HeadMorphChannel>(),
            Warnings: entry.Warnings
                .Concat(nativeMeshes.Warnings)
                .Concat(builtMaterialSlots.Warnings)
                .Concat(nativeDeduplication.Warnings)
                .Concat(runtimeSpringBone.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        );

        var runtimePath = Path.Combine(packageDirectory, "part-runtime.json");
        WriteJson(runtimePath, package, runtimeJsonOutput);
        DeletePartExportError(packageDirectory);
        return new PartPackageExportResult(entry, RuntimeJsonWriter.PrimaryPath(runtimePath, runtimeJsonOutput), package.Warnings);
    }

    private static void WriteBundleOpenSummary(
        string packageDirectory,
        PartRegistryEntry entry,
        ResolvedBundleInput input,
        BundleLoadDependencyMode dependencyMode,
        BundleInventory inventory
    )
    {
        var path = Path.Combine(packageDirectory, "bundle-open-summary.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            status = "opened",
            dependencyMode = dependencyMode.ToString(),
            input = new
            {
                originalPath = input.OriginalInputPath,
                resolvedBundlePath = input.ResolvedBundlePath,
                partKind = input.PartKind.ToString(),
                characterId = input.CharacterId,
                bundleStem = input.BundleStem,
            },
            part = new
            {
                costume3dId = entry.Costume3dId,
                partType = entry.PartType,
                packagePath = entry.PackagePath,
                sourcePackagePath = entry.SourcePackagePath,
            },
            inventory = new
            {
                assetsFileCount = inventory.AssetsFileCount,
                objectCount = inventory.ObjectCount,
                objectTypeCounts = inventory.ObjectTypeCounts,
                rootCount = inventory.Roots.Count,
                roots = inventory.Roots,
                skinnedMeshCount = inventory.SkinnedMeshes.Count,
                staticMeshCount = inventory.StaticMeshes.Count,
                materialCount = inventory.Materials.Count,
            },
        }, WriteJsonOptions));
    }

    private static void DeletePartExportError(string packageDirectory)
    {
        var errorPath = Path.Combine(packageDirectory, "part-export-error.json");
        if (File.Exists(errorPath))
        {
            File.Delete(errorPath);
        }
    }

    private ResolvedBundleInput ResolveInput(PartRegistryEntry entry)
    {
        if (entry.BundlePath is null)
        {
            throw new InvalidOperationException($"Part entry {entry.Costume3dId}/{entry.PartType} has no bundle path.");
        }

        var normalizedType = ResolveRuntimePartType(entry);
        var input = normalizedType == "body"
            ? resolver.ResolveBody(entry.BundlePath)
            : resolver.ResolveHead(entry.BundlePath);
        return input with { CharacterId = entry.CharacterId.ToString("00") };
    }

    private static bool IsSameLoadedInput(ResolvedBundleInput current, ResolvedBundleInput next)
    {
        return string.Equals(current.ResolvedBundlePath, next.ResolvedBundlePath, StringComparison.Ordinal) &&
            current.PartKind == next.PartKind &&
            string.Equals(current.CharacterId, next.CharacterId, StringComparison.Ordinal);
    }

    private static PartPackageExportResult AddWarning(PartPackageExportResult result, string warning)
    {
        return result with
        {
            Warnings = result.Warnings
                .Append(warning)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    private static string BuildMaterialDependencyFallbackWarning(MissingMaterialReferenceException ex)
    {
        return $"Recovered missing material reference {ex.MaterialKey} by loading the full source bundle directory.";
    }

    private static PartRuntimeSpringBone BuildPartSpringBone(string partType, SpringBoneExport springBone)
    {
        var combined = partType == "body"
            ? new CombinedSpringBoneExport(1, springBone, EmptySpringBone("Head"))
            : new CombinedSpringBoneExport(1, EmptySpringBone("Body"), springBone);
        var candidate = new VrmSpringBoneCandidateBuilder().Build(combined);
        var setup = BuildSinglePartRuntimeSetup(partType, springBone, candidate);
        return new PartRuntimeSpringBone(
            PartKind: ToRuntimePartKind(partType),
            PrefabGraph: springBone.PrefabGraph,
            Managers: setup.Managers,
            Bones: setup.Bones,
            Colliders: setup.Colliders,
            ColliderBindings: setup.ColliderBindings,
            ManagerColliderCaches: setup.ManagerColliderCaches,
            ActiveRootProfile: setup.ActiveRootProfile,
            Warnings: springBone.Warnings.Concat(candidate.Warnings).Distinct(StringComparer.Ordinal).ToList()
        );
    }

    private static PjskSpringBoneRuntimeUnitySetup BuildSinglePartRuntimeSetup(
        string partType,
        SpringBoneExport springBone,
        VrmSpringBoneCandidate candidate
    )
    {
        var partKind = ToRuntimePartKind(partType);
        var managers = springBone.Managers.Select(manager => BuildRuntimeManager(partKind, manager, springBone)).ToList();
        var bones = springBone.Bones.Select(bone => BuildRuntimeBone(partKind, bone)).ToList();
        var colliders = candidate.VrmExtensionDraft.Colliders
            .Where(collider => string.Equals(collider.PartKind, partKind, StringComparison.OrdinalIgnoreCase))
            .Select(collider => new PjskSpringBoneRuntimeCollider(
                PartKind: collider.PartKind,
                Index: collider.Index,
                PathId: collider.SourcePathId,
                ScriptName: collider.ScriptName,
                NodeName: collider.NodeName,
                NodePath: collider.NodePath,
                PoseRoot: collider.PoseRoot,
                Enabled: collider.Enabled,
                LinkedRenderer: collider.LinkedRenderer,
                LinkedRendererEnabled: collider.LinkedRendererEnabled,
                Shape: collider.Shape
            ))
            .ToList();
        var bindings = candidate.VrmExtensionDraft.ColliderGroups
            .Where(group => string.Equals(group.PartKind, partKind, StringComparison.OrdinalIgnoreCase))
            .Select(group => new PjskSpringBoneRuntimeColliderBinding(
                SourceKind: group.SourceKind ?? "direct",
                PartKind: group.PartKind,
                SourceSpringBonePathId: group.SourceSpringBonePathId,
                ColliderFlag: group.ColliderFlag,
                MatchedPrefixes: group.MatchedPrefixes,
                CollidersByRoot: group.CollidersByRoot,
                DefaultRoot: group.DefaultRoot,
                SourceColliderPathIds: group.SourceColliderPathIds,
                Colliders: group.Colliders
            ))
            .Concat(BuildDeferredColliderFlagBindings(partKind, bones))
            .ToList();
        var activeRoots = springBone.PrefabGraph.Transforms
            .Select(transform => FirstPathSegment(transform.TransformPath))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(root => root, StringComparer.Ordinal)
            .ToList();
        var defaultRoot = activeRoots.FirstOrDefault() ?? (partType == "body" ? "body" : "face");
        var bindingDecisions = PjskSekaiRuntimeExtensionBuilder.BuildBindingDecisions(bones, bindings);
        return new PjskSpringBoneRuntimeUnitySetup(
            Version: "0414-part-1",
            UnityVersion: "2022.3.21f1",
            CoordinateSpace: new PjskUnityRuntimeCoordinateSpace(
                Source: "unity-left-handed",
                Viewer: "three-js-right-handed",
                PositionConversion: "viewer_mirror_x",
                RotationConversion: "viewer_negate_quaternion_yz",
                ScaleConversion: "identity",
                Notes: new[] { "Single-part package; viewer composer must merge active parts before simulation." }
            ),
            PrefabGraphs: new[] { springBone.PrefabGraph },
            BodyHeadAssembly: new PjskUnityRuntimeBodyHeadAssembly(
                Version: "0414-part-1",
                SourceKind: "single_part",
                ParentRootPath: null,
                ParentAttachPath: null,
                ChildRootPath: null,
                ChildOriginPath: null,
                RuntimeMountPath: null,
                ParentingMode: "viewer_composer",
                CoordinateSpace: "unity-left-handed",
                Notes: new[] { "Resolved by viewer runtime composer." },
                FaceRendererName: "Face",
                CombineNodeAName: "Neck",
                CombineNodeBName: "Head",
                ChildMoveSuffix: "_target"
            ),
            RootSelectionProfile: new PjskSpringBoneRootSelectionProfile(
                Policy: "single_part_active_roots",
                DefaultBodyRoot: defaultRoot,
                RootCandidates: activeRoots.Select((root, index) => new PjskSpringBoneRootCandidate(
                    Root: root,
                    PartKind: partKind,
                    StaticActive: true,
                    DefaultPriority: index,
                    ManagerPathIds: managers.Where(manager => string.Equals(FirstPathSegment(manager.NodePath), root, StringComparison.OrdinalIgnoreCase)).Select(manager => manager.PathId).ToList(),
                    BonePathIds: bones.Where(bone => string.Equals(FirstPathSegment(bone.NodePath), root, StringComparison.OrdinalIgnoreCase)).Select(bone => bone.PathId).ToList(),
                    ColliderIndexes: colliders.Where(collider => string.Equals(FirstPathSegment(collider.NodePath), root, StringComparison.OrdinalIgnoreCase)).Select(collider => collider.Index).ToList(),
                    RendererPathIds: springBone.PrefabGraph.Renderers.Where(renderer => string.Equals(FirstPathSegment(renderer.TransformPath), root, StringComparison.OrdinalIgnoreCase)).Select(renderer => renderer.PathId).ToList(),
                    Reason: "single-part root"
                )).ToList()
            ),
            SetupPlan: new PjskSpringBoneSetupPlan(
                DiscoveryMode: "single_part_runtime_package",
                RootPolicy: "viewer_composer_merge",
                ManagerPathIds: managers.Select(manager => manager.PathId).ToList(),
                OrderedSteps: new[] { "mount part graph", "merge active part springbone", "rebind current body colliders", "reset spring runtime" },
                DirectBindingCount: bindings.Count(binding => binding.SourceKind == "direct"),
                ColliderFlagBindingCount: bindings.Count(binding => binding.SourceKind == "colliderFlag")
            ),
            BindingDecisions: bindingDecisions,
            ActiveRootProfile: new PjskSpringBoneActiveRootProfile(
                DefaultBodyRoot: defaultRoot,
                ActiveRoots: activeRoots.Count == 0 ? new[] { defaultRoot } : activeRoots,
                InactiveRoots: Array.Empty<string>()
            ),
            ManagerColliderCaches: BuildManagerColliderCaches(managers, colliders),
            Managers: managers,
            Bones: bones,
            Colliders: colliders,
            ColliderBindings: bindings,
            Warnings: candidate.Warnings
        );
    }

    private static IReadOnlyList<PjskSpringBoneRuntimeColliderBinding> BuildDeferredColliderFlagBindings(
        string partKind,
        IReadOnlyList<PjskSpringBoneRuntimeBone> bones
    )
    {
        if (!string.Equals(partKind, "Head", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<PjskSpringBoneRuntimeColliderBinding>();
        }

        return bones
            .Where(bone => bone.ColliderFlag > 0)
            .Select(bone =>
            {
                var prefixes = ResolveColliderFlagPrefixes(bone.ColliderFlag);
                return new PjskSpringBoneRuntimeColliderBinding(
                    SourceKind: "deferred_body_colliderFlag",
                    PartKind: partKind,
                    SourceSpringBonePathId: bone.PathId,
                    ColliderFlag: bone.ColliderFlag,
                    MatchedPrefixes: prefixes,
                    CollidersByRoot: prefixes.Count == 0
                        ? null
                        : new Dictionary<string, IReadOnlyList<int>>
                        {
                            ["body"] = Array.Empty<int>()
                        },
                    DefaultRoot: "body",
                    SourceColliderPathIds: Array.Empty<long>(),
                    Colliders: Array.Empty<int>()
                );
            })
            .ToList();
    }

    private static IReadOnlyList<string> ResolveColliderFlagPrefixes(int colliderFlag)
    {
        var prefixes = new List<string>();
        if ((colliderFlag & 1) != 0)
        {
            prefixes.Add("CL_Hip");
        }
        if ((colliderFlag & 2) != 0)
        {
            prefixes.Add("CL_Chest");
        }
        if ((colliderFlag & 4) != 0)
        {
            prefixes.Add("CL_Left_Arm");
        }
        if ((colliderFlag & 8) != 0)
        {
            prefixes.Add("CL_Right_Arm");
        }
        if ((colliderFlag & 16) != 0)
        {
            prefixes.Add("CL_Left_Elbow");
        }
        if ((colliderFlag & 32) != 0)
        {
            prefixes.Add("CL_Right_Elbow");
        }
        return prefixes;
    }

    private PjskUnityRuntimeNativeMeshSet ExportNativeMeshes(
        string partType,
        IImported imported,
        PartRuntimeSpringBone springBone
    )
    {
        return nativeMeshExporter.ExportSinglePart(
            partType == "body" ? "Body" : partType == "head_optional" ? "Accessory" : "Head",
            imported,
            springBone.PrefabGraph,
            springBone.ActiveRootProfile.ActiveRoots
        );
    }

    private sealed record BuiltMaterialSlots(
        IReadOnlyList<PjskSekaiRuntimeMaterialSlot> Slots,
        IReadOnlyList<string> Warnings
    );

    private sealed record NativeMeshDeduplicationResult(
        PjskUnityRuntimeNativeMeshSet NativeMeshes,
        IReadOnlyList<string> Warnings
    );

    private static NativeMeshDeduplicationResult DeduplicateNativeMeshes(
        PjskUnityRuntimeNativeMeshSet nativeMeshes,
        IReadOnlyList<PjskSekaiRuntimeMaterialSlot> materialSlots
    )
    {
        var materialSlotByKey = materialSlots
            .GroupBy(slot => slot.MaterialKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var warnings = new List<string>();
        var meshes = nativeMeshes.Meshes
            .GroupBy(mesh => $"{mesh.PartKind}::{mesh.MeshPath}::{mesh.RendererTransformPath}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (group.Count() == 1)
                {
                    return group.First();
                }

                var ranked = group
                    .Select(mesh => new
                    {
                        Mesh = mesh,
                        Score = ScoreNativeMeshMaterialCompleteness(mesh, materialSlotByKey),
                    })
                    .OrderByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Mesh.RendererPathId)
                    .ToList();
                var kept = ranked[0];
                var dropped = ranked.Skip(1)
                    .Select(candidate => string.Join(",", candidate.Mesh.Submeshes.Select(submesh => submesh.MaterialKey).Distinct(StringComparer.Ordinal)))
                    .ToList();
                warnings.Add(
                    $"Duplicate native mesh '{kept.Mesh.RendererTransformPath}' kept material(s) {string.Join(",", kept.Mesh.Submeshes.Select(submesh => submesh.MaterialKey).Distinct(StringComparer.Ordinal))} and dropped {string.Join("; ", dropped)} by material texture completeness."
                );
                return kept.Mesh;
            })
            .ToList();

        return new NativeMeshDeduplicationResult(
            nativeMeshes with { Meshes = meshes },
            warnings
        );
    }

    private static int ScoreNativeMeshMaterialCompleteness(
        PjskUnityRuntimeNativeMesh mesh,
        IReadOnlyDictionary<string, PjskSekaiRuntimeMaterialSlot> materialSlotByKey
    )
    {
        var score = 0;
        foreach (var submesh in mesh.Submeshes)
        {
            if (!materialSlotByKey.TryGetValue(submesh.MaterialKey, out var slot))
            {
                continue;
            }
            score += string.IsNullOrWhiteSpace(slot.MainTex) ? 0 : 1;
            score += string.IsNullOrWhiteSpace(slot.ShadowTex) ? 0 : 2;
            score += string.IsNullOrWhiteSpace(slot.ValueTex) ? 0 : 4;
            score += string.IsNullOrWhiteSpace(slot.FaceShadowTex) ? 0 : 2;
        }
        return score;
    }

    private static IReadOnlyList<PjskSekaiRuntimeMaterialSlot> FilterMaterialSlotsForNativeMeshes(
        string partType,
        IReadOnlyList<PjskSekaiRuntimeMaterialSlot> materialSlots,
        PjskUnityRuntimeNativeMeshSet nativeMeshes
    )
    {
        if (partType == "head_optional" || nativeMeshes.Meshes.Count == 0)
        {
            return materialSlots;
        }

        var visibleMaterialKeys = nativeMeshes.Meshes
            .SelectMany(mesh => mesh.Submeshes)
            .Select(submesh => submesh.MaterialKey)
            .ToHashSet(StringComparer.Ordinal);
        return materialSlots
            .Where(slot => visibleMaterialKeys.Contains(slot.MaterialKey))
            .ToList();
    }

    private static BuiltMaterialSlots BuildMaterialSlots(
        string partType,
        BundleInventory inventory,
        IReadOnlyDictionary<string, string> textures,
        IImported imported,
        PjskUnityRuntimeNativeMeshSet nativeMeshes
    )
    {
        var materialLookup = MaterialIdentityLookup.FromInventory(inventory.Materials);
        var warnings = new List<string>();
        var slots = new List<PjskSekaiRuntimeMaterialSlot>();
        foreach (var mesh in inventory.SkinnedMeshes.Concat(inventory.StaticMeshes))
        {
            foreach (var slot in mesh.MaterialSlots)
            {
                MaterialInventory material;
                RuntimeMaterialIdentity identity;
                if (slot.MaterialPathId == 0)
                {
                    var resolvedMaterialName = ResolveNativeSubmeshMaterialName(nativeMeshes, mesh, slot)
                        ?? ResolveImportedSubmeshMaterialName(imported, mesh, slot);
                    if (string.IsNullOrWhiteSpace(resolvedMaterialName))
                    {
                        warnings.Add($"Skipped empty material slot {mesh.MeshName}[{slot.SlotIndex}]: no native/imported material was present.");
                        continue;
                    }
                    material = BuildSyntheticMaterialInventory(partType, imported, slot, resolvedMaterialName);
                    identity = RuntimeMaterialIdentityResolver.Resolve(partType, slot.SlotIndex, slot.MaterialFileId, slot.MaterialPathId, material.Name);
                }
                else
                {
                    material = materialLookup.Require(slot);
                    identity = new RuntimeMaterialIdentity(slot.MaterialKey, slot.MaterialFileId, slot.MaterialPathId);
                }

                var materialName = slot.MaterialName ?? material.Name;
                var mainTex = FindTextureSlot(material, "_MainTex");
                var shadowTex = FindTextureSlot(material, "_ShadowTex");
                var valueTex = FindTextureSlot(material, "_ValueTex");
                var faceShadowTex = FindTextureSlot(material, "_FaceShadowTex");
                var materialKind = partType == "body"
                    ? ClassifyBodyMaterialKind(materialName)
                    : partType == "head_optional" ? "accessory" : ClassifyHeadMaterialKind(materialName, faceShadowTex is not null);
                slots.Add(new PjskSekaiRuntimeMaterialSlot(
                    Part: partType,
                    MeshName: mesh.MeshName,
                    SlotIndex: slot.SlotIndex,
                    MaterialKey: identity.MaterialKey,
                    MaterialFileId: identity.MaterialFileId,
                    MaterialPathId: identity.MaterialPathId,
                    MaterialName: materialName,
                    MaterialKind: materialKind,
                    MainTex: RewriteTexturePath(mainTex, textures),
                    ShadowTex: RewriteTexturePath(shadowTex, textures),
                    ValueTex: RewriteTexturePath(valueTex, textures),
                    FaceShadowTex: RewriteTexturePath(faceShadowTex, textures),
                    RenderOrder: ResolveRenderOrder(materialKind),
                    ShaderPipeline: partType == "body" ? "sekai_csh_toon" : "character_tint_with_weak_sdf",
                    Lighting: SekaiMaterialMetadata.BuildLightingSettings(material)
                ));
            }
        }
        var distinctSlots = slots
            .DistinctBy(slot => $"{slot.MeshName}::{slot.SlotIndex}::{slot.MaterialKey}", StringComparer.Ordinal)
            .ToList();
        AddNativeMaterialSlotAliases(distinctSlots, nativeMeshes, warnings);
        return new BuiltMaterialSlots(
            Slots: distinctSlots
                .DistinctBy(slot => $"{slot.MeshName}::{slot.SlotIndex}::{slot.MaterialKey}", StringComparer.Ordinal)
                .ToList(),
            Warnings: warnings
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToList()
        );
    }

    private static void AddNativeMaterialSlotAliases(
        List<PjskSekaiRuntimeMaterialSlot> slots,
        PjskUnityRuntimeNativeMeshSet nativeMeshes,
        List<string> warnings
    )
    {
        foreach (var nativeMesh in nativeMeshes.Meshes)
        {
            foreach (var nativeSubmesh in nativeMesh.Submeshes)
            {
                var exact = slots.Any(slot =>
                    string.Equals(slot.MeshName, nativeMesh.MeshName, StringComparison.OrdinalIgnoreCase) &&
                    slot.SlotIndex == nativeSubmesh.SlotIndex &&
                    string.Equals(slot.MaterialKey, nativeSubmesh.MaterialKey, StringComparison.Ordinal));
                if (exact)
                {
                    continue;
                }

                var source = slots.FirstOrDefault(slot =>
                    slot.SlotIndex == nativeSubmesh.SlotIndex &&
                    string.Equals(slot.MaterialKey, nativeSubmesh.MaterialKey, StringComparison.Ordinal));
                if (source is null)
                {
                    warnings.Add($"Native submesh material slot missing for {nativeMesh.MeshName}[{nativeSubmesh.SlotIndex}] ({nativeSubmesh.MaterialKey}).");
                    continue;
                }

                slots.Add(source with { MeshName = nativeMesh.MeshName });
            }
        }
    }

    private static MaterialInventory BuildSyntheticMaterialInventory(
        string partType,
        IImported imported,
        RenderMaterialSlotInventory slot,
        string materialName
    )
    {
        var importedMaterial = imported.MaterialList.FirstOrDefault(material =>
            string.Equals(material.Name, materialName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Renderer material slot {slot.SlotIndex} has an empty material reference; imported material '{materialName}' was not found."
            );
        var identity = RuntimeMaterialIdentityResolver.Resolve(
            partType,
            slot.SlotIndex,
            slot.MaterialFileId,
            slot.MaterialPathId,
            importedMaterial.Name
        );
        return new MaterialInventory(
            MaterialFileId: identity.MaterialFileId,
            MaterialPathId: identity.MaterialPathId,
            MaterialKey: identity.MaterialKey,
            Name: importedMaterial.Name,
            ShaderName: null,
            TextureSlots: BuildSyntheticTextureSlots(importedMaterial),
            ColorProperties: new[]
            {
                new ColorPropertyInventory(
                    "_Color",
                    importedMaterial.Diffuse.R,
                    importedMaterial.Diffuse.G,
                    importedMaterial.Diffuse.B,
                    Math.Clamp(1.0f - importedMaterial.Transparency, 0.0f, 1.0f)
                ),
            },
            FloatProperties: new[]
            {
                new FloatPropertyInventory("_Transparency", importedMaterial.Transparency),
            }
        );
    }

    private static string? ResolveNativeSubmeshMaterialName(
        PjskUnityRuntimeNativeMeshSet nativeMeshes,
        RenderMeshInventory mesh,
        RenderMaterialSlotInventory slot
    )
    {
        var nativeMesh = nativeMeshes.Meshes.FirstOrDefault(candidate =>
            string.Equals(candidate.MeshName, mesh.MeshName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(candidate.MeshPath), mesh.MeshName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(LastPathSegment(candidate.RendererTransformPath), mesh.MeshName, StringComparison.OrdinalIgnoreCase));
        var nativeSubmesh = nativeMesh?.Submeshes.FirstOrDefault(submesh => submesh.SlotIndex == slot.SlotIndex);
        return string.IsNullOrWhiteSpace(nativeSubmesh?.MaterialName) ? null : nativeSubmesh.MaterialName;
    }

    private static string? ResolveImportedSubmeshMaterialName(
        IImported imported,
        RenderMeshInventory mesh,
        RenderMaterialSlotInventory slot
    )
    {
        var importedMesh = imported.MeshList.FirstOrDefault(candidate =>
            string.Equals(Path.GetFileName(candidate.Path), mesh.MeshName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Path, mesh.MeshName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(candidate.Path), Path.GetFileNameWithoutExtension(mesh.MeshName), StringComparison.OrdinalIgnoreCase));
        if (importedMesh is null || slot.SlotIndex < 0 || slot.SlotIndex >= importedMesh.SubmeshList.Count)
        {
            return slot.MaterialName;
        }

        var materialName = importedMesh.SubmeshList[slot.SlotIndex].Material;
        return string.IsNullOrWhiteSpace(materialName) ? slot.MaterialName : materialName;
    }

    private static IReadOnlyList<TextureSlotInventory> BuildSyntheticTextureSlots(ImportedMaterial material)
    {
        var textureNames = material.Textures
            .Select(texture => texture.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new[]
        {
            new TextureSlotInventory("_MainTex", SelectTexture(textureNames, "_C", "main", "diffuse") ?? textureNames.FirstOrDefault()),
            new TextureSlotInventory("_ShadowTex", SelectTexture(textureNames, "_S", "shadow")),
            new TextureSlotInventory("_ValueTex", SelectTexture(textureNames, "_V", "value")),
            new TextureSlotInventory("_FaceShadowTex", SelectTexture(textureNames, "faceshadow", "face_shadow")),
        }
        .Where(slot => !string.IsNullOrWhiteSpace(slot.TextureName))
        .ToList();
    }

    private static string? SelectTexture(IReadOnlyList<string> textureNames, params string[] markers)
    {
        return textureNames.FirstOrDefault(name =>
            markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<PjskSekaiRuntimeTextureRole> BuildTextureRoles(IReadOnlyList<PjskSekaiRuntimeMaterialSlot> slots)
    {
        var roles = new List<PjskSekaiRuntimeTextureRole>();
        foreach (var slot in slots)
        {
            AddTextureRole(roles, slot, "main", slot.MainTex);
            AddTextureRole(roles, slot, "shadow", slot.ShadowTex);
            AddTextureRole(roles, slot, "value", slot.ValueTex);
            AddTextureRole(roles, slot, "faceShadow", slot.FaceShadowTex);
        }
        return roles;
    }

    private static object BuildManifest(
        PartRegistryEntry entry,
        ResolvedBundleInput input,
        BundleInventory inventory,
        string partType,
        IReadOnlyDictionary<string, float>? characterHeightMetersById
    )
    {
        return new
        {
            id = $"{partType}-{entry.CharacterId:00}-{entry.Costume3dId}-{entry.Unit ?? "default"}",
            displayName = entry.Name,
            characterId = entry.CharacterId.ToString("00"),
            characterHeightMeters = CharacterHeightResolver.ResolveMeters(characterHeightMetersById, entry.CharacterId),
            materialPipeline = "embedded",
            source = new
            {
                bundleRoot = input.ResolvedBundlePath,
                meshUrl = "part-runtime.json",
                manifestUrl = "part-runtime.json",
            },
            rootNodeName = inventory.Roots.FirstOrDefault()?.Name,
            attachNode = entry.AttachNode,
            proxy = partType == "body"
                ? BuildPartBodyProxy(SekaiMaterialMetadata.BuildBodyProxy(inventory.Materials))
                : BuildPartHeadProxy(SekaiMaterialMetadata.BuildHeadProxy(inventory.Materials)),
        };
    }

    private static bool RuntimePackageHasCharacterHeight(string runtimePath, string runtimeJsonOutput)
    {
        if (!RuntimeJsonWriter.OutputsExist(runtimePath, runtimeJsonOutput))
        {
            return false;
        }

        var inputPath = RuntimeJsonWriter.NormalizeMode(runtimeJsonOutput) == RuntimeJsonWriter.Json
            ? runtimePath
            : RuntimeJsonWriter.GzipPath(runtimePath);
        try
        {
            using var stream = OpenRuntimeJsonForRead(inputPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("manifest", out var manifest) &&
                manifest.TryGetProperty("characterHeightMeters", out var height) &&
                height.ValueKind == JsonValueKind.Number &&
                height.GetSingle() > 0f;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static PartRuntimeMount BuildMount(PartRegistryEntry entry, BundleInventory inventory, string partType)
    {
        return new PartRuntimeMount(
            MountKind: partType switch
            {
                "body" => "character_root",
                "head_optional" => "attach_node",
                _ => "head_origin",
            },
            RootNodeName: inventory.Roots.FirstOrDefault()?.Name,
            AttachNode: entry.AttachNode,
            ExpectedSkeletonId: entry.CharacterId.ToString("00"),
            Notes: partType == "head_optional"
                ? new[] { "Viewer must verify attachNode exists in the current composed prefab graph before mounting." }
                : new[] { "Viewer composer mounts this part and rebuilds SpringBone runtime after changes." }
        );
    }

    private IReadOnlyList<PartRegistryEntry> LoadPartEntries(string masterDirectory, string assetRoot)
    {
        var output = new CostumeRegistryExporter().ExportInMemory(masterDirectory, assetRoot);
        return output.PartRegistry.Entries;
    }

    private static bool HasRequiredBundleFiles(PartRegistryEntry entry)
    {
        return entry.BundlePath is not null &&
            File.Exists(entry.BundlePath) &&
            (entry.ColorVariationBundlePath is null || File.Exists(entry.ColorVariationBundlePath));
    }

    private static PjskSpringBoneRuntimeManager BuildRuntimeManager(string partKind, SpringMonoBehaviourEntry manager, SpringBoneExport part)
    {
        var bonePathIds = part.Bones
            .Where(bone => ReadPathId(bone.Raw["m_manager"]) == manager.PathId)
            .Select(bone => bone.PathId)
            .ToList();
        return new PjskSpringBoneRuntimeManager(
            PartKind: partKind,
            PathId: manager.PathId,
            NodeName: manager.GameObject?.Name,
            NodePath: manager.GameObject?.TransformPath,
            PoseRoot: FirstPathSegment(manager.GameObject?.TransformPath),
            ActiveSelf: manager.GameObject?.ActiveSelf,
            ActiveInHierarchy: manager.GameObject?.ActiveInHierarchy,
            Enabled: ReadBool(manager.Raw, "m_Enabled", defaultValue: true),
            AutomaticUpdates: ReadBool(manager.Raw, "automaticUpdates", defaultValue: true),
            EnableLengthLimits: ReadBool(manager.Raw, "enableLengthLimits", defaultValue: true),
            EnableAngleLimits: ReadBool(manager.Raw, "enableAngleLimits", defaultValue: true),
            EnableCollision: ReadBool(manager.Raw, "enableCollision", defaultValue: true),
            CollideWithGround: ReadBool(manager.Raw, "collideWithGround", defaultValue: false),
            GroundHeight: ReadFloat(manager.Raw, "groundHeight", 0f),
            IsSumOfForcesOnBone: ReadBool(manager.Raw, "isSumOfForcesOnBone", defaultValue: true),
            IsPaused: ReadBool(manager.Raw, "isPaused", defaultValue: false),
            DynamicRatio: ReadFloat(manager.Raw, "dynamicRatio", 1f),
            SimulationFrameRate: (int)ReadFloat(manager.Raw, "simulationFrameRate", 60f),
            SlowMotionScale: ReadFloat(manager.Raw, "slowMotionScale", 1f),
            Bounce: ReadFloat(manager.Raw, "bounce", 0f),
            Friction: ReadFloat(manager.Raw, "friction", 0f),
            AnimatedBoneNames: Array.Empty<string>(),
            RawGravity: ReadVector(manager.Raw, "gravity"),
            ForceProviders: Array.Empty<VrmSpringBoneForceProviderCandidate>(),
            BonePathIds: bonePathIds
        );
    }

    private static PjskSpringBoneRuntimeBone BuildRuntimeBone(string partKind, SpringBoneEntry bone)
    {
        return new PjskSpringBoneRuntimeBone(
            PartKind: partKind,
            PathId: bone.PathId,
            NodeName: bone.GameObject?.Name,
            NodePath: bone.GameObject?.TransformPath,
            PoseRoot: FirstPathSegment(bone.GameObject?.TransformPath),
            ActiveSelf: bone.GameObject?.ActiveSelf,
            ActiveInHierarchy: bone.GameObject?.ActiveInHierarchy,
            Enabled: ReadBool(bone.Raw, "m_Enabled", defaultValue: true),
            PivotNodePath: bone.PivotNode?.TransformPath,
            PivotNodeName: bone.PivotNode?.Name,
            PivotSourcePathId: bone.PivotNode?.PathId,
            HitRadius: bone.Radius ?? 0f,
            Stiffness: bone.StiffnessForce ?? 0f,
            DragForce: bone.DragForce ?? 0f,
            GravityPower: ReadFloat(bone.Raw, "gravityPower", 0f),
            GravityDir: ReadVectorArray(bone.Raw, "gravityDir"),
            RawStiffnessForce: bone.StiffnessForce,
            RawDragForce: bone.DragForce,
            RawSpringForce: bone.SpringForce,
            RawWindInfluence: bone.WindInfluence,
            RawAngularStiffness: ReadNullableFloat(bone.Raw, "angularStiffness"),
            RawSpringConstant: ReadNullableFloat(bone.Raw, "springConstant"),
            LengthLimitTargets: bone.LengthLimitTargets.Select(target => new VrmSpringBoneLengthLimitTargetCandidate(target.Name, target.TransformPath, target.PathId)).ToList(),
            RawAngleLimits: new VrmSpringBoneAngleLimitsCandidate(
                Y: ReadAxisLimit(bone.Raw, "yAngleLimits"),
                Z: ReadAxisLimit(bone.Raw, "zAngleLimits")
            ),
            DirectColliderPathIds: bone.Colliders.Select(collider => collider.PathId).ToList(),
            ColliderFlag: (int)ReadFloat(bone.Raw, "colliderFlag", 0f)
        );
    }

    private static VrmSpringBoneAxisLimitCandidate? ReadAxisLimit(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value) || value is not JsonObject axis)
        {
            return null;
        }

        return new VrmSpringBoneAxisLimitCandidate(
            Active: ReadOptionalBool(axis, "active") ??
                ReadOptionalBool(axis, "enabled") ??
                ReadOptionalBool(axis, "Active") ??
                ReadOptionalBool(axis, "Enabled") ??
                ReadOptionalBool(axis, "m_Enabled") ??
                true,
            Min: ReadNullableFloat(axis, "min"),
            Max: ReadNullableFloat(axis, "max")
        );
    }

    private static bool? ReadOptionalBool(JsonObject raw, string name)
    {
        if (!raw.TryGetPropertyValue(name, out var value) || value is null)
        {
            return null;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<PjskSpringBoneRuntimeManagerColliderCache> BuildManagerColliderCaches(
        IReadOnlyList<PjskSpringBoneRuntimeManager> managers,
        IReadOnlyList<PjskSpringBoneRuntimeCollider> colliders
    )
    {
        return managers.Select(manager => new PjskSpringBoneRuntimeManagerColliderCache(
            ManagerPathId: manager.PathId,
            PartKind: manager.PartKind,
            SourcePoseRoot: manager.PoseRoot,
            RuntimeRoot: manager.PoseRoot ?? "unknown",
            ManagerNodeName: manager.NodeName,
            ManagerNodePath: manager.NodePath,
            SpringBonePathIds: manager.BonePathIds,
            SphereColliderIndexes: colliders.Where(collider => collider.Shape.Sphere is not null).Select(collider => collider.Index).ToList(),
            CapsuleColliderIndexes: colliders.Where(collider => collider.Shape.Capsule is not null).Select(collider => collider.Index).ToList(),
            PanelColliderIndexes: colliders.Where(collider => collider.Shape.Panel is not null).Select(collider => collider.Index).ToList(),
            Reason: "single-part package cache; viewer composer must re-filter after body collider merge"
        )).ToList();
    }

    private static SpringBoneExport EmptySpringBone(string partKind)
    {
        return new SpringBoneExport(
            Version: 1,
            BundlePath: string.Empty,
            PartKind: partKind,
            PrefabGraph: new SpringPrefabGraph(1, partKind, string.Empty, Array.Empty<SpringPrefabGameObject>(), Array.Empty<SpringPrefabTransform>(), Array.Empty<SpringPrefabRenderer>(), Array.Empty<SpringPrefabAnimator>(), Array.Empty<SpringPrefabMonoBehaviour>(), Array.Empty<long>()),
            Managers: Array.Empty<SpringMonoBehaviourEntry>(),
            Bones: Array.Empty<SpringBoneEntry>(),
            SphereColliders: Array.Empty<SpringColliderEntry>(),
            CapsuleColliders: Array.Empty<SpringColliderEntry>(),
            PanelColliders: Array.Empty<SpringColliderEntry>(),
            ForceProviders: Array.Empty<SpringMonoBehaviourEntry>(),
            SpringBonePivots: Array.Empty<SpringMonoBehaviourEntry>(),
            ExtraBones: Array.Empty<SpringExtraBoneEntry>(),
            CharacterHair: null,
            CharacterEye: null,
            Warnings: Array.Empty<string>()
        );
    }

    private static string NormalizePartType(string partType)
    {
        return partType.ToLowerInvariant() switch
        {
            "accessory" => "head_optional",
            "head_optional" => "head_optional",
            var value => value,
        };
    }

    private static string ResolveRuntimePartType(PartRegistryEntry entry)
    {
        if (string.Equals(entry.HeadCostume3dAssetbundleType, "head_only", StringComparison.OrdinalIgnoreCase))
        {
            return "head_optional";
        }

        return NormalizePartType(entry.PartType);
    }

    private static string ToRuntimePartKind(string partType)
    {
        return partType == "body" ? "Body" : "Head";
    }

    private static string? SelectHeadRootName(BundleInventory inventory)
    {
        return inventory.Roots.FirstOrDefault(root => string.Equals(root.Name, "face", StringComparison.OrdinalIgnoreCase))?.Name
            ?? inventory.Roots.FirstOrDefault()?.Name;
    }

    private static string? SelectAccessoryRootName(BundleInventory inventory)
    {
        return inventory.Roots.FirstOrDefault()?.Name;
    }

    private static string? FirstPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        var normalized = path.Replace('\\', '/').Trim('/');
        var slash = normalized.IndexOf('/');
        return slash < 0 ? normalized : normalized[..slash];
    }

    private static string? LastPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        var normalized = path.Replace('\\', '/').Trim('/');
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string? TryRelativePath(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path)).Replace('\\', '/');
        }
        catch
        {
            return null;
        }
    }

    private static TextureSlotInventory? FindTextureSlot(MaterialInventory? material, string slotName)
    {
        return material?.TextureSlots
            .FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? RewriteTexturePath(TextureSlotInventory? textureSlot, IReadOnlyDictionary<string, string> textures)
    {
        var textureName = textureSlot?.TextureName;
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(textureSlot?.TextureKey) &&
            textures.TryGetValue(textureSlot.TextureKey, out var referencedPath))
        {
            return referencedPath;
        }

        return textures.TryGetValue(textureName, out var path)
            ? path
            : textures.TryGetValue(Path.GetFileNameWithoutExtension(textureName), out var stemPath) ? stemPath : null;
    }

    private static void AddTextureRole(List<PjskSekaiRuntimeTextureRole> roles, PjskSekaiRuntimeMaterialSlot slot, string role, string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }
        roles.Add(new PjskSekaiRuntimeTextureRole(
            Part: slot.Part,
            MaterialKey: slot.MaterialKey,
            MaterialFileId: slot.MaterialFileId,
            MaterialPathId: slot.MaterialPathId,
            MaterialName: slot.MaterialName,
            MaterialKind: slot.MaterialKind,
            Role: role,
            Uri: uri
        ));
    }

    private static string ClassifyBodyMaterialKind(string materialName)
    {
        var lower = materialName.ToLowerInvariant();
        return lower.Contains("skin") ? "skin" : "body";
    }

    private static string ClassifyHeadMaterialKind(string materialName, bool hasFaceShadowTex)
    {
        var name = materialName.ToLowerInvariant();
        if (name.Contains("eyelash"))
        {
            return "eyelash";
        }
        if (name.Contains("eyebrow"))
        {
            return "eyebrow";
        }
        if (name.Contains("eye_highlight") || name.Contains("_ehl_"))
        {
            return "eyelight";
        }
        if (name.Contains("_eye"))
        {
            return "eye";
        }
        if (name.Contains("_hair_"))
        {
            return "hair";
        }
        if (name.Contains("_acc_"))
        {
            return "accessory";
        }
        if (hasFaceShadowTex)
        {
            return "face_sdf";
        }
        return "face";
    }

    private static int ResolveRenderOrder(string materialKind)
    {
        return materialKind switch
        {
            "face_sdf" => 10,
            "hair" or "accessory" => 12,
            "eyelash" or "eyebrow" => 20,
            "eye" => 24,
            "eyelight" => 28,
            _ => 0,
        };
    }

    private float ResolveCharacterHeightMeters(string characterId)
    {
        return characterHeightMetersById.TryGetValue(characterId.PadLeft(2, '0'), out var height)
            ? height
            : 1.00f;
    }

    private static object BuildPartBodyProxy(BodyProxySettings proxy)
    {
        return new
        {
            bodyColor = proxy.BodyColor,
            shadowColor = proxy.ShadowColor,
            bodyScale = proxy.BodyScale,
            torsoLength = proxy.TorsoLength,
            shoulderWidth = proxy.ShoulderWidth,
        };
    }

    private static object BuildPartHeadProxy(HeadProxySettings proxy)
    {
        return new
        {
            faceColor = proxy.FaceColor,
            faceShadeColor = proxy.FaceShadeColor,
            skinColorDefault = proxy.SkinColorDefault,
            skinColor1 = proxy.SkinColor1,
            skinColor2 = proxy.SkinColor2,
            hairColor = proxy.HairColor,
            hairShadowColor = proxy.HairShadowColor,
            headRadius = proxy.HeadRadius,
            faceDepth = proxy.FaceDepth,
            hairArc = proxy.HairArc,
        };
    }

    private static readonly IReadOnlyDictionary<string, float> DefaultCharacterHeightMetersById =
        new Dictionary<string, float>
        {
            ["01"] = 1.61f,
            ["02"] = 1.59f,
            ["03"] = 1.66f,
            ["04"] = 1.59f,
            ["05"] = 1.58f,
            ["06"] = 1.63f,
            ["07"] = 1.56f,
            ["08"] = 1.68f,
            ["09"] = 1.56f,
            ["10"] = 1.60f,
            ["11"] = 1.74f,
            ["12"] = 1.78f,
            ["13"] = 1.72f,
            ["14"] = 1.52f,
            ["15"] = 1.56f,
            ["16"] = 1.80f,
            ["17"] = 1.54f,
            ["18"] = 1.62f,
            ["19"] = 1.58f,
            ["20"] = 1.63f,
            ["21"] = 1.58f,
            ["22"] = 1.52f,
            ["23"] = 1.56f,
            ["24"] = 1.62f,
            ["25"] = 1.67f,
            ["26"] = 1.75f,
        };

    private static IReadOnlyList<HeadMorphChannel> ReadHeadMorphBindings(IImported importedHead)
    {
        return importedHead.MorphList
            .Where(morph => morph.Path.EndsWith("/Face", StringComparison.OrdinalIgnoreCase) || string.Equals(morph.Path, "face/Face", StringComparison.OrdinalIgnoreCase))
            .SelectMany(morph => morph.Channels.Select(channel => new HeadMorphChannel(
                Name: channel.Name,
                SourceName: channel.Name,
                NameHash: Fnv1A32(channel.Name),
                CurveHash: Fnv1A32($"blendShape.{channel.Name}")
            )))
            .DistinctBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static uint Fnv1A32(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }

    private static bool ReadBool(System.Text.Json.Nodes.JsonObject raw, string name, bool defaultValue)
    {
        return raw[name] is { } node && bool.TryParse(node.ToString(), out var value) ? value : defaultValue;
    }

    private static float ReadFloat(System.Text.Json.Nodes.JsonObject raw, string name, float defaultValue)
    {
        return raw[name] is { } node &&
            float.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static float? ReadNullableFloat(System.Text.Json.Nodes.JsonObject raw, string name)
    {
        return raw[name] is { } node &&
            float.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static SpringVector3? ReadVector(System.Text.Json.Nodes.JsonObject raw, string name)
    {
        var node = raw[name]?.AsObject();
        if (node is null)
        {
            return null;
        }
        return new SpringVector3(
            ReadFloat(node, "x", ReadFloat(node, "X", 0f)),
            ReadFloat(node, "y", ReadFloat(node, "Y", 0f)),
            ReadFloat(node, "z", ReadFloat(node, "Z", 0f))
        );
    }

    private static float[] ReadVectorArray(System.Text.Json.Nodes.JsonObject raw, string name)
    {
        var vector = ReadVector(raw, name);
        return vector is null ? new[] { 0f, -1f, 0f } : new[] { vector.X, vector.Y, vector.Z };
    }

    private static Stream OpenRuntimeJsonForRead(string path)
    {
        var stream = File.OpenRead(path);
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress)
            : stream;
    }

    private static void WriteJson<T>(string path, T value, string runtimeJsonOutput)
    {
        RuntimeJsonWriter.Write(path, value, WriteJsonOptions, runtimeJsonOutput);
    }

    private static long? ReadPathId(System.Text.Json.Nodes.JsonNode? node)
    {
        var value = node?["m_PathID"]?.ToString();
        return long.TryParse(value, out var pathId) ? pathId : null;
    }
}

public sealed record PartPackageExportResult(
    PartRegistryEntry Entry,
    string RuntimePath,
    IReadOnlyList<string> Warnings,
    bool Succeeded = true
);

public sealed class PartPackageExportManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string? manifestPath;
    private readonly Dictionary<string, PartPackageInputStamp> packages;

    private PartPackageExportManifest(string? manifestPath, Dictionary<string, PartPackageInputStamp> packages)
    {
        this.manifestPath = manifestPath;
        this.packages = packages;
    }

    public static PartPackageExportManifest Load(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return new PartPackageExportManifest(manifestPath, new Dictionary<string, PartPackageInputStamp>(StringComparer.Ordinal));
        }

        var packages = JsonSerializer.Deserialize<Dictionary<string, PartPackageInputStamp>>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) ?? new Dictionary<string, PartPackageInputStamp>(StringComparer.Ordinal);
        return new PartPackageExportManifest(manifestPath, new Dictionary<string, PartPackageInputStamp>(packages, StringComparer.Ordinal));
    }

    public bool CanSkip(string packagePath, string runtimePath, PartPackageInputStamp stamp)
    {
        return !string.IsNullOrWhiteSpace(manifestPath) &&
            File.Exists(runtimePath) &&
            packages.TryGetValue(packagePath, out var existing) &&
            existing == stamp;
    }

    public void Update(string packagePath, PartPackageInputStamp stamp)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        packages[packagePath] = stamp;
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(packages, JsonOptions));
    }

    public static void Merge(string manifestPath, IEnumerable<string> shardManifestPaths)
    {
        var merged = new Dictionary<string, PartPackageInputStamp>(StringComparer.Ordinal);
        foreach (var shardManifestPath in shardManifestPaths.Where(File.Exists))
        {
            var packages = JsonSerializer.Deserialize<Dictionary<string, PartPackageInputStamp>>(
                File.ReadAllText(shardManifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new Dictionary<string, PartPackageInputStamp>(StringComparer.Ordinal);
            foreach (var pair in packages)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        var parent = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(merged, JsonOptions));
    }
}

public sealed record PartPackageInputStamp(
    string BundlePath,
    long BundleLength,
    long BundleLastWriteUtcTicks,
    string? ColorVariationBundlePath,
    long? ColorVariationLength,
    long? ColorVariationLastWriteUtcTicks
)
{
    public static PartPackageInputStamp From(PartRegistryEntry entry)
    {
        if (entry.BundlePath is null)
        {
            throw new InvalidOperationException($"Part entry {entry.PackagePath} has no bundle path.");
        }

        var bundle = new FileInfo(entry.BundlePath);
        FileInfo? colorVariation = entry.ColorVariationBundlePath is null
            ? null
            : new FileInfo(entry.ColorVariationBundlePath);
        return new PartPackageInputStamp(
            BundlePath: entry.BundlePath,
            BundleLength: bundle.Length,
            BundleLastWriteUtcTicks: bundle.LastWriteTimeUtc.Ticks,
            ColorVariationBundlePath: entry.ColorVariationBundlePath,
            ColorVariationLength: colorVariation?.Length,
            ColorVariationLastWriteUtcTicks: colorVariation?.LastWriteTimeUtc.Ticks
        );
    }
}
