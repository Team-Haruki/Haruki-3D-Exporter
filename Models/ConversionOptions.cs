namespace PjskBundle2Parts.Models;

public sealed record ConversionOptions(
    string OutputDirectory,
    string? MotionPath,
    string? MasterDirectory,
    string? AssetRoot,
    bool EmitCostumeRegistries,
    bool EmitRuntimeRoleCatalog,
    bool EmitPartPackages,
    bool EmitRoleRuntimes,
    bool ExportFaceMotion,
    int? PartCostume3dId,
    string? PartType,
    string? PartUnit,
    IReadOnlyList<int> RoleCharacter3dIds,
    string? FaceMotionSourcePath,
    string? ManifestPath,
    int PartPackageProcessConcurrency,
    int PartPackageShardCount,
    int PartPackageShardIndex,
    string? PartPackageClaimDirectory,
    string AssetStudioLogLevel,
    bool CompactTextures,
    bool OptimizeTextureStore,
    string? SharedContentStore,
    string? CompiledContentStore,
    string PngOptimizeMode,
    string TextureFormat,
    int TextureCompactWorkers,
    bool ConvertModelTextures,
    string? PartPackageWorkList,
    string? BundleHashIndex
)
{
    public bool OwnsOutputFinalization =>
        string.IsNullOrWhiteSpace(PartPackageClaimDirectory) &&
        string.IsNullOrWhiteSpace(PartPackageWorkList);
}
