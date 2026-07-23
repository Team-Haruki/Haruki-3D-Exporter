using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record PjskSekaiRuntimeBuildResult(
    PjskSekaiRuntimeExtension Extension,
    PjskSekaiRuntimeResolveReport Report
);

public sealed record PjskSekaiRuntimeExtension(
    [property: JsonPropertyName("specVersion")] string SpecVersion,
    [property: JsonPropertyName("profileVersion")] int ProfileVersion,
    [property: JsonPropertyName("character")] PjskSekaiRuntimeCharacter Character,
    [property: JsonPropertyName("container")] PjskSekaiRuntimeContainer Container,
    [property: JsonPropertyName("bodyHeadAssembly")] PjskSekaiRuntimeAssembly BodyHeadAssembly,
    [property: JsonPropertyName("sekaiRuntimeMaterialProfile")] SekaiRuntimeMaterialProfile SekaiRuntimeMaterialProfile,
    [property: JsonPropertyName("bodyManifest")] BodyAssetManifest BodyManifest,
    [property: JsonPropertyName("headManifest")] HeadAssetManifest HeadManifest,
    [property: JsonPropertyName("materialSlots")] PjskSekaiRuntimeMaterialSlots MaterialSlots,
    [property: JsonPropertyName("textureRoles")] IReadOnlyList<PjskSekaiRuntimeTextureRole> TextureRoles,
    [property: JsonPropertyName("characterTextures")] IReadOnlyDictionary<string, string> CharacterTextures,
    [property: JsonPropertyName("morphChannelBindings")] IReadOnlyList<HeadMorphChannel> MorphChannelBindings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("motionPackage")] PjskSekaiRuntimeMotionPackage? MotionPackage,
    [property: JsonPropertyName("characterControllers")] PjskSekaiRuntimeCharacterControllers CharacterControllers,
    [property: JsonPropertyName("pjskSpringBone")] PjskSekaiRuntimeSpringBonePayload PjskSpringBone,
    [property: JsonPropertyName("springBoneSourceMetadata")] PjskSekaiRuntimeSpringBoneSourceMetadata SpringBoneSourceMetadata,
    [property: JsonPropertyName("viewerHints")] PjskSekaiRuntimeViewerHints ViewerHints,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes
);

public sealed record PjskUnityRuntimePackage(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("unityVersion")] string UnityVersion,
    [property: JsonPropertyName("coordinateSpace")] PjskUnityRuntimeCoordinateSpace CoordinateSpace,
    [property: JsonPropertyName("assemblyDiagnostics")] PjskUnityRuntimeAssemblyDiagnostics AssemblyDiagnostics,
    [property: JsonPropertyName("character")] PjskSekaiRuntimeCharacter Character,
    [property: JsonPropertyName("container")] PjskSekaiRuntimeContainer Container,
    [property: JsonPropertyName("bodyManifest")] BodyAssetManifest BodyManifest,
    [property: JsonPropertyName("headManifest")] HeadAssetManifest HeadManifest,
    [property: JsonPropertyName("materialSlots")] PjskSekaiRuntimeMaterialSlots MaterialSlots,
    [property: JsonPropertyName("textureRoles")] IReadOnlyList<PjskSekaiRuntimeTextureRole> TextureRoles,
    [property: JsonPropertyName("characterTextures")] IReadOnlyDictionary<string, string> CharacterTextures,
    [property: JsonPropertyName("morphChannelBindings")] IReadOnlyList<HeadMorphChannel> MorphChannelBindings,
    [property: JsonPropertyName("nativeMeshes")] PjskUnityRuntimeNativeMeshSet NativeMeshes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("motionPackage")] PjskSekaiRuntimeMotionPackage? MotionPackage,
    [property: JsonPropertyName("characterControllers")] PjskSekaiRuntimeCharacterControllers CharacterControllers,
    [property: JsonPropertyName("pjskSpringBone")] PjskSekaiRuntimeSpringBonePayload PjskSpringBone,
    [property: JsonPropertyName("runtimeUnitySetup")] PjskSpringBoneRuntimeUnitySetup RuntimeUnitySetup,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes
);

