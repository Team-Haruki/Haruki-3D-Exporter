using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record VrmcVrmBuildResult(
    VrmcVrmExtension Extension,
    VrmcVrmResolveReport Report
);

public sealed record VrmcVrmExtension(
    [property: JsonPropertyName("specVersion")] string SpecVersion,
    [property: JsonPropertyName("meta")] VrmcVrmMeta Meta,
    [property: JsonPropertyName("humanoid")] VrmcVrmHumanoid Humanoid,
    [property: JsonPropertyName("firstPerson")] VrmcVrmFirstPerson FirstPerson,
    [property: JsonPropertyName("lookAt")] VrmcVrmLookAt LookAt,
    [property: JsonPropertyName("expressions")] VrmcVrmExpressions Expressions
);

public sealed record VrmcVrmMeta(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("authors")] IReadOnlyList<string> Authors,
    [property: JsonPropertyName("copyrightInformation")] string CopyrightInformation,
    [property: JsonPropertyName("contactInformation")] string ContactInformation,
    [property: JsonPropertyName("references")] IReadOnlyList<string> References,
    [property: JsonPropertyName("thirdPartyLicenses")] string ThirdPartyLicenses,
    [property: JsonPropertyName("licenseUrl")] string LicenseUrl,
    [property: JsonPropertyName("avatarPermission")] string AvatarPermission,
    [property: JsonPropertyName("allowExcessivelyViolentUsage")] bool AllowExcessivelyViolentUsage,
    [property: JsonPropertyName("allowExcessivelySexualUsage")] bool AllowExcessivelySexualUsage,
    [property: JsonPropertyName("commercialUsage")] string CommercialUsage,
    [property: JsonPropertyName("allowPoliticalOrReligiousUsage")] bool AllowPoliticalOrReligiousUsage,
    [property: JsonPropertyName("allowAntisocialOrHateUsage")] bool AllowAntisocialOrHateUsage,
    [property: JsonPropertyName("creditNotation")] string CreditNotation,
    [property: JsonPropertyName("allowRedistribution")] bool AllowRedistribution,
    [property: JsonPropertyName("modification")] string Modification,
    [property: JsonPropertyName("otherLicenseUrl")] string OtherLicenseUrl
);

public sealed record VrmcVrmHumanoid(
    [property: JsonPropertyName("humanBones")] IReadOnlyDictionary<string, VrmcVrmHumanBone> HumanBones
);

public sealed record VrmcVrmHumanBone(
    [property: JsonPropertyName("node")] int Node
);

public sealed record VrmcVrmFirstPerson(
    [property: JsonPropertyName("meshAnnotations")] IReadOnlyList<VrmcVrmMeshAnnotation> MeshAnnotations
);

public sealed record VrmcVrmMeshAnnotation(
    [property: JsonPropertyName("node")] int Node,
    [property: JsonPropertyName("type")] string Type
);

public sealed record VrmcVrmLookAt(
    [property: JsonPropertyName("offsetFromHeadBone")] float[] OffsetFromHeadBone,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("rangeMapHorizontalInner")] VrmcVrmLookAtRangeMap RangeMapHorizontalInner,
    [property: JsonPropertyName("rangeMapHorizontalOuter")] VrmcVrmLookAtRangeMap RangeMapHorizontalOuter,
    [property: JsonPropertyName("rangeMapVerticalDown")] VrmcVrmLookAtRangeMap RangeMapVerticalDown,
    [property: JsonPropertyName("rangeMapVerticalUp")] VrmcVrmLookAtRangeMap RangeMapVerticalUp
);

public sealed record VrmcVrmLookAtRangeMap(
    [property: JsonPropertyName("inputMaxValue")] float InputMaxValue,
    [property: JsonPropertyName("outputScale")] float OutputScale
);

public sealed record VrmcVrmExpressions(
    [property: JsonPropertyName("preset")] IReadOnlyDictionary<string, VrmcVrmExpression> Preset,
    [property: JsonPropertyName("custom")] IReadOnlyDictionary<string, VrmcVrmExpression> Custom
);

public sealed record VrmcVrmExpression(
    [property: JsonPropertyName("morphTargetBinds")] IReadOnlyList<VrmcVrmMorphTargetBind> MorphTargetBinds,
    [property: JsonPropertyName("materialColorBinds")] IReadOnlyList<VrmcVrmMaterialColorBind> MaterialColorBinds,
    [property: JsonPropertyName("textureTransformBinds")] IReadOnlyList<VrmcVrmTextureTransformBind> TextureTransformBinds,
    [property: JsonPropertyName("isBinary")] bool IsBinary,
    [property: JsonPropertyName("overrideBlink")] string OverrideBlink,
    [property: JsonPropertyName("overrideLookAt")] string OverrideLookAt,
    [property: JsonPropertyName("overrideMouth")] string OverrideMouth
);

public sealed record VrmcVrmMorphTargetBind;

public sealed record VrmcVrmMaterialColorBind;

public sealed record VrmcVrmTextureTransformBind;

public sealed record VrmcVrmResolveReport(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("glbPath")] string GlbPath,
    [property: JsonPropertyName("nodeCount")] int NodeCount,
    [property: JsonPropertyName("candidateHumanBoneCount")] int CandidateHumanBoneCount,
    [property: JsonPropertyName("resolvedHumanBoneCount")] int ResolvedHumanBoneCount,
    [property: JsonPropertyName("requiredHumanBones")] IReadOnlyList<string> RequiredHumanBones,
    [property: JsonPropertyName("missingRequiredHumanBones")] IReadOnlyList<string> MissingRequiredHumanBones,
    [property: JsonPropertyName("resolvedHumanBones")] IReadOnlyList<VrmcVrmHumanBoneResolve> ResolvedHumanBones,
    [property: JsonPropertyName("skippedHumanBones")] IReadOnlyList<VrmcVrmSkippedHumanBone> SkippedHumanBones,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record VrmcVrmHumanBoneResolve(
    [property: JsonPropertyName("humanBone")] string HumanBone,
    [property: JsonPropertyName("sourceNodeName")] string SourceNodeName,
    [property: JsonPropertyName("resolvedNode")] int ResolvedNode,
    [property: JsonPropertyName("resolvedPath")] string ResolvedPath,
    [property: JsonPropertyName("matchMode")] string MatchMode
);

public sealed record VrmcVrmSkippedHumanBone(
    [property: JsonPropertyName("humanBone")] string HumanBone,
    [property: JsonPropertyName("sourceNodeName")] string SourceNodeName,
    [property: JsonPropertyName("reason")] string Reason
);
