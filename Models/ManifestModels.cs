namespace PjskBundle2Parts.Models;

public sealed record Vec3(float X, float Y, float Z);

public sealed record AssetSource(
    string BundleRoot,
    string ManifestUrl,
    string MeshUrl,
    string? SkeletonUrl,
    IReadOnlyList<string> AnimationUrls
);

public sealed record AssemblyAnchor(
    string? NodeName,
    Vec3 FallbackPosition
);

public sealed record MaterialLightingSettings(
    float SpecularPower,
    float RimThreshold,
    float ShadowTexWeight,
    int FadeMode,
    float HueSinAngle,
    float HueCosAngle,
    float Saturation,
    float Value,
    float Contrast,
    string PartsAmbientColor,
    string ReflectionBlendColor,
    float OutlineWidth,
    float OutlineOffset,
    float OutlineLightness,
    float ShadowWidth,
    float UseOutlineSecondNormal,
    float DistortionFps,
    float DistortionIntensity,
    float DistortionIntensityX,
    float DistortionIntensityY,
    float DistortionOffsetX,
    float DistortionOffsetY,
    float DistortionScrollSpeed,
    float DistortionScrollX,
    float DistortionScrollY,
    float DistortionTexTilingX,
    float DistortionTexTilingY,
    float Threshold,
    float LightInfluence,
    float LightInfluenceForEyeHighlight,
    float? SekaiShadowThreshold = null,
    bool? UseLambert = null,
    bool? UseValueTex = null,
    bool? UseFaceSdf = null,
    bool? UseSkinColor = null,
    int? SkinMaskMode = null,
    float? FaceSdfMirror = null,
    float? FaceSdfBias = null,
    bool? UseFaceShadowLimiter = null,
    float? RangeLimit = null,
    float? FaceSkinShadowStrength = null,
    float? FaceSphereShadowEdge = null,
    float? FaceSphereShadowSmoothness = null,
    float? FaceSphereShadowWeight = null,
    bool? HairShadow = null
);

public sealed record BodyMaterialSlot(
    string MeshName,
    int SlotIndex,
    long MaterialFileId,
    long MaterialPathId,
    string MaterialKey,
    string? MaterialName,
    string MaterialKind,
    string? MainTex,
    string? ShadowTex,
    string? ValueTex,
    MaterialLightingSettings Lighting
);

public sealed record FaceMaterialSlot(
    string MeshName,
    int SlotIndex,
    long MaterialFileId,
    long MaterialPathId,
    string MaterialKey,
    string? MaterialName,
    string MaterialKind,
    string? MainTex,
    string? ShadowTex,
    string? ValueTex,
    string? FaceShadowTex,
    string Mode,
    MaterialLightingSettings Lighting
);

public sealed record BodySkeletonMetadata(
    string SkeletonId,
    string? RootNodeName,
    AssemblyAnchor NeckAttach
);

public sealed record HeadAssemblyMetadata(
    string ExpectedSkeletonId,
    AssemblyAnchor AttachOrigin,
    string? RootNodeName,
    IReadOnlyDictionary<string, string> BoneRemap
);

public sealed record BodyProxySettings(
    string BodyColor,
    string ShadowColor,
    float BodyScale,
    float TorsoLength,
    float ShoulderWidth
);

public sealed record HeadMorphChannel(
    string Name,
    string SourceName,
    uint NameHash,
    uint CurveHash
);

public sealed record HeadProxySettings(
    string FaceColor,
    string FaceShadeColor,
    string SkinColorDefault,
    string SkinColor1,
    string SkinColor2,
    string HairColor,
    string HairShadowColor,
    float HeadRadius,
    float FaceDepth,
    float HairArc
);

public sealed record BodyAssetManifest(
    string Id,
    string DisplayName,
    string CharacterId,
    float CharacterHeightMeters,
    AssetSource Source,
    Vec3 NeckAnchor,
    BodySkeletonMetadata Skeleton,
    IReadOnlyList<BodyMaterialSlot> BodyMaterials,
    BodyProxySettings Proxy
);

public sealed record HeadAssetManifest(
    string Id,
    string DisplayName,
    string CharacterId,
    float CharacterHeightMeters,
    AssetSource Source,
    Vec3 RawImportOffset,
    HeadAssemblyMetadata Assembly,
    string DefaultFaceMode,
    IReadOnlyList<FaceMaterialSlot> FaceMaterials,
    IReadOnlyList<string> MorphChannels,
    IReadOnlyList<HeadMorphChannel> MorphChannelBindings,
    HeadProxySettings Proxy
);