public sealed record PjskUnityRuntimeAssemblyDiagnostics(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("bodyRootPath")] string? BodyRootPath,
    [property: JsonPropertyName("headRootPath")] string? HeadRootPath,
    [property: JsonPropertyName("bodyAttachPath")] string? BodyAttachPath,
    [property: JsonPropertyName("headOriginPath")] string? HeadOriginPath,
    [property: JsonPropertyName("coordinateSpaceSource")] string CoordinateSpaceSource,
    [property: JsonPropertyName("coordinateSpaceViewer")] string CoordinateSpaceViewer,
    [property: JsonPropertyName("keyPathResolutions")] IReadOnlyList<PjskUnityRuntimeKeyPathResolution> KeyPathResolutions,
    [property: JsonPropertyName("rendererDiagnostics")] IReadOnlyList<PjskUnityRuntimeRendererDiagnostic> RendererDiagnostics,
    [property: JsonPropertyName("motionTargetCoverage")] PjskUnityRuntimeMotionTargetCoverage MotionTargetCoverage,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PjskUnityRuntimeKeyPathResolution(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("expectedPath")] string? ExpectedPath,
    [property: JsonPropertyName("resolved")] bool Resolved,
    [property: JsonPropertyName("pathId")] long? PathId,
    [property: JsonPropertyName("nodeName")] string? NodeName,
    [property: JsonPropertyName("resolvedPath")] string? ResolvedPath
);

public sealed record PjskUnityRuntimeRendererDiagnostic(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("rendererPathId")] long PathId,
    [property: JsonPropertyName("rendererName")] string? Name,
    [property: JsonPropertyName("transformPath")] string? TransformPath,
    [property: JsonPropertyName("meshName")] string? MeshName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("rootBonePathId")] long? RootBonePathId,
    [property: JsonPropertyName("rootBonePath")] string? RootBonePath,
    [property: JsonPropertyName("skinnedBoneCount")] int SkinnedBoneCount,
    [property: JsonPropertyName("resolvedSkinnedBoneCount")] int ResolvedSkinnedBoneCount,
    [property: JsonPropertyName("sampleSkinnedBonePaths")] IReadOnlyList<string> SampleSkinnedBonePaths,
    [property: JsonPropertyName("missingSkinnedBonePathIds")] IReadOnlyList<long> MissingSkinnedBonePathIds
);

public sealed record PjskUnityRuntimeMotionTargetCoverage(
    [property: JsonPropertyName("bindingCount")] int BindingCount,
    [property: JsonPropertyName("resolvedBindingCount")] int ResolvedBindingCount,
    [property: JsonPropertyName("unresolvedBindingCount")] int UnresolvedBindingCount,
    [property: JsonPropertyName("totalTargetCount")] int TotalTargetCount,
    [property: JsonPropertyName("bodyTargetCount")] int BodyTargetCount,
    [property: JsonPropertyName("headTargetCount")] int HeadTargetCount,
    [property: JsonPropertyName("sampleUnresolvedBindings")] IReadOnlyList<string> SampleUnresolvedBindings
);

public sealed record PjskUnityRuntimeNativeMeshSet(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("coordinateSpace")] string CoordinateSpace,
    [property: JsonPropertyName("meshes")] IReadOnlyList<PjskUnityRuntimeNativeMesh> Meshes,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PjskUnityRuntimeNativeMesh(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("meshPath")] string MeshPath,
    [property: JsonPropertyName("meshName")] string MeshName,
    [property: JsonPropertyName("rendererPathId")] long RendererPathId,
    [property: JsonPropertyName("rendererTransformPath")] string RendererTransformPath,
    [property: JsonPropertyName("rootBonePath")] string? RootBonePath,
    [property: JsonPropertyName("bonePaths")] IReadOnlyList<string> BonePaths,
    [property: JsonPropertyName("boneInverseBindMatrices")] IReadOnlyList<float> BoneInverseBindMatrices,
    [property: JsonPropertyName("submeshes")] IReadOnlyList<PjskUnityRuntimeNativeSubmesh> Submeshes,
    [property: JsonPropertyName("positions")] IReadOnlyList<float> Positions,
    [property: JsonPropertyName("normals")] IReadOnlyList<float> Normals,
    [property: JsonPropertyName("tangents")] IReadOnlyList<float> Tangents,
    [property: JsonPropertyName("uv0")] IReadOnlyList<float> Uv0,
    [property: JsonPropertyName("uv1")] IReadOnlyList<float> Uv1,
    [property: JsonPropertyName("uv2")] IReadOnlyList<float> Uv2,
    [property: JsonPropertyName("colors")] IReadOnlyList<float> Colors,
    [property: JsonPropertyName("skinIndices")] IReadOnlyList<ushort> SkinIndices,
    [property: JsonPropertyName("skinWeights")] IReadOnlyList<float> SkinWeights,
    [property: JsonPropertyName("morphTargets")] IReadOnlyList<PjskUnityRuntimeNativeMorphTarget> MorphTargets
);

