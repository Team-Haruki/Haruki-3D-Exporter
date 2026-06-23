namespace PjskBundle2Parts.Models;

public sealed record ExporterConfig(
    string? Body,
    string? Head,
    string? Output,
    string? Motion,
    string? HeadRoot,
    string? Master,
    string? AssetRoot,
    int? Character3dId,
    bool? KeepIntermediate,
    bool? EmitCostumeRegistries,
    bool? EmitPartPackages,
    bool? ExportFaceMotion,
    int? PartCostume3dId,
    string? PartType,
    string? PartUnit,
    string? SourcePath,
    string? Manifest,
    string? AssetStudioRoot
);
