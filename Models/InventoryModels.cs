using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record TextureSlotInventory(
    string SlotName,
    string? TextureName,
    long TextureFileId = 0,
    long TexturePathId = 0,
    string? TextureKey = null,
    [property: JsonIgnore] byte[]? TextureData = null
);

public sealed record ColorPropertyInventory(
    string Name,
    float R,
    float G,
    float B,
    float A
);

public sealed record FloatPropertyInventory(
    string Name,
    float Value
);

public sealed record MaterialInventory(
    long MaterialFileId,
    long MaterialPathId,
    string MaterialKey,
    string Name,
    string? ShaderName,
    IReadOnlyList<TextureSlotInventory> TextureSlots,
    IReadOnlyList<ColorPropertyInventory> ColorProperties,
    IReadOnlyList<FloatPropertyInventory> FloatProperties
);

public sealed record RenderMaterialSlotInventory(
    int SlotIndex,
    long MaterialFileId,
    long MaterialPathId,
    string MaterialKey,
    string? MaterialName,
    [property: JsonIgnore] MaterialInventory? ResolvedMaterial = null
);

public sealed record RenderMeshInventory(
    string NodeName,
    string NodePath,
    string MeshName,
    int VertexCount,
    int SubMeshCount,
    IReadOnlyList<RenderMaterialSlotInventory> MaterialSlots,
    IReadOnlyList<string> BoneNames
);

public sealed record RootNodeInventory(
    string Name,
    string Path
);

public sealed record BundleInventory(
    string BundlePath,
    string PartKind,
    int AssetsFileCount,
    int ObjectCount,
    IReadOnlyDictionary<string, int> ObjectTypeCounts,
    IReadOnlyList<RootNodeInventory> Roots,
    IReadOnlyList<string> BoneNames,
    IReadOnlyList<string> AttachNodeCandidates,
    IReadOnlyList<string> OriginNodeCandidates,
    IReadOnlyList<RenderMeshInventory> SkinnedMeshes,
    IReadOnlyList<RenderMeshInventory> StaticMeshes,
    IReadOnlyList<MaterialInventory> Materials
);
