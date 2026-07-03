using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record GameCharacterMaster(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("resourceId")] int ResourceId,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("height")] float Height,
    [property: JsonPropertyName("figure")] string Figure,
    [property: JsonPropertyName("breastSize")] string BreastSize,
    [property: JsonPropertyName("modelName")] string ModelName,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("supportUnitType")] string? SupportUnitType
);

public sealed record Character3dMaster(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("headCostume3dId")] int HeadCostume3dId,
    [property: JsonPropertyName("hairCostume3dId")] int HairCostume3dId,
    [property: JsonPropertyName("bodyCostume3dId")] int BodyCostume3dId
);

public sealed record Costume3dModelMaster(
    [property: JsonPropertyName("costume3dId")] int Costume3dId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("assetbundleName")] string? AssetbundleName,
    [property: JsonPropertyName("headCostume3dAssetbundleType")] string? HeadCostume3dAssetbundleType,
    [property: JsonPropertyName("colorAssetbundleName")] string? ColorAssetbundleName,
    [property: JsonPropertyName("part")] string? Part,
    [property: JsonPropertyName("thumbnailAssetbundleName")] string? ThumbnailAssetbundleName
);

public sealed record Costume3dMaster(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("costume3dGroupId")] int Costume3dGroupId,
    [property: JsonPropertyName("partType")] string PartType,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("colorId")] int ColorId,
    [property: JsonPropertyName("colorName")] string? ColorName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("costume3dType")] string? Costume3dType,
    [property: JsonPropertyName("costume3dRarity")] string? Costume3dRarity,
    [property: JsonPropertyName("assetbundleName")] string? AssetbundleName,
    [property: JsonPropertyName("howToObtain")] string? HowToObtain
);

public sealed record CardMaster(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("cardRarityType")] string? CardRarityType,
    [property: JsonPropertyName("prefix")] string? Prefix,
    [property: JsonPropertyName("assetbundleName")] string? AssetbundleName,
    [property: JsonPropertyName("releaseAt")] long? ReleaseAt
);

public sealed record CardCostume3dMaster(
    [property: JsonPropertyName("cardId")] int CardId,
    [property: JsonPropertyName("costume3dId")] int Costume3dId,
    [property: JsonPropertyName("isInitialObtainHair")] bool IsInitialObtainHair
);

public sealed record Costume3dModelPatternMaster(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("headCostume3dId")] int HeadCostume3dId,
    [property: JsonPropertyName("hairCostume3dId")] int HairCostume3dId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("isDefault")] bool? IsDefault
);

public sealed record ResolvedCharacter3dCostume(
    int Character3dId,
    int CharacterId,
    string CharacterName,
    string? Unit,
    string BodyPath,
    string? BodyColorVariationPath,
    string HairPath,
    string MainHeadPath,
    string MainHeadAssetbundleName,
    string? MainHeadColorVariationPath,
    string MainHeadMode,
    string MainHeadCostumeType,
    string? HeadTextureFallbackPath,
    string? HeadTextureFallbackAssetbundleName,
    string HairBundleKind,
    string HairVariantGroupKey,
    string HeadBundleKind,
    string HeadVariantGroupKey,
    string HeadCompositionKind,
    string? AccessoryHeadPath,
    string? AccessoryHeadAssetbundleName,
    string? AccessoryHeadCostumeType,
    string? AccessoryAttachNode,
    string? AccessoryColorAssetbundleName,
    string? AccessoryColorVariationPath,
    int BodyCostume3dId,
    int HairCostume3dId,
    int HeadCostume3dId,
    string BodyAssetbundleName,
    string HairAssetbundleName,
    string HeadAssetbundleName,
    string? BodyColorAssetbundleName,
    string? HairColorAssetbundleName,
    string? HeadColorAssetbundleName
);
