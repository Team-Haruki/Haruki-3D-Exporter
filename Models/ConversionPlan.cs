namespace PjskBundle2Parts.Models;

public sealed record ConversionPlan(
    string PlanPath,
    string BodyInventoryPath,
    string HeadInventoryPath,
    string BodyManifestTemplatePath,
    string HeadManifestTemplatePath,
    string SekaiVrmProfilePath,
    string BodySpringBonePath,
    string HeadSpringBonePath,
    string CombinedSpringBonePath,
    string VrmSpringBoneCandidatePath,
    string VrmcSpringBoneExtensionPath,
    string VrmcSpringBoneResolveReportPath,
    string VrmcVrmExtensionPath,
    string VrmcVrmResolveReportPath,
    string PjskSekaiRuntimeExtensionPath,
    string PjskSekaiRuntimeResolveReportPath,
    string CharacterSpringBoneGlbPath,
    string CharacterPrefabRuntimeGlbPath,
    string CharacterUnityRuntimeJsonPath,
    string CharacterUnityRuntimeBinPath,
    string CharacterVrmCoreGlbPath,
    string CharacterVrmCandidateGlbPath,
    string CharacterVrmPath,
    ConversionPlanSummary Summary,
    BundleInventory BodyInventory,
    BundleInventory HeadInventory,
    BodyAssetManifest BodyManifestTemplate,
    HeadAssetManifest HeadManifestTemplate,
    SekaiVrmMigrationProfile SekaiVrmProfile
);

public sealed record ConversionPlanSummary(
    string CharacterId,
    string OutputDirectory,
    string BodyInputPath,
    string ResolvedBodyBundlePath,
    string HeadInputPath,
    string ResolvedHeadBundlePath,
    string SkeletonId,
    int BodyRootCount,
    int HeadRootCount,
    int BodySkinnedMeshCount,
    int HeadSkinnedMeshCount,
    IReadOnlyList<string> Notes
);

public sealed record PreviewLightProfile(
    float X,
    float Y,
    float Z,
    float Intensity,
    float Ambient,
    float ShadowThreshold,
    float ShadowWeight,
    float CharacterAmbient,
    float RimIntensity,
    float RimThreshold,
    float RimDirectionality,
    float FaceSoftness,
    float FaceSdfUseLightDirection,
    float CharacterHeight
);

public sealed record PluginPreviewProfile(
    Vec3 DirectionalLocation,
    float DirectionalEnergy,
    float AmbientIntensity,
    float ShadowThreshold,
    float ShadowWeight,
    float CharacterAmbientIntensity,
    float RimIntensity,
    float RimThreshold,
    float RimDirectionality,
    Vec3 RimRotationDegrees
);

public sealed record SekaiRuntimeMaterialProfile(
    int Version,
    string BodyPipeline,
    string FacePipeline,
    string LayerPipeline,
    string VrmStrategy,
    IReadOnlyDictionary<string, string> TextureRoles,
    PluginPreviewProfile PluginPreview,
    PreviewLightProfile ViewerTunedPreview
);

public sealed record VrmTargetContainerProfile(
    string Preferred,
    string Fallback,
    string CurrentPhase
);

public sealed record VrmMaterialFallbackProfile(
    string Shader,
    string MainTex,
    string ShadowTex,
    string ValueTex,
    string FaceShadowTex
);

public sealed record StandardVrmFallbackProfile(
    IReadOnlyDictionary<string, string> HumanoidBoneMap,
    VrmMaterialFallbackProfile MaterialFallback,
    string ExpressionSource,
    string SpringBoneSource
);

public sealed record SekaiRuntimeExtrasProfile(
    string ExtensionName,
    bool RequiredForExactViewerRender,
    IReadOnlyList<string> PayloadKeys,
    IReadOnlyList<string> MaterialKinds
);

public sealed record SekaiVrmMigrationProfile(
    int Version,
    VrmTargetContainerProfile TargetContainer,
    StandardVrmFallbackProfile StandardVrmFallback,
    SekaiRuntimeExtrasProfile SekaiRuntimeExtras,
    IReadOnlyList<string> PreserveOutsideStandardVrm,
    IReadOnlyList<string> UnresolvedBeforeTrueParity,
    SekaiRuntimeMaterialProfile SekaiRuntimeMaterialProfile
);
