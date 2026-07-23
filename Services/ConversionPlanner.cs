using System.Text.Json;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class ConversionPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly IReadOnlyDictionary<string, float> characterHeightMetersById;

    public ConversionPlanner(
        IReadOnlyDictionary<string, float>? characterHeightMetersById = null
    )
    {
        this.characterHeightMetersById =
            characterHeightMetersById ?? DefaultCharacterHeightMetersById;
    }

    public ConversionPlan CreatePlan(
        ResolvedBundleInput body,
        ResolvedBundleInput head,
        string outputDirectory,
        BundleInventory bodyInventory,
        BundleInventory headInventory,
        string? headRootOverride = null
    )
    {
        var normalizedOut = Path.GetFullPath(outputDirectory);
        var characterId = body.CharacterId != "unknown"
            ? body.CharacterId
            : head.CharacterId;
        var skeletonId = $"characterv2_{characterId}_humanoid";
        var summary = new ConversionPlanSummary(
            characterId,
            normalizedOut,
            body.OriginalInputPath,
            body.ResolvedBundlePath,
            head.OriginalInputPath,
            head.ResolvedBundlePath,
            skeletonId,
            bodyInventory.Roots.Count,
            headInventory.Roots.Count,
            bodyInventory.SkinnedMeshes.Count,
            headInventory.SkinnedMeshes.Count,
            new[]
            {
                "This conversion plan now includes real bundle inventory extracted via AssetStudio.",
                "Body input was resolved from a directory-aware bundle locator.",
                "Head input was resolved from a file-aware bundle locator.",
            }
        );

        var bodyManifest = BuildBodyTemplate(body, skeletonId, bodyInventory);
        var headManifest = BuildHeadTemplate(head, skeletonId, headInventory, bodyInventory, headRootOverride);
        return new ConversionPlan(
            Path.Combine(normalizedOut, "conversion-plan.json"),
            Path.Combine(normalizedOut, "body.inventory.json"),
            Path.Combine(normalizedOut, "head.inventory.json"),
            Path.Combine(normalizedOut, "body.manifest.template.json"),
            Path.Combine(normalizedOut, "head.manifest.template.json"),
            Path.Combine(normalizedOut, "sekai-vrm-profile.json"),
            Path.Combine(normalizedOut, "body.springbone.json"),
            Path.Combine(normalizedOut, "head.springbone.json"),
            Path.Combine(normalizedOut, "springbone.json"),
            Path.Combine(normalizedOut, "vrm-springbone.candidate.json"),
            Path.Combine(normalizedOut, "vrmc-springbone.extension.json"),
            Path.Combine(normalizedOut, "vrmc-springbone.resolve-report.json"),
            Path.Combine(normalizedOut, "vrmc-vrm.extension.json"),
            Path.Combine(normalizedOut, "vrmc-vrm.resolve-report.json"),
            Path.Combine(normalizedOut, "pjsk-sekai-runtime.extension.json"),
            Path.Combine(normalizedOut, "pjsk-sekai-runtime.resolve-report.json"),
            Path.Combine(normalizedOut, "character", "character.springbone.glb"),
            Path.Combine(normalizedOut, "character", "character.prefab-runtime.glb"),
            Path.Combine(normalizedOut, "character", "unity-runtime.json"),
            Path.Combine(normalizedOut, "character", "unity-runtime.bin"),
            Path.Combine(normalizedOut, "character", "character.vrm-core.glb"),
            Path.Combine(normalizedOut, "character", "character.vrm-candidate.glb"),
            Path.Combine(normalizedOut, "character", "character.vrm"),
            summary,
            bodyInventory,
            headInventory,
            bodyManifest,
            headManifest,
            BuildSekaiVrmProfile(characterId)
        );
    }

    public async Task WritePlanAsync(ConversionPlan plan)
    {
        await using var stream = File.Create(plan.PlanPath);
        await JsonSerializer.SerializeAsync(stream, plan.Summary, JsonOptions);
    }

    public async Task WriteManifestTemplatesAsync(ConversionPlan plan)
    {
        await using (var bodyStream = File.Create(plan.BodyManifestTemplatePath))
        {
            await JsonSerializer.SerializeAsync(
                bodyStream,
                plan.BodyManifestTemplate,
                JsonOptions
            );
        }

        await using (var headStream = File.Create(plan.HeadManifestTemplatePath))
        {
            await JsonSerializer.SerializeAsync(
                headStream,
                plan.HeadManifestTemplate,
                JsonOptions
            );
        }
    }

    public async Task WriteInventoriesAsync(ConversionPlan plan)
    {
        await using (var bodyStream = File.Create(plan.BodyInventoryPath))
        {
            await JsonSerializer.SerializeAsync(bodyStream, plan.BodyInventory, JsonOptions);
        }

        await using (var headStream = File.Create(plan.HeadInventoryPath))
        {
            await JsonSerializer.SerializeAsync(headStream, plan.HeadInventory, JsonOptions);
        }
    }

    public async Task WriteSekaiVrmProfileAsync(ConversionPlan plan)
    {
        await using var stream = File.Create(plan.SekaiVrmProfilePath);
        await JsonSerializer.SerializeAsync(stream, plan.SekaiVrmProfile, JsonOptions);
    }

    private BodyAssetManifest BuildBodyTemplate(
        ResolvedBundleInput body,
        string skeletonId,
        BundleInventory inventory
    )
    {
        var rootName = inventory.Roots.FirstOrDefault()?.Name ?? "BodyRoot";
        var neckAttachNode = SelectPreferredBodyAttachNode(inventory.AttachNodeCandidates);
        var materialLookup = MaterialIdentityLookup.FromInventory(inventory.Materials);
        var bodyMaterialSlots = inventory.SkinnedMeshes
            .Concat(inventory.StaticMeshes)
            .SelectMany(mesh => mesh.MaterialSlots.Select(slot =>
            {
                var material = materialLookup.Require(slot);
                return new BodyMaterialSlot(
                    MeshName: mesh.MeshName,
                    SlotIndex: slot.SlotIndex,
                    MaterialFileId: slot.MaterialFileId,
                    MaterialPathId: slot.MaterialPathId,
                    MaterialKey: slot.MaterialKey,
                    MaterialName: slot.MaterialName,
                    MaterialKind: ClassifyBodyMaterialKind(slot.MaterialName ?? material.Name),
                    MainTex: FindTextureSlot(material, "_MainTex"),
                    ShadowTex: FindTextureSlot(material, "_ShadowTex"),
                    ValueTex: FindTextureSlot(material, "_ValueTex"),
                    Lighting: SekaiMaterialMetadata.BuildLightingSettings(material),
                    RawMaterial: SekaiMaterialMetadata.BuildRawMaterialProperties(material)
                );
            }))
            .DistinctBy(
                slot => $"{slot.MeshName}::{slot.SlotIndex}::{slot.MaterialKey}",
                StringComparer.Ordinal
            )
            .ToList();

        var bodyTintSource = inventory.Materials
            .FirstOrDefault(HasSkinColorProperty);
        var bodyColor = FindColorProperty(bodyTintSource, "_DefaultSkinColor")
            ?? FindColorProperty(bodyTintSource, "_SkinColorDefault")
            ?? "#f2d0c3";
        var bodyShadowColor = FindColorProperty(bodyTintSource, "_Shadow1SkinColor")
            ?? bodyColor;

        return new BodyAssetManifest(
            Id: $"body-{body.CharacterId}-{body.BundleStem}",
            DisplayName: inventory.Roots.FirstOrDefault()?.Name ?? $"Body {body.CharacterId} {body.BundleStem}",
            CharacterId: body.CharacterId,
            CharacterHeightMeters: ResolveCharacterHeightMeters(body.CharacterId),
            Source: new AssetSource(
                BundleRoot: body.ResolvedBundlePath,
                ManifestUrl: "body.manifest.json",
                MeshUrl: "character/unity-runtime.json",
                SkeletonUrl: "character/unity-runtime.json",
                AnimationUrls: Array.Empty<string>()
            ),
            NeckAnchor: new Vec3(0f, 1.75f, 0.15f),
            Skeleton: new BodySkeletonMetadata(
                SkeletonId: skeletonId,
                RootNodeName: rootName,
                NeckAttach: new AssemblyAnchor(
                    NodeName: neckAttachNode,
                    FallbackPosition: new Vec3(0f, 1.75f, 0.15f)
                )
            ),
            BodyMaterials: bodyMaterialSlots,
            Proxy: new BodyProxySettings(
                BodyColor: bodyColor,
                ShadowColor: bodyShadowColor,
                BodyScale: 1.0f,
                TorsoLength: 2.2f,
                ShoulderWidth: 1.1f
            )
        );
    }

    private SekaiVrmMigrationProfile BuildSekaiVrmProfile(string characterId)
    {
        var normalizedCharacterId = characterId.PadLeft(2, '0');
        var characterHeight = ResolveCharacterHeightMeters(normalizedCharacterId);
        var preview = new PreviewLightProfile(
            X: -1.6f,
            Y: 0.9f,
            Z: 0.75f,
            Intensity: 0.48f,
            Ambient: 0.16f,
            ShadowThreshold: 0.33f,
            ShadowWeight: 1f,
            CharacterAmbient: 0.12f,
            RimIntensity: 0.18f,
            RimThreshold: 0.18f,
            RimDirectionality: 0.85f,
            FaceSoftness: 0.96f,
            FaceSdfUseLightDirection: 0.5f,
            CharacterHeight: characterHeight
        );
        var runtimeProfile = new SekaiRuntimeMaterialProfile(
            Version: 1,
            BodyPipeline: "sekai_csh_toon",
            FacePipeline: "character_tint_with_weak_sdf",
            LayerPipeline: "sekai_eye_layers",
            VrmStrategy: "mtoon_fallback_with_sekai_extras",
            TextureRoles: new Dictionary<string, string>
            {
                ["main"] = "Sekai C",
                ["shadow"] = "Sekai S",
                ["value"] = "Sekai H",
                ["faceShadow"] = "Sekai SDF",
            },
            PluginPreview: new PluginPreviewProfile(
                DirectionalLocation: new Vec3(-1.6f, -0.75f, 0.9f),
                DirectionalEnergy: 0.48f,
                AmbientIntensity: 0.16f,
                ShadowThreshold: 0.33f,
                ShadowWeight: 1f,
                CharacterAmbientIntensity: 0.12f,
                RimIntensity: 0.18f,
                RimThreshold: 0.18f,
                RimDirectionality: 0.85f,
                RimRotationDegrees: new Vec3(135f, 0f, -90f)
            ),
            ViewerTunedPreview: preview
        );

        return new SekaiVrmMigrationProfile(
            Version: 1,
            TargetContainer: new VrmTargetContainerProfile(
                Preferred: "unity-runtime.json",
                Fallback: "none",
                CurrentPhase: "pure_unity_runtime_json_0414"
            ),
            StandardVrmFallback: new StandardVrmFallbackProfile(
                HumanoidBoneMap: new Dictionary<string, string>
                {
                    ["hips"] = "Hip",
                    ["spine"] = "Spine",
                    ["chest"] = "Chest",
                    ["neck"] = "Neck",
                    ["head"] = "Head",
                    ["leftShoulder"] = "Left_Shoulder",
                    ["leftUpperArm"] = "Left_Arm",
                    ["leftLowerArm"] = "Left_Elbow",
                    ["leftHand"] = "Left_Wrist",
                    ["rightShoulder"] = "Right_Shoulder",
                    ["rightUpperArm"] = "Right_Arm",
                    ["rightLowerArm"] = "Right_Elbow",
                    ["rightHand"] = "Right_Wrist",
                    ["leftUpperLeg"] = "Left_Thigh",
                    ["leftLowerLeg"] = "Left_Knee",
                    ["leftFoot"] = "Left_Ankle",
                    ["leftToes"] = "Left_Toe",
                    ["rightUpperLeg"] = "Right_Thigh",
                    ["rightLowerLeg"] = "Right_Knee",
                    ["rightFoot"] = "Right_Ankle",
                    ["rightToes"] = "Right_Toe",
                },
                MaterialFallback: new VrmMaterialFallbackProfile(
                    Shader: "MToon",
                    MainTex: "baseColorTexture",
                    ShadowTex: "shadeMultiplyTexture",
                    ValueTex: "custom extras for rim/spec mask",
                    FaceShadowTex: "custom extras only"
                ),
                ExpressionSource: "PJSK morphChannelBindings",
                SpringBoneSource: "raw_bundle_springbone_json"
            ),
            SekaiRuntimeExtras: new SekaiRuntimeExtrasProfile(
                ExtensionName: "PJSK_sekai_runtime",
                RequiredForExactViewerRender: true,
                PayloadKeys: new[]
                {
                    "sekaiRuntimeMaterialProfile",
                    "bodyManifest",
                    "headManifest",
                    "materialSlots",
                    "textureRoles",
                    "morphChannelBindings",
                    "bodyHeadAssembly",
                    "pjskSpringBone",
                    "springBoneSourceMetadata",
                },
                MaterialKinds: new[]
                {
                    "body",
                    "hair",
                    "accessory",
                    "face_sdf",
                    "face",
                    "eye",
                    "eyelight",
                    "eyelash",
                    "eyebrow",
                }
            ),
            PreserveOutsideStandardVrm: new[]
            {
                "Sekai C/S/H texture role separation",
                "FaceShadowTex/SDF UV1 semantics",
                "body/head stitch metadata before final VRM merge is stable",
                "PJSK morph hash bindings for face motion JSON",
                "plugin/viewer tuned preview lighting",
            },
            UnresolvedBeforeTrueParity: new[]
            {
                "browser equivalent of the runtime face SDF bone-basis driver",
                "springbone.json to VRM springBone mapping",
                "exact Unity shader controller animation tracks",
            },
            SekaiRuntimeMaterialProfile: runtimeProfile
        );
    }

    private HeadAssetManifest BuildHeadTemplate(
        ResolvedBundleInput head,
        string skeletonId,
        BundleInventory headInventory,
        BundleInventory bodyInventory,
        string? headRootOverride
    )
    {
        var rootName = ResolveHeadRootName(headInventory, headRootOverride);
        var attachOrigin = SelectPreferredHeadOriginNode(headInventory.OriginNodeCandidates);
        var bodyBones = new HashSet<string>(bodyInventory.BoneNames, StringComparer.OrdinalIgnoreCase);
        var boneRemap = headInventory.BoneNames
            .Where(bodyBones.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);
        var materialLookup = MaterialIdentityLookup.FromInventory(headInventory.Materials);
        var faceSlots = headInventory.SkinnedMeshes
            .Concat(headInventory.StaticMeshes)
            .Where(mesh => IsFaceLikeMesh(mesh, materialLookup))
            .SelectMany(mesh => mesh.MaterialSlots.Select(slot =>
            {
                var material = materialLookup.Require(slot);
                var hasFaceShadowTex = FindTextureSlot(material, "_FaceShadowTex") is not null;
                return new FaceMaterialSlot(
                    MeshName: mesh.MeshName,
                    SlotIndex: slot.SlotIndex,
                    MaterialFileId: slot.MaterialFileId,
                    MaterialPathId: slot.MaterialPathId,
                    MaterialKey: slot.MaterialKey,
                    MaterialName: slot.MaterialName,
                    MaterialKind: ClassifyHeadMaterialKind(slot.MaterialName ?? material.Name, hasFaceShadowTex),
                    MainTex: FindTextureSlot(material, "_MainTex"),
                    ShadowTex: FindTextureSlot(material, "_ShadowTex"),
                    ValueTex: FindTextureSlot(material, "_ValueTex"),
                    FaceShadowTex: FindTextureSlot(material, "_FaceShadowTex"),
                    Mode: hasFaceShadowTex ? "sdf" : "clean",
                    Lighting: SekaiMaterialMetadata.BuildLightingSettings(material),
                    RawMaterial: SekaiMaterialMetadata.BuildRawMaterialProperties(material)
                );
            }))
            .DistinctBy(
                slot => $"{slot.MeshName}::{slot.SlotIndex}::{slot.MaterialKey}",
                StringComparer.Ordinal
            )
            .ToList();

        var faceTintSource = faceSlots
            .Select(slot => materialLookup.Require(new RenderMaterialSlotInventory(
                SlotIndex: slot.SlotIndex,
                MaterialFileId: slot.MaterialFileId,
                MaterialPathId: slot.MaterialPathId,
                MaterialKey: slot.MaterialKey,
                MaterialName: slot.MaterialName
            )))
            .FirstOrDefault(HasSkinColorProperty);
        var skinColorDefault = FindColorProperty(faceTintSource, "_SkinColorDefault")
            ?? FindColorProperty(faceTintSource, "_DefaultSkinColor")
            ?? FindColorProperty(faceTintSource, "_Shadow1SkinColor")
            ?? "#fde2d9";
        var skinColor1 = FindColorProperty(faceTintSource, "_Shadow1SkinColor")
            ?? skinColorDefault;
        var skinColor2 = FindColorProperty(faceTintSource, "_Shadow2SkinColor")
            ?? skinColor1;

        return new HeadAssetManifest(
            Id: $"head-{head.CharacterId}-{head.BundleStem}",
            DisplayName: headInventory.Roots.FirstOrDefault()?.Name ?? $"Head {head.CharacterId} {head.BundleStem}",
            CharacterId: head.CharacterId,
            CharacterHeightMeters: ResolveCharacterHeightMeters(head.CharacterId),
            Source: new AssetSource(
                BundleRoot: head.ResolvedBundlePath,
                ManifestUrl: "head.manifest.json",
                MeshUrl: "character/unity-runtime.json",
                SkeletonUrl: null,
                AnimationUrls: Array.Empty<string>()
            ),
            RawImportOffset: new Vec3(0f, 0f, 0f),
            Assembly: new HeadAssemblyMetadata(
                ExpectedSkeletonId: skeletonId,
                RootNodeName: rootName,
                AttachOrigin: new AssemblyAnchor(
                    NodeName: attachOrigin,
                    FallbackPosition: new Vec3(0f, 0.08f, 0.02f)
                ),
                BoneRemap: boneRemap
            ),
            DefaultFaceMode: faceSlots.Any(slot => slot.Mode == "sdf") ? "sdf" : "clean",
            FaceMaterials: faceSlots,
            MorphChannels: Array.Empty<string>(),
            MorphChannelBindings: Array.Empty<HeadMorphChannel>(),
            Proxy: new HeadProxySettings(
                FaceColor: skinColorDefault,
                FaceShadeColor: skinColor1,
                SkinColorDefault: skinColorDefault,
                SkinColor1: skinColor1,
                SkinColor2: skinColor2,
                HairColor: "#7b5b4a",
                HairShadowColor: "#513d33",
                HeadRadius: 0.74f,
                FaceDepth: 0.82f,
                HairArc: 0.98f
            )
        );
    }

    private static string ResolveHeadRootName(BundleInventory inventory, string? headRootOverride)
    {
        if (!string.IsNullOrWhiteSpace(headRootOverride))
        {
            var match = inventory.Roots.FirstOrDefault(root =>
                string.Equals(root.Name, headRootOverride, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"Head root '{headRootOverride}' was not found. Available roots: {string.Join(", ", inventory.Roots.Select(root => root.Name))}"
                );
            }
            return match.Name;
        }

        return inventory.Roots.FirstOrDefault(root =>
                root.Name.StartsWith("mdl_chr_", StringComparison.OrdinalIgnoreCase))
            ?.Name
            ?? inventory.Roots.FirstOrDefault(root =>
                string.Equals(root.Name, "face", StringComparison.OrdinalIgnoreCase))
            ?.Name
            ?? inventory.Roots.FirstOrDefault()?.Name
            ?? "HeadRoot";
    }

    private float ResolveCharacterHeightMeters(string characterId)
    {
        return characterHeightMetersById.TryGetValue(characterId.PadLeft(2, '0'), out var height)
            ? height
            : 1.00f;
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


    private static string? FindTextureSlot(MaterialInventory? material, string slotName)
    {
        return material?.TextureSlots
            .FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase))
            ?.TextureName;
    }

    private static string? FindColorProperty(MaterialInventory? material, string propertyName)
    {
        var color = material?.ColorProperties
            .FirstOrDefault(entry => string.Equals(entry.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return color is null ? null : ToHex(color.R, color.G, color.B);
    }

    private static bool HasSkinColorProperty(MaterialInventory material)
    {
        return material.ColorProperties.Any(entry =>
            string.Equals(entry.Name, "_DefaultSkinColor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "_SkinColorDefault", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "_Shadow1SkinColor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "_Shadow2SkinColor", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToHex(float r, float g, float b)
    {
        static int ClampByte(float value) => Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        return $"#{ClampByte(r):X2}{ClampByte(g):X2}{ClampByte(b):X2}".ToLowerInvariant();
    }

    private static bool IsFaceLikeMesh(
        RenderMeshInventory mesh,
        MaterialIdentityLookup materialLookup
    )
    {
        var meshName = mesh.MeshName.ToLowerInvariant();
        if (meshName.Contains("face") || meshName.Contains("eye"))
        {
            return true;
        }

        foreach (var slot in mesh.MaterialSlots)
        {
            var material = materialLookup.Require(slot);

            if (material.TextureSlots.Any(textureSlot =>
                    string.Equals(textureSlot.SlotName, "_FaceShadowTex", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string SelectPreferredBodyAttachNode(IReadOnlyList<string> candidates)
    {
        return candidates.FirstOrDefault(name => string.Equals(name, "Neck", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(name => string.Equals(name, "Head", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault()
            ?? "Neck";
    }

    private static string SelectPreferredHeadOriginNode(IReadOnlyList<string> candidates)
    {
        return candidates.FirstOrDefault(name => string.Equals(name, "NeckSocket", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(name => string.Equals(name, "Neck", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(name => string.Equals(name, "Head", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault()
            ?? "Neck";
    }

    private static string ClassifyBodyMaterialKind(string materialName)
    {
        var name = materialName.ToLowerInvariant();
        if (name.Contains("_acc_"))
        {
            return "accessory";
        }
        if (name.Contains("_hair_"))
        {
            return "hair";
        }
        return "body";
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
        if (name.Contains("_ehl_"))
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
}