public sealed record PjskUnityRuntimeNativeSubmesh(
    [property: JsonPropertyName("slotIndex")] int SlotIndex,
    [property: JsonPropertyName("materialKey")] string MaterialKey,
    [property: JsonPropertyName("materialFileId")] long MaterialFileId,
    [property: JsonPropertyName("materialPathId")] long MaterialPathId,
    [property: JsonPropertyName("materialName")] string MaterialName,
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("indices")] IReadOnlyList<int> Indices
);

public sealed record PjskUnityRuntimeNativeMorphTarget(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("indices")] IReadOnlyList<int> Indices,
    [property: JsonPropertyName("positionDeltas")] IReadOnlyList<float> PositionDeltas,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("normalDeltas")] IReadOnlyList<float>? NormalDeltas
);

public sealed record PjskUnityRuntimeCoordinateSpace(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("viewer")] string Viewer,
    [property: JsonPropertyName("positionConversion")] string PositionConversion,
    [property: JsonPropertyName("rotationConversion")] string RotationConversion,
    [property: JsonPropertyName("scaleConversion")] string ScaleConversion,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes
);

public sealed record PjskSekaiRuntimeCharacter(
    [property: JsonPropertyName("characterId")] string CharacterId,
    [property: JsonPropertyName("skeletonId")] string SkeletonId,
    [property: JsonPropertyName("characterHeightMeters")] float CharacterHeightMeters,
    [property: JsonPropertyName("bodyBundlePath")] string BodyBundlePath,
    [property: JsonPropertyName("headBundlePath")] string HeadBundlePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("costume")] PjskSekaiRuntimeCostumeMetadata? Costume
);

public sealed record PjskSekaiRuntimeCostumeMetadata(
    [property: JsonPropertyName("character3dId")] int Character3dId,
    [property: JsonPropertyName("characterName")] string CharacterName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("bodyCostume3dId")] int BodyCostume3dId,
    [property: JsonPropertyName("bodyAssetbundleName")] string BodyAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("bodyColorAssetbundleName")] string? BodyColorAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("bodyColorVariationPath")] string? BodyColorVariationPath,
    [property: JsonPropertyName("hairCostume3dId")] int HairCostume3dId,
    [property: JsonPropertyName("hairAssetbundleName")] string HairAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("hairColorAssetbundleName")] string? HairColorAssetbundleName,
    [property: JsonPropertyName("hairBundleKind")] string HairBundleKind,
    [property: JsonPropertyName("hairVariantGroupKey")] string HairVariantGroupKey,
    [property: JsonPropertyName("headCostume3dId")] int HeadCostume3dId,
    [property: JsonPropertyName("headAssetbundleName")] string HeadAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("headColorAssetbundleName")] string? HeadColorAssetbundleName,
    [property: JsonPropertyName("headBundleKind")] string HeadBundleKind,
    [property: JsonPropertyName("headVariantGroupKey")] string HeadVariantGroupKey,
    [property: JsonPropertyName("headCompositionKind")] string HeadCompositionKind,
    [property: JsonPropertyName("mainHeadAssetbundleName")] string MainHeadAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("mainHeadColorVariationPath")] string? MainHeadColorVariationPath,
    [property: JsonPropertyName("mainHeadMode")] string MainHeadMode,
    [property: JsonPropertyName("mainHeadCostumeType")] string MainHeadCostumeType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("headTextureFallbackAssetbundleName")] string? HeadTextureFallbackAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("accessoryHeadAssetbundleName")] string? AccessoryHeadAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("accessoryAttachNode")] string? AccessoryAttachNode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("accessoryColorAssetbundleName")] string? AccessoryColorAssetbundleName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("accessoryColorVariationPath")] string? AccessoryColorVariationPath,
    [property: JsonPropertyName("assetBundleNames")] IReadOnlyList<string> AssetBundleNames,
    [property: JsonPropertyName("assetBundlePaths")] IReadOnlyList<string> AssetBundlePaths
);

