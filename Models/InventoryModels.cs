using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record TextureSlotInventory(
    string SlotName,
    string? TextureName,
    long TextureFileId = 0,
    long TexturePathId = 0,
    string? TextureKey = null,
    [property: JsonIgnore] byte[]? TextureData = null,
    float ScaleX = 1f,
    float ScaleY = 1f,
    float OffsetX = 0f,
    float OffsetY = 0f,
    int ColorSpace = 0,
    int SourceWidth = 0,
    int SourceHeight = 0,
    int SourceMipCount = 0,
    int SourceFormat = 0,
    int FilterMode = 0,
    int AnisoLevel = 0,
    float MipBias = 0f,
    int WrapU = 0,
    int WrapV = 0,
    int WrapW = 0
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

public sealed record IntPropertyInventory(
    string Name,
    int Value
);

public sealed record MaterialInventory(
    long MaterialFileId,
    long MaterialPathId,
    string MaterialKey,
    string Name,
    string? ShaderName,
    IReadOnlyList<TextureSlotInventory> TextureSlots,
    IReadOnlyList<ColorPropertyInventory> ColorProperties,
    IReadOnlyList<FloatPropertyInventory> FloatProperties,
    IReadOnlyList<string>? ValidKeywords = null,
    IReadOnlyList<string>? InvalidKeywords = null,
    IReadOnlyList<IntPropertyInventory>? IntProperties = null,
    uint LightmapFlags = 0,
    bool EnableInstancingVariants = false,
    bool DoubleSidedGi = false,
    int CustomRenderQueue = -1,
    IReadOnlyDictionary<string, string>? StringTags = null,
    IReadOnlyList<string>? DisabledShaderPasses = null,
    long ShaderFileId = 0,
    long ShaderPathId = 0,
    string? ShaderKey = null
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
