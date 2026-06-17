using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record CostumeRegistryExport(
    Character3dIndex Character3dIndex,
    PartRegistry PartRegistry,
    HeadHairCompatibilityRegistry HeadHairCompatibility,
    CardCostumeUnlockRegistry CardCostumeUnlocks
);

public sealed record Character3dIndex(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("source")] IReadOnlyDictionary<string, string> Source,
    [property: JsonPropertyName("entries")] IReadOnlyList<Character3dIndexEntry> Entries
);

public sealed record Character3dIndexEntry(
    [property: JsonPropertyName("character3dId")] int Character3dId,
    [property: JsonPropertyName("characterId")] int CharacterId,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("bodyCostume3dId")] int BodyCostume3dId,
    [property: JsonPropertyName("headCostume3dId")] int HeadCostume3dId,
    [property: JsonPropertyName("hairCostume3dId")] int HairCostume3dId,
    [property: JsonPropertyName("outputPath")] string OutputPath,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
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
    [property: JsonPropertyName("costumeAssetbundleName")] string? CostumeAssetbundleName,
    [property: JsonPropertyName("modelAssetbundleName")] string? ModelAssetbundleName,
    [property: JsonPropertyName("colorAssetbundleName")] string? ColorAssetbundleName,
    [property: JsonPropertyName("headCostume3dAssetbundleType")] string? HeadCostume3dAssetbundleType,
    [property: JsonPropertyName("part")] string? Part,
    [property: JsonPropertyName("bundlePath")] string? BundlePath,
    [property: JsonPropertyName("colorVariationBundlePath")] string? ColorVariationBundlePath,
    [property: JsonPropertyName("packagePath")] string PackagePath,
    [property: JsonPropertyName("attachNode")] string? AttachNode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
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