public sealed record PjskSekaiRuntimeContainer(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("sourceGlb")] string? SourceGlb,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("prefabRuntimeGlb")] string? PrefabRuntimeGlb,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("unityRuntimeJson")] string? UnityRuntimeJson,
    [property: JsonPropertyName("preferredContainer")] string PreferredContainer,
    [property: JsonPropertyName("fallbackContainer")] string FallbackContainer,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("requiresCustomViewerForExactRender")] bool RequiresCustomViewerForExactRender
);

public sealed record PjskSekaiRuntimeAssembly(
    [property: JsonPropertyName("bodyRootNodeName")] string? BodyRootNodeName,
    [property: JsonPropertyName("headRootNodeName")] string? HeadRootNodeName,
    [property: JsonPropertyName("bodyNeckAttach")] AssemblyAnchor BodyNeckAttach,
    [property: JsonPropertyName("headAttachOrigin")] AssemblyAnchor HeadAttachOrigin,
    [property: JsonPropertyName("boneRemap")] IReadOnlyDictionary<string, string> BoneRemap,
    [property: JsonPropertyName("stitchMode")] string StitchMode
);

public sealed record PjskSekaiRuntimeMaterialSlots(
    [property: JsonPropertyName("body")] IReadOnlyList<PjskSekaiRuntimeMaterialSlot> Body,
    [property: JsonPropertyName("head")] IReadOnlyList<PjskSekaiRuntimeMaterialSlot> Head,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("accessory")] IReadOnlyList<PjskSekaiRuntimeMaterialSlot>? Accessory = null
);

public sealed record PjskSekaiRuntimeMaterialSlot(
    [property: JsonPropertyName("part")] string Part,
    [property: JsonPropertyName("meshName")] string MeshName,
    [property: JsonPropertyName("slotIndex")] int SlotIndex,
    [property: JsonPropertyName("materialKey")] string MaterialKey,
    [property: JsonPropertyName("materialFileId")] long MaterialFileId,
    [property: JsonPropertyName("materialPathId")] long MaterialPathId,
    [property: JsonPropertyName("materialName")] string? MaterialName,
    [property: JsonPropertyName("materialKind")] string MaterialKind,
    [property: JsonPropertyName("mainTex")] string? MainTex,
    [property: JsonPropertyName("shadowTex")] string? ShadowTex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("valueTex")] string? ValueTex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("faceShadowTex")] string? FaceShadowTex,
    [property: JsonPropertyName("renderOrder")] int RenderOrder,
    [property: JsonPropertyName("shaderPipeline")] string ShaderPipeline,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [property: JsonPropertyName("isAccessory")] bool IsAccessory,
    [property: JsonPropertyName("lighting")] MaterialLightingSettings Lighting,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("rawMaterial")] RawMaterialProperties? RawMaterial = null
);

public sealed record PjskSekaiRuntimeTextureRole(
    [property: JsonPropertyName("part")] string Part,
    [property: JsonPropertyName("materialKey")] string MaterialKey,
    [property: JsonPropertyName("materialFileId")] long MaterialFileId,
    [property: JsonPropertyName("materialPathId")] long MaterialPathId,
    [property: JsonPropertyName("materialName")] string? MaterialName,
    [property: JsonPropertyName("materialKind")] string MaterialKind,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("uri")] string Uri
);

public sealed record PjskSekaiRuntimeMotionPackage(
    [property: JsonPropertyName("sourcePath")] string SourcePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("bodyMotionGlb")] string? BodyMotionGlb,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("unityMotionJson")] string? UnityMotionJson,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("bodyMotionBindings")] PjskBodyMotionBindingSet? BodyMotionBindings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("faceMotion")] PjskFaceMotionSet? FaceMotion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("lightMotion")] PjskLightMotionSet? LightMotion
);

public sealed record PjskSekaiRuntimeSpringBonePayload(
    [property: JsonPropertyName("raw")] CombinedSpringBoneExport Raw,
    [property: JsonPropertyName("vrmCandidate")] VrmSpringBoneCandidate VrmCandidate,
    [property: JsonPropertyName("runtimeUnitySetup")] PjskSpringBoneRuntimeUnitySetup RuntimeUnitySetup
);

