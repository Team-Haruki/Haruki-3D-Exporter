namespace PjskBundle2Parts.Models;

public sealed record ConversionOptions(
    string? BodyPath,
    string? HeadPath,
    string OutputDirectory,
    string? MotionPath,
    string? HeadRootName,
    bool KeepIntermediate,
    int? Character3dId,
    string? MasterDirectory,
    string? AssetRoot,
    bool EmitCostumeRegistries,
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
    string RuntimeJsonOutput,
    bool CompactTextures,
    bool OptimizeTextureStore,
    string? SharedContentStore,
    string? CompiledContentStore,
    string PngOptimizeMode,
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
