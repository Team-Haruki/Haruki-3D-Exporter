using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record PartRuntimePackage(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("part")] PartRuntimeIdentity Part,
    [property: JsonPropertyName("source")] PartRuntimeSource Source,
    [property: JsonPropertyName("mount")] PartRuntimeMount Mount,
    [property: JsonPropertyName("manifest")] object Manifest,
    [property: JsonPropertyName("nativeMeshes")] PjskUnityRuntimeNativeMeshSet NativeMeshes,
    [property: JsonPropertyName("materialSlots")] IReadOnlyList<PjskSekaiRuntimeMaterialSlot> MaterialSlots,
    [property: JsonPropertyName("textureRoles")] IReadOnlyList<PjskSekaiRuntimeTextureRole> TextureRoles,
    [property: JsonPropertyName("characterTextures")] IReadOnlyDictionary<string, string> CharacterTextures,
    [property: JsonPropertyName("characterControllers")] PjskSekaiRuntimeCharacterControllers CharacterControllers,
    [property: JsonPropertyName("springBone")] PartRuntimeSpringBone SpringBone,
    [property: JsonPropertyName("morphChannelBindings")] IReadOnlyList<HeadMorphChannel> MorphChannelBindings,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PartRuntimeCorePackage(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("nativeMeshes")] PjskUnityRuntimeNativeMeshSet NativeMeshes,
    [property: JsonPropertyName("springBone")] PartRuntimeSpringBone SpringBone,
    [property: JsonPropertyName("characterControllers")] PjskSekaiRuntimeCharacterControllers CharacterControllers,
    [property: JsonPropertyName("morphChannelBindings")] IReadOnlyList<HeadMorphChannel> MorphChannelBindings,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PartRuntimeDeltaPackage(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("corePath")] string CorePath,
    [property: JsonPropertyName("part")] PartRuntimeIdentity Part,
    [property: JsonPropertyName("source")] PartRuntimeSource Source,
    [property: JsonPropertyName("mount")] PartRuntimeMount Mount,
    [property: JsonPropertyName("manifest")] object Manifest,
    [property: JsonPropertyName("materialSlots")] IReadOnlyList<PjskSekaiRuntimeMaterialSlot> MaterialSlots,
    [property: JsonPropertyName("textureRoles")] IReadOnlyList<PjskSekaiRuntimeTextureRole> TextureRoles,
    [property: JsonPropertyName("characterTextures")] IReadOnlyDictionary<string, string> CharacterTextures,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PartRuntimeIdentity(
    [property: JsonPropertyName("costume3dId")] int Costume3dId,
    [property: JsonPropertyName("partType")] string PartType,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("colorId")] int ColorId,
    [property: JsonPropertyName("colorName")] string? ColorName,
    [property: JsonPropertyName("costume3dGroupId")] int Costume3dGroupId,
    [property: JsonPropertyName("modelAssetbundleName")] string? ModelAssetbundleName,
    [property: JsonPropertyName("headCostume3dAssetbundleType")] string? HeadCostume3dAssetbundleType
);

public sealed record PartRuntimeSource(
    [property: JsonPropertyName("bundlePath")] string BundlePath,
    [property: JsonPropertyName("colorVariationBundlePath")] string? ColorVariationBundlePath,
    [property: JsonPropertyName("assetRootRelativeBundlePath")] string? AssetRootRelativeBundlePath
);

public sealed record PartRuntimeMount(
    [property: JsonPropertyName("mountKind")] string MountKind,
    [property: JsonPropertyName("rootNodeName")] string? RootNodeName,
    [property: JsonPropertyName("attachNode")] string? AttachNode,
    [property: JsonPropertyName("expectedSkeletonId")] string? ExpectedSkeletonId,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("accessoryTransformAdjustments")] IReadOnlyDictionary<string, PartRuntimeAccessoryTransformAdjustment>? AccessoryTransformAdjustments = null
);

public sealed record PartRuntimeAccessoryTransformAdjustment(
    [property: JsonPropertyName("position")] SpringVector3 Position,
    [property: JsonPropertyName("rotationEulerDegrees")] SpringVector3 RotationEulerDegrees,
    [property: JsonPropertyName("scale")] SpringVector3 Scale
);

public sealed record PartRuntimeSpringBone(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("prefabGraph")] SpringPrefabGraph PrefabGraph,
    [property: JsonPropertyName("managers")] IReadOnlyList<PjskSpringBoneRuntimeManager> Managers,
    [property: JsonPropertyName("bones")] IReadOnlyList<PjskSpringBoneRuntimeBone> Bones,
    [property: JsonPropertyName("extraBones")] IReadOnlyList<SpringExtraBoneEntry> ExtraBones,
    [property: JsonPropertyName("colliders")] IReadOnlyList<PjskSpringBoneRuntimeCollider> Colliders,
    [property: JsonPropertyName("colliderBindings")] IReadOnlyList<PjskSpringBoneRuntimeColliderBinding> ColliderBindings,
    [property: JsonPropertyName("managerColliderCaches")] IReadOnlyList<PjskSpringBoneRuntimeManagerColliderCache> ManagerColliderCaches,
    [property: JsonPropertyName("activeRootProfile")] PjskSpringBoneActiveRootProfile ActiveRootProfile,
    [property: JsonPropertyName("funit")] SpringFUnitSummary FUnit,
    [property: JsonPropertyName("constraintSetup")] PjskUnityRuntimeConstraintSetup ConstraintSetup,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);