public sealed record PjskSpringBoneRuntimeUnitySetup(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("unityVersion")] string UnityVersion,
    [property: JsonPropertyName("coordinateSpace")] PjskUnityRuntimeCoordinateSpace CoordinateSpace,
    [property: JsonPropertyName("prefabGraphs")] IReadOnlyList<SpringPrefabGraph> PrefabGraphs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("bodyHeadAssembly")] PjskUnityRuntimeBodyHeadAssembly? BodyHeadAssembly,
    [property: JsonPropertyName("rootSelectionProfile")] PjskSpringBoneRootSelectionProfile RootSelectionProfile,
    [property: JsonPropertyName("setupPlan")] PjskSpringBoneSetupPlan SetupPlan,
    [property: JsonPropertyName("bindingDecisions")] IReadOnlyList<PjskSpringBoneBindingDecision> BindingDecisions,
    [property: JsonPropertyName("activeRootProfile")] PjskSpringBoneActiveRootProfile ActiveRootProfile,
    [property: JsonPropertyName("funit")] SpringFUnitSummary FUnit,
    [property: JsonPropertyName("constraintSetup")] PjskUnityRuntimeConstraintSetup ConstraintSetup,
    [property: JsonPropertyName("managerColliderCaches")] IReadOnlyList<PjskSpringBoneRuntimeManagerColliderCache> ManagerColliderCaches,
    [property: JsonPropertyName("managers")] IReadOnlyList<PjskSpringBoneRuntimeManager> Managers,
    [property: JsonPropertyName("bones")] IReadOnlyList<PjskSpringBoneRuntimeBone> Bones,
    [property: JsonPropertyName("colliders")] IReadOnlyList<PjskSpringBoneRuntimeCollider> Colliders,
    [property: JsonPropertyName("colliderBindings")] IReadOnlyList<PjskSpringBoneRuntimeColliderBinding> ColliderBindings,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PjskUnityRuntimeConstraintSetup(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("constraints")] IReadOnlyList<PjskUnityRuntimeConstraint> Constraints,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PjskUnityRuntimeConstraint(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("pathId")] long PathId,
    [property: JsonPropertyName("ownerPath")] string? OwnerPath,
    [property: JsonPropertyName("ownerName")] string? OwnerName,
    [property: JsonPropertyName("enabled")] bool? Enabled,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("weight")] float Weight,
    [property: JsonPropertyName("locked")] bool? Locked,
    [property: JsonPropertyName("translationAxis")] int? TranslationAxis,
    [property: JsonPropertyName("rotationAxis")] int? RotationAxis,
    [property: JsonPropertyName("translationAtRest")] SpringVector3? TranslationAtRest,
    [property: JsonPropertyName("rotationAtRest")] SpringVector3? RotationAtRest,
    [property: JsonPropertyName("translationOffset")] SpringVector3? TranslationOffset,
    [property: JsonPropertyName("rotationOffset")] SpringVector3? RotationOffset,
    [property: JsonPropertyName("aimVector")] SpringVector3? AimVector,
    [property: JsonPropertyName("upVector")] SpringVector3? UpVector,
    [property: JsonPropertyName("worldUpVector")] SpringVector3? WorldUpVector,
    [property: JsonPropertyName("worldUpType")] int? WorldUpType,
    [property: JsonPropertyName("worldUpObjectPathId")] long? WorldUpObjectPathId,
    [property: JsonPropertyName("worldUpObjectName")] string? WorldUpObjectName,
    [property: JsonPropertyName("worldUpObjectPath")] string? WorldUpObjectPath,
    [property: JsonPropertyName("sources")] IReadOnlyList<PjskUnityRuntimeConstraintSource> Sources,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string Reason
);

public sealed record PjskUnityRuntimeConstraintSource(
    [property: JsonPropertyName("sourcePathId")] long? SourcePathId,
    [property: JsonPropertyName("sourceName")] string? SourceName,
    [property: JsonPropertyName("sourcePath")] string? SourcePath,
    [property: JsonPropertyName("weight")] float Weight,
    [property: JsonPropertyName("translationOffset")] SpringVector3? TranslationOffset,
    [property: JsonPropertyName("rotationOffset")] SpringVector3? RotationOffset
);

