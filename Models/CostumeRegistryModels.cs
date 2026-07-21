using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record CostumeRegistryExport(
    PartRegistry PartRegistry,
    HeadHairCompatibilityRegistry HeadHairCompatibility,
    CardCostumeUnlockRegistry CardCostumeUnlocks,
    PartSourceMap PartSourceMap
);

public sealed record RuntimeRoleCatalog(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("masterVersion")] string MasterVersion,
    [property: JsonPropertyName("roles")] IReadOnlyList<RuntimeRoleCatalogEntry> Roles
);

public sealed record RuntimeRoleCatalogEntry(
    [property: JsonPropertyName("roleId")] int RoleId,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("bodyCostume3dId")] int BodyCostume3dId,
    [property: JsonPropertyName("headCostume3dId")] int HeadCostume3dId,
    [property: JsonPropertyName("hairCostume3dId")] int HairCostume3dId,
    [property: JsonPropertyName("roleRuntimePath")] string RoleRuntimePath
);

public sealed record PartRegistry(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("source")] IReadOnlyDictionary<string, string> Source,
    [property: JsonPropertyName("entries")] IReadOnlyList<PartRegistryEntry> Entries
);

public sealed record PartRegistryEntry(
    [property: JsonPropertyName("costume3dId")] int Costume3dId,
    [property: JsonPropertyName("partType")] string PartType,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("colorId")] int ColorId,
    [property: JsonPropertyName("colorName")] string? ColorName,
    [property: JsonPropertyName("costume3dGroupId")] int Costume3dGroupId,
    [property: JsonPropertyName("outfitId")] int OutfitId,
    [property: JsonPropertyName("accessoryId")] int AccessoryId,
    [property: JsonPropertyName("costumeAssetbundleName")] string? CostumeAssetbundleName,
    [property: JsonPropertyName("modelAssetbundleName")] string? ModelAssetbundleName,
    [property: JsonPropertyName("colorAssetbundleName")] string? ColorAssetbundleName,
    [property: JsonPropertyName("headCostume3dAssetbundleType")] string? HeadCostume3dAssetbundleType,
    [property: JsonPropertyName("part")] string? Part,
    [property: JsonPropertyName("bundlePath")] string? BundlePath,
    [property: JsonPropertyName("colorVariationBundlePath")] string? ColorVariationBundlePath,
    [property: JsonPropertyName("baseSourceKey")] string? BaseSourceKey,
    [property: JsonPropertyName("sourceKey")] string? SourceKey,
    [property: JsonPropertyName("sourcePackagePath")] string? SourcePackagePath,
    [property: JsonPropertyName("packagePath")] string PackagePath,
    [property: JsonPropertyName("attachNode")] string? AttachNode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PartSourceMap(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("source")] IReadOnlyDictionary<string, string> Source,
    [property: JsonPropertyName("entries")] IReadOnlyList<PartSourceMapEntry> Entries
);

public sealed record PartSourceMapEntry(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("baseSourceKey")] string BaseSourceKey,
    [property: JsonPropertyName("partType")] string PartType,
    [property: JsonPropertyName("bundlePath")] string BundlePath,
    [property: JsonPropertyName("colorVariationBundlePath")] string? ColorVariationBundlePath,
    [property: JsonPropertyName("assetRootRelativeBundlePath")] string? AssetRootRelativeBundlePath,
    [property: JsonPropertyName("assetRootRelativeColorVariationBundlePath")] string? AssetRootRelativeColorVariationBundlePath,
    [property: JsonPropertyName("packagePath")] string PackagePath,
    [property: JsonPropertyName("representative")] PartSourceMapAlias Representative,
    [property: JsonPropertyName("aliases")] IReadOnlyList<PartSourceMapAlias> Aliases
);

public sealed record PartSourceMapAlias(
    [property: JsonPropertyName("costume3dId")] int Costume3dId,
    [property: JsonPropertyName("partType")] string PartType,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("colorId")] int ColorId,
    [property: JsonPropertyName("colorName")] string? ColorName,
    [property: JsonPropertyName("costume3dGroupId")] int Costume3dGroupId
);

public sealed record HeadHairCompatibilityRegistry(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("source")] IReadOnlyDictionary<string, string> Source,
    [property: JsonPropertyName("rules")] IReadOnlyList<HeadHairCompatibilityRule> Rules
);

public sealed record HeadHairCompatibilityRule(
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("headCostume3dId")] int HeadCostume3dId,
    [property: JsonPropertyName("hairCostume3dId")] int HairCostume3dId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("isDefault")] bool IsDefault,
    [property: JsonPropertyName("headCompositionKind")] string HeadCompositionKind,
    [property: JsonPropertyName("activeContributors")] IReadOnlyList<string> ActiveContributors,
    [property: JsonPropertyName("source")] IReadOnlyList<string> Source,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record CardCostumeUnlockRegistry(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("source")] IReadOnlyDictionary<string, string> Source,
    [property: JsonPropertyName("entries")] IReadOnlyList<CardCostumeUnlockEntry> Entries
);

public sealed record CardCostumeUnlockEntry(
    [property: JsonPropertyName("cardId")] int CardId,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("cardRarityType")] string? CardRarityType,
    [property: JsonPropertyName("prefix")] string? Prefix,
    [property: JsonPropertyName("assetbundleName")] string? AssetbundleName,
    [property: JsonPropertyName("releaseAt")] long? ReleaseAt,
    [property: JsonPropertyName("costumes")] IReadOnlyList<CardCostumeUnlockCostume> Costumes,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record CardCostumeUnlockCostume(
    [property: JsonPropertyName("costume3dId")] int Costume3dId,
    [property: JsonPropertyName("partType")] string? PartType,
    [property: JsonPropertyName("costume3dGroupId")] int? Costume3dGroupId,
    [property: JsonPropertyName("colorId")] int? ColorId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("isInitialObtainHair")] bool IsInitialObtainHair
);
