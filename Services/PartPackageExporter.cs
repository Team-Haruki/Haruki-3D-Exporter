using System.Text.Json;
using System.Globalization;
using AssetStudio;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class PartPackageExporter
{
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly BundleInputResolver resolver = new();
    private readonly AssetStudioBundleParser parser = new();
    private readonly AssetStudioImportedModelFactory modelFactory = new();
    private readonly SpringBoneExporter springBoneExporter = new();
    private readonly UnityRuntimeNativeMeshExporter nativeMeshExporter = new();
    private readonly UnityRuntimeTextureExporter textureExporter = new();

    public IReadOnlyList<PartPackageExportResult> ExportAll(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        string? manifestPath = null
    )
    {
        var manifest = PartPackageExportManifest.Load(manifestPath);
        var partEntries = LoadPartEntries(masterDirectory, assetRoot)
            .Where(entry => entry.BundlePath is not null && entry.Status != "missing")
            .Where(HasRequiredBundleFiles)
            .ToList();
        var results = new List<PartPackageExportResult>();
        foreach (var entry in partEntries)
        {
            var packageDirectory = Path.Combine(outputDirectory, entry.PackagePath.Replace('/', Path.DirectorySeparatorChar));
            var runtimePath = Path.Combine(packageDirectory, "part-runtime.json");
            var stamp = PartPackageInputStamp.From(entry);
            if (manifest.CanSkip(entry.PackagePath, runtimePath, stamp))
            {
                results.Add(new PartPackageExportResult(entry, runtimePath, Array.Empty<string>()));
                continue;
            }

            var result = Export(entry, assetRoot, outputDirectory);
            manifest.Update(entry.PackagePath, stamp);
            results.Add(result);
        }
        manifest.Save();

        return results;
    }

    public PartPackageExportResult ExportOne(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        int costume3dId,
        string partType,
        string? unit
    )
    {
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

        return Export(entry, assetRoot, outputDirectory);
    }

    public PartPackageExportResult Export(PartRegistryEntry entry, string assetRoot, string outputDirectory)
    {
        if (entry.BundlePath is null)
        {
            throw new InvalidOperationException($"Part entry {entry.Costume3dId}/{entry.PartType} has no bundle path.");
        }

        var packageDirectory = Path.Combine(outputDirectory, entry.PackagePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(packageDirectory);
        var normalizedType = ResolveRuntimePartType(entry);
        var input = normalizedType == "body"
            ? resolver.ResolveBody(entry.BundlePath)
            : resolver.ResolveHead(entry.BundlePath);
        input = input with { CharacterId = entry.CharacterId.ToString("00") };

        var inventory = parser.Parse(input);
        var imported = modelFactory.CreateImportedModel(
            input,
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
            imported,
            overrideTextures
        );
        var springBone = springBoneExporter.Export(input);
        var runtimeSpringBone = BuildPartSpringBone(normalizedType, springBone);
        var nativeMeshes = ExportNativeMeshes(normalizedType, imported, runtimeSpringBone);
        var materialSlots = BuildMaterialSlots(normalizedType, inventory, textures);
        var textureRoles = BuildTextureRoles(materialSlots);
        var package = new PartRuntimePackage(
            Version: "0414-part-1",
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
            Manifest: BuildManifest(entry, input, inventory, normalizedType),
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
                .Concat(runtimeSpringBone.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        );

        var runtimePath = Path.Combine(packageDirectory, "part-runtime.json");
        WriteJson(runtimePath, package);
        return new PartPackageExportResult(entry, runtimePath, package.Warnings);
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
                Notes: new[] { "Resolved by viewer runtime composer." }
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

    private static IReadOnlyList<PjskSekaiRuntimeMaterialSlot> BuildMaterialSlots(
        string partType,
        BundleInventory inventory,
        IReadOnlyDictionary<string, string> textures
    )
    {
        var materialMap = inventory.Materials.ToDictionary(material => material.Name, StringComparer.OrdinalIgnoreCase);
        return inventory.SkinnedMeshes
            .Concat(inventory.StaticMeshes)
            .SelectMany(mesh => mesh.MaterialNames.Select(materialName =>
            {
                materialMap.TryGetValue(materialName, out var material);
                var materialKind = partType == "body"
                    ? ClassifyBodyMaterialKind(materialName)
                    : partType == "head_optional" ? "accessory" : ClassifyHeadMaterialKind(materialName, FindTextureSlot(material, "_FaceShadowTex") is not null);
                return new PjskSekaiRuntimeMaterialSlot(
                    Part: partType,
                    MeshName: mesh.MeshName,
                    MaterialName: materialName,
                    MaterialKind: materialKind,
                    MainTex: RewriteTexturePath(FindTextureSlot(material, "_MainTex"), textures),
                    ShadowTex: RewriteTexturePath(FindTextureSlot(material, "_ShadowTex"), textures),
                    ValueTex: RewriteTexturePath(FindTextureSlot(material, "_ValueTex"), textures),
                    FaceShadowTex: RewriteTexturePath(FindTextureSlot(material, "_FaceShadowTex"), textures),
                    RenderOrder: ResolveRenderOrder(materialKind),
                    ShaderPipeline: partType == "body" ? "sekai_csh_toon" : "character_tint_with_weak_sdf",
                    Lighting: BuildDefaultLighting()
                );
            }))
            .DistinctBy(slot => $"{slot.MeshName}::{slot.MaterialName}", StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        string partType
    )
    {
        return new
        {
            id = $"{partType}-{entry.CharacterId:00}-{entry.Costume3dId}-{entry.Unit ?? "default"}",
            displayName = entry.Name,
            characterId = entry.CharacterId.ToString("00"),
            source = new
            {
                bundleRoot = input.ResolvedBundlePath,
                meshUrl = "part-runtime.json",
                manifestUrl = "part-runtime.json",
            },
            rootNodeName = inventory.Roots.FirstOrDefault()?.Name,
            attachNode = entry.AttachNode,
        };
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
            IsSumOfForcesOnBone: ReadBool(manager.Raw, "isSumOfForcesOnBone", defaultValue: false),
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
            RawAngleLimits: new VrmSpringBoneAngleLimitsCandidate(null, null),
            DirectColliderPathIds: bone.Colliders.Select(collider => collider.PathId).ToList(),
            ColliderFlag: (int)ReadFloat(bone.Raw, "colliderFlag", 0f)
        );
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

    private static string? FindTextureSlot(MaterialInventory? material, string slotName)
    {
        return material?.TextureSlots.FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase))?.TextureName;
    }

    private static string? RewriteTexturePath(string? textureName, IReadOnlyDictionary<string, string> textures)
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return null;
        }
        return textures.TryGetValue(textureName, out var path)
            ? path
            : textures.TryGetValue(Path.GetFileNameWithoutExtension(textureName), out var stemPath) ? stemPath : textureName;
    }

    private static void AddTextureRole(List<PjskSekaiRuntimeTextureRole> roles, PjskSekaiRuntimeMaterialSlot slot, string role, string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }
        roles.Add(new PjskSekaiRuntimeTextureRole(slot.Part, slot.MaterialName, slot.MaterialKind, role, uri));
    }

    private static string ClassifyBodyMaterialKind(string materialName)
    {
        var lower = materialName.ToLowerInvariant();
        return lower.Contains("skin") ? "skin" : "body";
    }

    private static string ClassifyHeadMaterialKind(string materialName, bool hasFaceShadowTex)
    {
        var lower = materialName.ToLowerInvariant();
        if (lower.Contains("eye_highlight") || lower.Contains("_ehl_"))
        {
            return "eyelight";
        }
        if (lower.Contains("eye"))
        {
            return "eye";
        }
        if (lower.Contains("hair"))
        {
            return "hair";
        }
        return hasFaceShadowTex ? "face_sdf" : "face";
    }

    private static int ResolveRenderOrder(string materialKind)
    {
        return materialKind switch
        {
            "face_sdf" or "face" => 10,
            "eye" => 20,
            "eyelight" => 30,
            "hair" or "accessory" => 12,
            _ => 0,
        };
    }

    private static MaterialLightingSettings BuildDefaultLighting()
    {
        return new MaterialLightingSettings(
            SpecularPower: 0.35f,
            RimThreshold: 0.2f,
            ShadowTexWeight: 1f,
            Saturation: 1f,
            PartsAmbientColor: "#ffffff",
            ReflectionBlendColor: "#ffffff",
            OutlineWidth: 0.006f,
            OutlineOffset: 0f,
            OutlineLightness: 0.4f,
            ShadowWidth: 0.35f,
            UseOutlineSecondNormal: 0f,
            DistortionFps: 0f,
            DistortionIntensity: 0f,
            DistortionIntensityX: 0f,
            DistortionIntensityY: 0f,
            DistortionOffsetX: 0f,
            DistortionOffsetY: 0f,
            DistortionScrollSpeed: 0f,
            DistortionScrollX: 0f,
            DistortionScrollY: 0f,
            DistortionTexTilingX: 1f,
            DistortionTexTilingY: 1f,
            Threshold: 0.5f,
            LightInfluence: 0.5f,
            LightInfluenceForEyeHighlight: 0.5f
        );
    }

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

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, WriteJsonOptions));
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
    IReadOnlyList<string> Warnings
);

public sealed class PartPackageExportManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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