public sealed record PjskUnityRuntimeBodyHeadAssembly(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("parentRootPath")] string? ParentRootPath,
    [property: JsonPropertyName("parentAttachPath")] string? ParentAttachPath,
    [property: JsonPropertyName("childRootPath")] string? ChildRootPath,
    [property: JsonPropertyName("childOriginPath")] string? ChildOriginPath,
    [property: JsonPropertyName("parentingMode")] string ParentingMode,
    [property: JsonPropertyName("coordinateSpace")] string CoordinateSpace,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("faceRendererName")] string? FaceRendererName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("combineNodeAName")] string? CombineNodeAName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("combineNodeBName")] string? CombineNodeBName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("childMoveSuffix")] string? ChildMoveSuffix = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("parentCombineNodeAPath")] string? ParentCombineNodeAPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("parentCombineNodeBPath")] string? ParentCombineNodeBPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("childCombineNodeAPath")] string? ChildCombineNodeAPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("childCombineNodeBPath")] string? ChildCombineNodeBPath = null
);

public sealed record PjskSpringBoneRootSelectionProfile(
    [property: JsonPropertyName("policy")] string Policy,
    [property: JsonPropertyName("defaultBodyRoot")] string DefaultBodyRoot,
    [property: JsonPropertyName("rootCandidates")] IReadOnlyList<PjskSpringBoneRootCandidate> RootCandidates
);

public sealed record PjskSpringBoneRootCandidate(
    [property: JsonPropertyName("root")] string Root,
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("staticActive")] bool? StaticActive,
    [property: JsonPropertyName("defaultPriority")] int DefaultPriority,
    [property: JsonPropertyName("managerPathIds")] IReadOnlyList<long> ManagerPathIds,
    [property: JsonPropertyName("bonePathIds")] IReadOnlyList<long> BonePathIds,
    [property: JsonPropertyName("colliderIndexes")] IReadOnlyList<int> ColliderIndexes,
    [property: JsonPropertyName("rendererPathIds")] IReadOnlyList<long> RendererPathIds,
    [property: JsonPropertyName("reason")] string Reason
);

public sealed record PjskSpringBoneSetupPlan(
    [property: JsonPropertyName("discoveryMode")] string DiscoveryMode,
    [property: JsonPropertyName("rootPolicy")] string RootPolicy,
    [property: JsonPropertyName("managerPathIds")] IReadOnlyList<long> ManagerPathIds,
    [property: JsonPropertyName("orderedSteps")] IReadOnlyList<string> OrderedSteps,
    [property: JsonPropertyName("directBindingCount")] int DirectBindingCount,
    [property: JsonPropertyName("colliderFlagBindingCount")] int ColliderFlagBindingCount
);

public sealed record PjskSpringBoneBindingDecision(
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("sourceSpringBonePathId")] long SourceSpringBonePathId,
    [property: JsonPropertyName("nodePath")] string? NodePath,
    [property: JsonPropertyName("poseRoot")] string? PoseRoot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("colliderFlag")] int? ColliderFlag,
    [property: JsonPropertyName("directColliderPathIds")] IReadOnlyList<long> DirectColliderPathIds,
    [property: JsonPropertyName("candidateRoots")] IReadOnlyDictionary<string, IReadOnlyList<int>> CandidateRoots,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("defaultRoot")] string? DefaultRoot,
    [property: JsonPropertyName("selectedColliderIndexes")] IReadOnlyList<int> SelectedColliderIndexes,
    [property: JsonPropertyName("reason")] string Reason
);

public sealed record PjskSpringBoneActiveRootProfile(
    [property: JsonPropertyName("defaultBodyRoot")] string DefaultBodyRoot,
    [property: JsonPropertyName("activeRoots")] IReadOnlyList<string> ActiveRoots,
    [property: JsonPropertyName("inactiveRoots")] IReadOnlyList<string> InactiveRoots
);

public sealed record PjskSpringBoneRuntimeManagerColliderCache(
    [property: JsonPropertyName("managerPathId")] long ManagerPathId,
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("sourcePoseRoot")] string? SourcePoseRoot,
    [property: JsonPropertyName("runtimeRoot")] string RuntimeRoot,
    [property: JsonPropertyName("managerNodeName")] string? ManagerNodeName,
    [property: JsonPropertyName("managerNodePath")] string? ManagerNodePath,
    [property: JsonPropertyName("springBonePathIds")] IReadOnlyList<long> SpringBonePathIds,
    [property: JsonPropertyName("sphereColliderIndexes")] IReadOnlyList<int> SphereColliderIndexes,
    [property: JsonPropertyName("capsuleColliderIndexes")] IReadOnlyList<int> CapsuleColliderIndexes,
    [property: JsonPropertyName("panelColliderIndexes")] IReadOnlyList<int> PanelColliderIndexes,
    [property: JsonPropertyName("reason")] string Reason
);

