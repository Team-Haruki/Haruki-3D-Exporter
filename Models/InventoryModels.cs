namespace PjskBundle2Parts.Models;

public sealed record TextureSlotInventory(
    string SlotName,
    string? TextureName
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
    string Name,
    string? ShaderName,
    IReadOnlyList<TextureSlotInventory> TextureSlots,
    IReadOnlyList<ColorPropertyInventory> ColorProperties,
    IReadOnlyList<FloatPropertyInventory> FloatProperties
);

public sealed record RenderMeshInventory(
    string NodeName,
    string NodePath,
    string MeshName,
    int VertexCount,
    int SubMeshCount,
    IReadOnlyList<string> MaterialNames,
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