public sealed record PjskSpringBoneRuntimeManager(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("pathId")] long PathId,
    [property: JsonPropertyName("nodeName")] string? NodeName,
    [property: JsonPropertyName("nodePath")] string? NodePath,
    [property: JsonPropertyName("poseRoot")] string? PoseRoot,
    [property: JsonPropertyName("activeSelf")] bool? ActiveSelf,
    [property: JsonPropertyName("activeInHierarchy")] bool? ActiveInHierarchy,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("automaticUpdates")] bool AutomaticUpdates,
    [property: JsonPropertyName("enableLengthLimits")] bool EnableLengthLimits,
    [property: JsonPropertyName("enableAngleLimits")] bool EnableAngleLimits,
    [property: JsonPropertyName("enableCollision")] bool EnableCollision,
    [property: JsonPropertyName("collideWithGround")] bool CollideWithGround,
    [property: JsonPropertyName("groundHeight")] float GroundHeight,
    [property: JsonPropertyName("isSumOfForcesOnBone")] bool IsSumOfForcesOnBone,
    [property: JsonPropertyName("isPaused")] bool IsPaused,
    [property: JsonPropertyName("dynamicRatio")] float DynamicRatio,
    [property: JsonPropertyName("simulationFrameRate")] int SimulationFrameRate,
    [property: JsonPropertyName("slowMotionScale")] float SlowMotionScale,
    [property: JsonPropertyName("bounce")] float Bounce,
    [property: JsonPropertyName("friction")] float Friction,
    [property: JsonPropertyName("animatedBoneNames")] IReadOnlyList<string> AnimatedBoneNames,
    [property: JsonPropertyName("rawGravity")] SpringVector3? RawGravity,
    [property: JsonPropertyName("forceProviders")] IReadOnlyList<VrmSpringBoneForceProviderCandidate> ForceProviders,
    [property: JsonPropertyName("bonePathIds")] IReadOnlyList<long> BonePathIds
);

public sealed record PjskSpringBoneRuntimeBone(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("pathId")] long PathId,
    [property: JsonPropertyName("nodeName")] string? NodeName,
    [property: JsonPropertyName("nodePath")] string? NodePath,
    [property: JsonPropertyName("poseRoot")] string? PoseRoot,
    [property: JsonPropertyName("activeSelf")] bool? ActiveSelf,
    [property: JsonPropertyName("activeInHierarchy")] bool? ActiveInHierarchy,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("pivotNodePath")] string? PivotNodePath,
    [property: JsonPropertyName("pivotNodeName")] string? PivotNodeName,
    [property: JsonPropertyName("pivotSourcePathId")] long? PivotSourcePathId,
    [property: JsonPropertyName("hitRadius")] float HitRadius,
    [property: JsonPropertyName("stiffness")] float Stiffness,
    [property: JsonPropertyName("dragForce")] float DragForce,
    [property: JsonPropertyName("gravityPower")] float GravityPower,
    [property: JsonPropertyName("gravityDir")] float[] GravityDir,
    [property: JsonPropertyName("rawStiffnessForce")] float? RawStiffnessForce,
    [property: JsonPropertyName("rawDragForce")] float? RawDragForce,
    [property: JsonPropertyName("rawSpringForce")] SpringVector3? RawSpringForce,
    [property: JsonPropertyName("rawWindInfluence")] float? RawWindInfluence,
    [property: JsonPropertyName("rawAngularStiffness")] float? RawAngularStiffness,
    [property: JsonPropertyName("rawSpringConstant")] float? RawSpringConstant,
    [property: JsonPropertyName("lengthLimitTargets")] IReadOnlyList<VrmSpringBoneLengthLimitTargetCandidate> LengthLimitTargets,
    [property: JsonPropertyName("rawAngleLimits")] VrmSpringBoneAngleLimitsCandidate RawAngleLimits,
    [property: JsonPropertyName("directColliderPathIds")] IReadOnlyList<long> DirectColliderPathIds,
    [property: JsonPropertyName("colliderFlag")] int ColliderFlag
);

public sealed record PjskSpringBoneRuntimeCollider(
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("pathId")] long PathId,
    [property: JsonPropertyName("scriptName")] string ScriptName,
    [property: JsonPropertyName("nodeName")] string? NodeName,
    [property: JsonPropertyName("nodePath")] string? NodePath,
    [property: JsonPropertyName("poseRoot")] string? PoseRoot,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("linkedRenderer")] VrmSpringBoneObjectRefCandidate? LinkedRenderer,
    [property: JsonPropertyName("linkedRendererEnabled")] bool? LinkedRendererEnabled,
    [property: JsonPropertyName("shape")] VrmSpringBoneColliderShapeCandidate Shape
);

public sealed record PjskSpringBoneRuntimeColliderBinding(
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("partKind")] string PartKind,
    [property: JsonPropertyName("sourceSpringBonePathId")] long SourceSpringBonePathId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("colliderFlag")] int? ColliderFlag,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("matchedPrefixes")] IReadOnlyList<string>? MatchedPrefixes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("collidersByRoot")] IReadOnlyDictionary<string, IReadOnlyList<int>>? CollidersByRoot,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("defaultRoot")] string? DefaultRoot,
    [property: JsonPropertyName("sourceColliderPathIds")] IReadOnlyList<long> SourceColliderPathIds,
    [property: JsonPropertyName("colliders")] IReadOnlyList<int> Colliders
);

public sealed record PjskSekaiRuntimeCharacterControllers(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("hair")] SpringCharacterHairEntry? Hair,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("eye")] SpringCharacterEyeEntry? Eye
);

public sealed record PjskSekaiRuntimeSpringBoneSourceMetadata(
    [property: JsonPropertyName("rawSpringBoneJson")] string RawSpringBoneJson,
    [property: JsonPropertyName("vrmSpringBoneCandidateJson")] string VrmSpringBoneCandidateJson,
    [property: JsonPropertyName("vrmcSpringBoneExtensionJson")] string VrmcSpringBoneExtensionJson,
    [property: JsonPropertyName("embeddedExtension")] string EmbeddedExtension
);

public sealed record PjskSekaiRuntimeViewerHints(
    [property: JsonPropertyName("materialBindingMode")] string MaterialBindingMode,
    [property: JsonPropertyName("faceMode")] string FaceMode,
    [property: JsonPropertyName("bodyPipeline")] string BodyPipeline,
    [property: JsonPropertyName("facePipeline")] string FacePipeline,
    [property: JsonPropertyName("layerPipeline")] string LayerPipeline,
    [property: JsonPropertyName("preserveGltfMaterialsAsFallback")] bool PreserveGltfMaterialsAsFallback
);

public sealed record PjskSekaiRuntimeResolveReport(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("extensionName")] string ExtensionName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("sourceGlb")] string? SourceGlb,
    [property: JsonPropertyName("bodyMaterialSlotCount")] int BodyMaterialSlotCount,
    [property: JsonPropertyName("headMaterialSlotCount")] int HeadMaterialSlotCount,
    [property: JsonPropertyName("textureRoleCount")] int TextureRoleCount,
    [property: JsonPropertyName("characterTextureCount")] int CharacterTextureCount,
    [property: JsonPropertyName("morphChannelBindingCount")] int MorphChannelBindingCount,
    [property: JsonPropertyName("embeddedFaceMotionClipCount")] int EmbeddedFaceMotionClipCount,
    [property: JsonPropertyName("bodyMotionGlb")] string? BodyMotionGlb,
    [property: JsonPropertyName("missingTextureRoles")] IReadOnlyList<PjskSekaiRuntimeMissingTextureRole> MissingTextureRoles,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PjskSekaiRuntimeMissingTextureRole(
    [property: JsonPropertyName("part")] string Part,
    [property: JsonPropertyName("meshName")] string MeshName,
    [property: JsonPropertyName("slotIndex")] int SlotIndex,
    [property: JsonPropertyName("materialKey")] string MaterialKey,
    [property: JsonPropertyName("materialFileId")] long MaterialFileId,
    [property: JsonPropertyName("materialPathId")] long MaterialPathId,
    [property: JsonPropertyName("materialName")] string? MaterialName,
    [property: JsonPropertyName("materialKind")] string MaterialKind,
    [property: JsonPropertyName("role")] string Role
);
