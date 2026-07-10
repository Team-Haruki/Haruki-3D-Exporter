using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using AssetStudio;
using PjskBundle2Parts.Models;
using PjskBundle2Parts.Services;

var parseResult = ConversionOptionsParser.Parse(args);

if (!parseResult.IsSuccess || parseResult.Options is null)
{
    Console.Error.WriteLine(parseResult.ErrorMessage);
    Console.Error.WriteLine();
    Console.Error.WriteLine(ConversionOptionsParser.Usage);
    return 1;
}

var options = parseResult.Options;
Logger.Default = new AssetStudioConsoleLogger(options.AssetStudioLogLevel);
if (options.ExportFaceMotion)
{
    try
    {
        var faceMotionExporter = new MotionPackageExporter();
        var outputPath = faceMotionExporter.ExportFaceMotion(
            options.MotionPath!,
            options.OutputDirectory,
            options.FaceMotionSourcePath
        );
        Console.WriteLine($"Wrote face motion: {outputPath}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Face motion export failed: {ex.Message}");
        return 2;
    }
}

if (options.EmitRoleRuntimes)
{
    try
    {
        if (ResolvePartPackageProcessConcurrency(options) > 1)
        {
            return RunRoleRuntimeWorkers(options);
        }

        var roleRuntimeExporter = new RoleRuntimeExporter();
        var results = roleRuntimeExporter.ExportMany(
            options.MasterDirectory!,
            options.AssetRoot!,
            options.OutputDirectory,
            options.RoleCharacter3dIds,
            options.MotionPath,
            options.RuntimeJsonOutput
        );
        Console.WriteLine($"Wrote {results.Count} role runtime package(s).");
        foreach (var result in results.Where(result => result.Warnings.Count > 0))
        {
            Console.WriteLine($"Warnings for character3d {result.Character3dId}: {result.Warnings.Count}");
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Role runtime export failed: {ex.Message}");
        return 2;
    }
}

if (options.EmitCostumeRegistries)
{
    try
    {
        var costumeRegistryExporter = new CostumeRegistryExporter();
        costumeRegistryExporter.Export(
            options.MasterDirectory!,
            options.AssetRoot!,
            options.OutputDirectory,
            options.RuntimeJsonOutput
        );
        if (options.EmitPartPackages)
        {
            Console.WriteLine("Part package export is incremental; pass --part-costume3d-id and --part-type to build a specific runtime part package.");
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Costume registry export failed: {ex.Message}");
        return 2;
    }
}

if (options.EmitPartPackages)
{
    try
    {
        var partCharacterHeightMetersById = !string.IsNullOrWhiteSpace(options.MasterDirectory)
            ? LoadCharacterHeightMetersById(options.MasterDirectory!)
            : null;
        var partPackageExporter = new PartPackageExporter(partCharacterHeightMetersById);
        if (options.PartCostume3dId is not null && options.PartType is not null)
        {
            var result = partPackageExporter.ExportOne(
                options.MasterDirectory!,
                options.AssetRoot!,
                options.OutputDirectory,
                options.PartCostume3dId.Value,
                options.PartType,
                options.PartUnit,
                options.RuntimeJsonOutput
            );
            Console.WriteLine($"Wrote part runtime package: {result.RuntimePath}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine($"Warnings: {result.Warnings.Count}");
            }
            RunTextureCompactionIfEnabled(options);
        }
        else
        {
            if (ResolvePartPackageProcessConcurrency(options) > 1)
            {
                return RunPartPackageWorkers(options);
            }

            var results = partPackageExporter.ExportAll(
                options.MasterDirectory!,
                options.AssetRoot!,
                options.OutputDirectory,
                options.ManifestPath,
                options.PartPackageShardCount,
                options.PartPackageShardIndex,
                options.RuntimeJsonOutput
            );
            var succeeded = results.Count(result => result.Succeeded);
            var failed = results.Count - succeeded;
            Console.WriteLine($"Wrote {succeeded} part runtime package(s).");
            if (failed > 0)
            {
                Console.Error.WriteLine($"Skipped {failed} part runtime package(s); see part-export-error.json files in the output tree.");
                return 2;
            }
            RunTextureCompactionIfEnabled(options);
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Part package export failed: {ex.Message}");
        return 2;
    }
}

var resolver = new BundleInputResolver();
var character3dCostumeResolver = new Character3dCostumeResolver();
var parser = new AssetStudioBundleParser();
var modelFactory = new AssetStudioImportedModelFactory();
var springBoneExporter = new SpringBoneExporter();
var vrmSpringBoneCandidateBuilder = new VrmSpringBoneCandidateBuilder();
var pjskSekaiRuntimeExtensionBuilder = new PjskSekaiRuntimeExtensionBuilder();
var unityRuntimeNativeMeshExporter = new UnityRuntimeNativeMeshExporter();
var unityRuntimeTextureExporter = new UnityRuntimeTextureExporter();
var motionPackageExporter = new MotionPackageExporter();
var outputPruner = new OutputPruner();
string? motionPath = options.MotionPath;
IReadOnlyDictionary<string, float>? characterHeightMetersById = null;
ResolvedBundleInput bodyInput;
ResolvedBundleInput headInput;
ResolvedBundleInput? accessoryInput = null;
BundleInventory bodyInventory;
BundleInventory headInventory;
BundleInventory? accessoryInventory = null;
ResolvedCharacter3dCostume? resolvedCharacter3dCostume = null;

try
{
    if (!string.IsNullOrWhiteSpace(options.MasterDirectory))
    {
        characterHeightMetersById = LoadCharacterHeightMetersById(options.MasterDirectory!);
    }

    if (options.Character3dId is not null)
    {
        resolvedCharacter3dCostume = character3dCostumeResolver.Resolve(
            options.Character3dId.Value,
            options.MasterDirectory!,
            options.AssetRoot!
        );
        bodyInput = resolver.ResolveBody(resolvedCharacter3dCostume.BodyPath);
        headInput = resolver.ResolveHead(resolvedCharacter3dCostume.MainHeadPath);
        var masterCharacterId = resolvedCharacter3dCostume.CharacterId.ToString("00");
        bodyInput = bodyInput with { CharacterId = masterCharacterId };
        headInput = headInput with { CharacterId = masterCharacterId };
        if (resolvedCharacter3dCostume.AccessoryHeadPath is not null)
        {
            accessoryInput = resolver.ResolveHead(resolvedCharacter3dCostume.AccessoryHeadPath);
            accessoryInput = accessoryInput with { CharacterId = masterCharacterId };
        }
        motionPath ??= ResolveDefaultCostumeSettingMotionPath(
            options.AssetRoot!,
            resolvedCharacter3dCostume
        );
    }
    else
    {
        bodyInput = resolver.ResolveBody(options.BodyPath!);
        headInput = resolver.ResolveHead(options.HeadPath!);
    }
    bodyInventory = parser.Parse(bodyInput);
    headInventory = parser.Parse(headInput);
    if (accessoryInput is not null)
    {
        accessoryInventory = parser.Parse(accessoryInput);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Bundle parsing failed: {ex.Message}");
    return 2;
}

var planner = new ConversionPlanner(characterHeightMetersById);
var plan = planner.CreatePlan(
    bodyInput,
    headInput,
    options.OutputDirectory,
    bodyInventory,
    headInventory,
    options.HeadRootName
);
var bodySpringBone = springBoneExporter.Export(bodyInput);
var headSpringBone = springBoneExporter.Export(headInput);
var combinedSpringBone = new CombinedSpringBoneExport(
    Version: 1,
    Body: bodySpringBone,
    Head: headSpringBone
);
var vrmSpringBoneCandidate = vrmSpringBoneCandidateBuilder.Build(combinedSpringBone);

var importedBody = modelFactory.CreateImportedModel(bodyInput);
var importedHead = modelFactory.CreateImportedModel(headInput, plan.HeadManifestTemplate.Assembly.RootNodeName);
var importedHeadUnityRuntime = modelFactory.CreateImportedModel(headInput, "face");
IImported? headTextureFallback = null;
if (resolvedCharacter3dCostume?.HeadTextureFallbackPath is not null)
{
    var fallbackInput = resolver.ResolveHead(resolvedCharacter3dCostume.HeadTextureFallbackPath)
        with { CharacterId = headInput.CharacterId };
    headTextureFallback = modelFactory.CreateImportedModel(fallbackInput);
    plan = plan with
    {
            HeadManifestTemplate = FillMissingHeadLayerTextures(
                plan.HeadManifestTemplate,
                importedHead,
                headTextureFallback
            ),
    };
}
if (HasMissingHeadLayerTextures(plan.HeadManifestTemplate))
{
    foreach (var fallbackTexturePath in ResolveDefaultHeadLayerTextureFallbackPaths(headInput.ResolvedBundlePath))
    {
        var fallbackTextures = modelFactory.CreateImportedTextures(fallbackTexturePath);
        plan = plan with
        {
            HeadManifestTemplate = FillMissingHeadLayerTexturesFromTextures(
                plan.HeadManifestTemplate,
                importedHead,
                fallbackTextures
            ),
        };

        if (!HasMissingHeadLayerTextures(plan.HeadManifestTemplate))
        {
            break;
        }
    }
}
var importedAccessory = accessoryInput is not null
    ? modelFactory.CreateImportedModel(accessoryInput, SelectAccessoryRootName(accessoryInventory))
    : null;
IReadOnlyList<ImportedTexture> bodyOverrideTextures =
    resolvedCharacter3dCostume?.BodyColorVariationPath is not null
        ? modelFactory.CreateImportedTextures(resolvedCharacter3dCostume.BodyColorVariationPath)
        : Array.Empty<ImportedTexture>();
IReadOnlyList<ImportedTexture> headOverrideTextures =
    resolvedCharacter3dCostume?.MainHeadColorVariationPath is not null
        ? modelFactory.CreateImportedTextures(resolvedCharacter3dCostume.MainHeadColorVariationPath)
        : Array.Empty<ImportedTexture>();
IReadOnlyList<ImportedTexture> accessoryOverrideTextures =
    resolvedCharacter3dCostume?.AccessoryColorVariationPath is not null
        ? modelFactory.CreateImportedTextures(resolvedCharacter3dCostume.AccessoryColorVariationPath)
        : Array.Empty<ImportedTexture>();

outputPruner.PruneLegacyContainers(options.OutputDirectory);
var characterTexturePathByName = unityRuntimeTextureExporter.ExportCharacterTextures(
    Path.Combine(options.OutputDirectory, "character"),
    importedBody,
    importedHead,
    importedAccessory,
    bodyOverrideTextures: bodyOverrideTextures,
    headOverrideTextures: headOverrideTextures,
    accessoryOverrideTextures: accessoryOverrideTextures
);
var updatedBodyManifest = UpdateBodyManifestForUnityRuntime(plan.BodyManifestTemplate);
var updatedHeadManifest = UpdateHeadManifestForUnityRuntime(plan.HeadManifestTemplate, importedHead);
plan = plan with
{
    BodyManifestTemplate = updatedBodyManifest,
    HeadManifestTemplate = updatedHeadManifest,
};
var motionExport = motionPackageExporter.Export(
    motionPath,
    Path.Combine(options.OutputDirectory, "motion"),
    importedBody,
    options.RuntimeJsonOutput
);
if (motionExport.UnityMotionJsonPath is not null)
{
    plan = plan with
    {
        BodyManifestTemplate = plan.BodyManifestTemplate with
        {
            Source = plan.BodyManifestTemplate.Source with
            {
                AnimationUrls = new[]
                {
                    Path.GetRelativePath(
                        options.OutputDirectory,
                        motionExport.UnityMotionJsonPath
                    ).Replace('\\', '/')
                },
            },
        },
    };
}

var pjskSekaiRuntime = pjskSekaiRuntimeExtensionBuilder.Build(
    plan,
    characterTexturePathByName,
    combinedSpringBone,
    vrmSpringBoneCandidate,
    motionExport,
    resolvedCharacter3dCostume,
    accessoryInventory
);
var nativeMeshes = unityRuntimeNativeMeshExporter.Export(
    importedBody,
    importedHeadUnityRuntime,
    pjskSekaiRuntime.Extension.PjskSpringBone.RuntimeUnitySetup,
    importedAccessory,
    resolvedCharacter3dCostume?.AccessoryAttachNode
);
var pjskUnityRuntime = new PjskUnityRuntimePackage(
    Version: "0414",
    UnityVersion: "2022.3.21f1",
    CoordinateSpace: new PjskUnityRuntimeCoordinateSpace(
        Source: "unity-left-handed",
        Viewer: "three-js-right-handed",
        PositionConversion: "viewer_mirror_x",
        RotationConversion: "viewer_negate_quaternion_yz",
        ScaleConversion: "identity",
        Notes: new[]
        {
            "Prefab Transform, renderer, collider, and motion data are stored in Unity source space.",
            "The viewer must apply the declared Unity-to-Three conversion only at the Three.js display/simulation boundary.",
            "Frida/runtime comparisons should use source Unity space or explicitly convert viewer-space diagnostics back to Unity space."
        }
    ),
    AssemblyDiagnostics: BuildUnityRuntimeAssemblyDiagnostics(
        plan,
        pjskSekaiRuntime.Extension.PjskSpringBone.RuntimeUnitySetup,
        pjskSekaiRuntime.Extension.MotionPackage?.BodyMotionBindings
    ),
    Character: pjskSekaiRuntime.Extension.Character,
    Container: pjskSekaiRuntime.Extension.Container,
    BodyManifest: pjskSekaiRuntime.Extension.BodyManifest,
    HeadManifest: pjskSekaiRuntime.Extension.HeadManifest,
    MaterialSlots: pjskSekaiRuntime.Extension.MaterialSlots,
    TextureRoles: pjskSekaiRuntime.Extension.TextureRoles,
    CharacterTextures: pjskSekaiRuntime.Extension.CharacterTextures,
    MorphChannelBindings: pjskSekaiRuntime.Extension.MorphChannelBindings,
    NativeMeshes: nativeMeshes,
    MotionPackage: pjskSekaiRuntime.Extension.MotionPackage,
    CharacterControllers: pjskSekaiRuntime.Extension.CharacterControllers,
    PjskSpringBone: pjskSekaiRuntime.Extension.PjskSpringBone,
    RuntimeUnitySetup: pjskSekaiRuntime.Extension.PjskSpringBone.RuntimeUnitySetup,
    Notes: pjskSekaiRuntime.Extension.Notes
);

Directory.CreateDirectory(options.OutputDirectory);
await WriteJsonAsync(plan.PjskSekaiRuntimeExtensionPath, pjskSekaiRuntime.Extension);
await WriteJsonAsync(plan.CharacterUnityRuntimeJsonPath, pjskUnityRuntime);
await WriteJsonAsync(plan.BodySpringBonePath, bodySpringBone);
await WriteJsonAsync(plan.HeadSpringBonePath, headSpringBone);
await WriteJsonAsync(plan.CombinedSpringBonePath, combinedSpringBone);
await WriteJsonAsync(plan.VrmSpringBoneCandidatePath, vrmSpringBoneCandidate);
if (options.KeepIntermediate)
{
    await planner.WritePlanAsync(plan);
    await planner.WriteInventoriesAsync(plan);
    await planner.WriteManifestTemplatesAsync(plan);
    await planner.WriteSekaiVrmProfileAsync(plan);
    await WriteJsonAsync(plan.PjskSekaiRuntimeResolveReportPath, pjskSekaiRuntime.Report);
}
else
{
    outputPruner.Prune(options.OutputDirectory);
}

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
};

Console.WriteLine("Resolved inputs");
if (resolvedCharacter3dCostume is not null)
{
    Console.WriteLine($"  Character3D: {resolvedCharacter3dCostume.Character3dId} ({resolvedCharacter3dCostume.CharacterName})");
    Console.WriteLine($"  Unit: {resolvedCharacter3dCostume.Unit ?? "<none>"}");
    Console.WriteLine($"  Body costume: {resolvedCharacter3dCostume.BodyCostume3dId} -> {resolvedCharacter3dCostume.BodyAssetbundleName}");
    if (resolvedCharacter3dCostume.BodyColorAssetbundleName is not null)
    {
        Console.WriteLine($"  Body color: {resolvedCharacter3dCostume.BodyColorAssetbundleName} -> {resolvedCharacter3dCostume.BodyColorVariationPath ?? "<missing>"}");
    }
    Console.WriteLine($"  Hair costume: {resolvedCharacter3dCostume.HairCostume3dId} -> {resolvedCharacter3dCostume.HairAssetbundleName} ({resolvedCharacter3dCostume.HairBundleKind}, group {resolvedCharacter3dCostume.HairVariantGroupKey})");
    Console.WriteLine($"  Head costume: {resolvedCharacter3dCostume.HeadCostume3dId} -> {resolvedCharacter3dCostume.HeadAssetbundleName} ({resolvedCharacter3dCostume.HeadColorAssetbundleName ?? "default"}, {resolvedCharacter3dCostume.HeadBundleKind}, group {resolvedCharacter3dCostume.HeadVariantGroupKey})");
    Console.WriteLine($"  Head composition: {resolvedCharacter3dCostume.HeadCompositionKind}");
    Console.WriteLine($"  Main head: {resolvedCharacter3dCostume.MainHeadAssetbundleName} ({resolvedCharacter3dCostume.MainHeadMode}, {resolvedCharacter3dCostume.MainHeadCostumeType})");
    if (resolvedCharacter3dCostume.AccessoryHeadAssetbundleName is not null)
    {
        Console.WriteLine($"  Head accessory: {resolvedCharacter3dCostume.AccessoryHeadAssetbundleName} -> {resolvedCharacter3dCostume.AccessoryHeadPath ?? "<missing>"} ({resolvedCharacter3dCostume.AccessoryHeadCostumeType ?? "head_only"}, attach {resolvedCharacter3dCostume.AccessoryAttachNode ?? "<unknown>"})");
        if (resolvedCharacter3dCostume.AccessoryColorAssetbundleName is not null)
        {
            Console.WriteLine($"  Head accessory color: {resolvedCharacter3dCostume.AccessoryColorAssetbundleName} -> {resolvedCharacter3dCostume.AccessoryColorVariationPath ?? "<missing>"}");
        }
    }
}
    Console.WriteLine($"  Body: {bodyInput.ResolvedBundlePath}");
    Console.WriteLine($"  Head: {headInput.ResolvedBundlePath}");
    if (accessoryInput is not null)
    {
        Console.WriteLine($"  Accessory: {accessoryInput.ResolvedBundlePath}");
    }
    if (motionPath is not null)
    {
        Console.WriteLine($"  Motion: {motionPath}");
    }
Console.WriteLine();
Console.WriteLine("Generated files");
Console.WriteLine($"  PJSK runtime extension: {plan.PjskSekaiRuntimeExtensionPath}");
Console.WriteLine($"  Unity runtime json: {Path.GetRelativePath(options.OutputDirectory, plan.CharacterUnityRuntimeJsonPath).Replace('\\', '/')}");
Console.WriteLine($"  PJSK springbone payload: body={pjskSekaiRuntime.Extension.PjskSpringBone.Raw.Body.Bones.Count}, head={pjskSekaiRuntime.Extension.PjskSpringBone.Raw.Head.Bones.Count}");
if (options.KeepIntermediate)
{
    Console.WriteLine($"  Plan: {plan.PlanPath}");
    Console.WriteLine($"  Body inventory: {plan.BodyInventoryPath}");
    Console.WriteLine($"  Head inventory: {plan.HeadInventoryPath}");
    Console.WriteLine($"  Body template: {plan.BodyManifestTemplatePath}");
    Console.WriteLine($"  Head template: {plan.HeadManifestTemplatePath}");
    Console.WriteLine($"  Sekai VRM profile: {plan.SekaiVrmProfilePath}");
    Console.WriteLine($"  Body springbone: {plan.BodySpringBonePath}");
    Console.WriteLine($"  Head springbone: {plan.HeadSpringBonePath}");
    Console.WriteLine($"  Combined springbone: {plan.CombinedSpringBonePath}");
    Console.WriteLine($"  VRM springbone candidate: {plan.VrmSpringBoneCandidatePath}");
    Console.WriteLine($"  PJSK runtime resolve report: {plan.PjskSekaiRuntimeResolveReportPath}");
}
else
{
    Console.WriteLine("  Intermediate/debug outputs: pruned; springbone metadata JSON retained for handoff");
}
if (motionExport.UnityMotionJsonPath is not null)
{
    Console.WriteLine($"  Unity motion json: {Path.GetRelativePath(options.OutputDirectory, motionExport.UnityMotionJsonPath).Replace('\\', '/')}");
}
if (motionExport.FaceMotion is not null)
{
    Console.WriteLine($"  Embedded face motion clips: {motionExport.FaceMotion.Clips.Count}");
}
Console.WriteLine();
Console.WriteLine("Plan preview");
Console.WriteLine(JsonSerializer.Serialize(plan.Summary, jsonOptions));
return 0;

static async Task WriteJsonAsync<T>(string path, T payload)
{
    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(
        stream,
        payload,
        new JsonSerializerOptions
        {
            WriteIndented = true,
        }
    );
}

static string? ResolveDefaultCostumeSettingMotionPath(
    string assetRoot,
    ResolvedCharacter3dCostume costume
)
{
    var root = Path.GetFullPath(assetRoot);
    var fileName = $"{ResolveCostumeSettingMotionCharacterId(costume):00}_00.bundle";
    var candidates = new[]
    {
        Path.Combine(root, "character", "motion", "costume_setting", fileName),
        Path.Combine(root, "motion", "costume_setting", fileName),
        Path.Combine(root, "costume_setting", fileName),
    };
    return candidates.FirstOrDefault(File.Exists);
}

static int ResolveCostumeSettingMotionCharacterId(ResolvedCharacter3dCostume costume)
{
    if (costume.CharacterId != 21)
    {
        return costume.CharacterId;
    }

    return (costume.Unit ?? string.Empty).ToLowerInvariant() switch
    {
        "light_sound" => 27,
        "idol" => 28,
        "street" => 29,
        "theme_park" => 30,
        "school_refusal" => 31,
        _ => 21,
    };
}

static BodyAssetManifest UpdateBodyManifestForUnityRuntime(BodyAssetManifest manifest)
{
    return manifest with
    {
        Source = manifest.Source with
        {
            MeshUrl = "character/unity-runtime.json",
            SkeletonUrl = "character/unity-runtime.json",
        },
    };
}

static HeadAssetManifest FillMissingHeadLayerTextures(
    HeadAssetManifest manifest,
    IImported importedHead,
    IImported fallbackHead
)
{
    var changed = false;
    var materials = manifest.FaceMaterials
        .Select(slot =>
        {
            if (
                !string.IsNullOrWhiteSpace(slot.MainTex) ||
                slot.MaterialKind is not ("eye" or "eyelight")
            )
            {
                return slot;
            }

            var fallbackTexture = FindFallbackHeadLayerTexture(fallbackHead, slot.MaterialKind);
            if (fallbackTexture is null)
            {
                return slot;
            }

            AddImportedTextureIfMissing(importedHead.TextureList, fallbackTexture);
            changed = true;
            return slot with
            {
                MainTex = Path.GetFileNameWithoutExtension(fallbackTexture.Name),
            };
        })
        .ToList();

    return changed
        ? manifest with { FaceMaterials = materials }
        : manifest;
}

static HeadAssetManifest FillMissingHeadLayerTexturesFromTextures(
    HeadAssetManifest manifest,
    IImported importedHead,
    IReadOnlyList<ImportedTexture> fallbackTextures
)
{
    var changed = false;
    var materials = manifest.FaceMaterials
        .Select(slot =>
        {
            if (
                !string.IsNullOrWhiteSpace(slot.MainTex) ||
                slot.MaterialKind is not ("eye" or "eyelight")
            )
            {
                return slot;
            }

            var fallbackTexture = FindFallbackHeadLayerTextureFromTextures(fallbackTextures, slot.MaterialKind);
            if (fallbackTexture is null)
            {
                return slot;
            }

            AddImportedTextureIfMissing(importedHead.TextureList, fallbackTexture);
            changed = true;
            return slot with
            {
                MainTex = Path.GetFileNameWithoutExtension(fallbackTexture.Name),
            };
        })
        .ToList();

    return changed
        ? manifest with { FaceMaterials = materials }
        : manifest;
}

static ImportedTexture? FindFallbackHeadLayerTexture(
    IImported fallbackHead,
    string materialKind
)
{
    var material = fallbackHead.MaterialList.FirstOrDefault(material =>
    {
        var name = material.Name.ToLowerInvariant();
        return materialKind == "eyelight"
            ? name.Contains("_ehl_")
            : name.Contains("_eye_") && !name.Contains("eyelash");
    });

    if (material is not null)
    {
        foreach (var textureRef in material.Textures)
        {
            var texture = fallbackHead.TextureList.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, textureRef.Name, StringComparison.OrdinalIgnoreCase));
            if (texture is not null)
            {
                return texture;
            }
        }
    }

    return fallbackHead.TextureList.FirstOrDefault(texture =>
    {
        var name = texture.Name.ToLowerInvariant();
        return materialKind == "eyelight"
            ? name.Contains("tex_ehl_")
            : name.Contains("tex_eye_");
    });
}

static ImportedTexture? FindFallbackHeadLayerTextureFromTextures(
    IReadOnlyList<ImportedTexture> fallbackTextures,
    string materialKind
)
{
    return fallbackTextures.FirstOrDefault(texture =>
    {
        var name = texture.Name.ToLowerInvariant();
        return materialKind == "eyelight"
            ? name.Contains("tex_ehl_")
            : name.Contains("tex_eye_");
    });
}

static void AddImportedTextureIfMissing(
    List<ImportedTexture> destination,
    ImportedTexture source
)
{
    if (destination.Any(texture =>
            string.Equals(texture.Name, source.Name, StringComparison.OrdinalIgnoreCase)))
    {
        return;
    }

    using var stream = new MemoryStream(source.Data);
    destination.Add(new ImportedTexture(stream, source.Name));
}

static bool HasMissingHeadLayerTextures(HeadAssetManifest manifest)
{
    return manifest.FaceMaterials.Any(slot =>
        string.IsNullOrWhiteSpace(slot.MainTex) &&
        slot.MaterialKind is "eye" or "eyelight");
}

static IReadOnlyList<string> ResolveDefaultHeadLayerTextureFallbackPaths(string headBundlePath)
{
    var directory = Path.GetDirectoryName(headBundlePath);
    if (string.IsNullOrWhiteSpace(directory))
    {
        return Array.Empty<string>();
    }

    return new[] { "0001.bundle", "0001_mc.bundle" }
        .Select(fileName => Path.Combine(directory, fileName))
        .Where(path =>
            File.Exists(path) &&
            !string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(headBundlePath),
                StringComparison.OrdinalIgnoreCase
            ))
        .ToList();
}

static HeadAssetManifest UpdateHeadManifestForUnityRuntime(
    HeadAssetManifest manifest,
    IImported importedHead
)
{
    return manifest with
    {
        Source = manifest.Source with
        {
            MeshUrl = "character/unity-runtime.json",
        },
        MorphChannels = importedHead.MorphList
            .Where(morph => morph.Path.EndsWith("/Face", StringComparison.OrdinalIgnoreCase) || string.Equals(morph.Path, "face/Face", StringComparison.OrdinalIgnoreCase))
            .SelectMany(morph => morph.Channels.Select(channel => channel.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        MorphChannelBindings = ReadHeadMorphBindings(importedHead),
    };
}

static PjskUnityRuntimeAssemblyDiagnostics BuildUnityRuntimeAssemblyDiagnostics(
    ConversionPlan plan,
    PjskSpringBoneRuntimeUnitySetup setup,
    PjskBodyMotionBindingSet? bodyMotionBindings
)
{
    var transformByPathId = setup.PrefabGraphs
        .SelectMany(graph => graph.Transforms)
        .GroupBy(transform => transform.PathId)
        .ToDictionary(group => group.Key, group => group.First());
    var transformByPath = setup.PrefabGraphs
        .SelectMany(graph => graph.Transforms)
        .Where(transform => !string.IsNullOrWhiteSpace(transform.TransformPath))
        .GroupBy(transform => transform.TransformPath!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var warnings = new List<string>();

    var defaultBodyRoot = setup.ActiveRootProfile.DefaultBodyRoot;
    var bodyRootPath = ResolveRootPath(setup, defaultBodyRoot)
        ?? setup.RootSelectionProfile.DefaultBodyRoot;
    var headRootPath = ResolveRootPath(setup, "face") ?? "face";
    var bodyAttachPath = setup.BodyHeadAssembly.ParentAttachPath ??
        ResolveNamedPathInRoot(
            setup,
            defaultBodyRoot,
            plan.BodyManifestTemplate.Skeleton.NeckAttach.NodeName
        ) ?? ResolveFirstExistingPath(transformByPath, new[]
    {
        $"{defaultBodyRoot}/Position/PositionOffset/Hip/Waist/Spine/Chest/Neck",
        $"{defaultBodyRoot}/Position/Hip/Waist/Spine/Chest/Neck",
    });
    var headOriginPath = setup.BodyHeadAssembly.ChildOriginPath ??
        ResolveFirstExistingPath(transformByPath, new[]
    {
        "face/Position/Hip/Waist/Spine/Chest/Neck",
    }) ?? ResolveNamedPathInRoot(
        setup,
        "face",
        plan.HeadManifestTemplate.Assembly.AttachOrigin.NodeName
    );

    AddMissingWarning(warnings, "body root", bodyRootPath, transformByPath);
    AddMissingWarning(warnings, "head root", headRootPath, transformByPath);
    AddMissingWarning(warnings, "body attach", bodyAttachPath, transformByPath);
    AddMissingWarning(warnings, "head origin", headOriginPath, transformByPath);

    (string Label, string PartKind, string? Path)[] keyPathSpecs =
    {
        ("bodyRoot", "Body", bodyRootPath),
        ("headRoot", "Head", headRootPath),
        ("bodyAttach", "Body", bodyAttachPath),
        ("headOrigin", "Head", headOriginPath),
        ("id5ExpectedBodyNeckA", "Body", "body/Position/PositionOffset/Hip/Waist/Spine/Chest/Neck"),
        ("id5ExpectedBodyNeckB", "Body", "body/Position/Hip/Waist/Spine/Chest/Neck"),
        ("expectedBodyHeadA", "Body", "body/Position/PositionOffset/Hip/Waist/Spine/Chest/Neck/Head"),
        ("expectedBodyHeadB", "Body", "body/Position/Hip/Waist/Spine/Chest/Neck/Head"),
        ("id5ExpectedFacePosition", "Head", "face/Position"),
        ("expectedFaceNeck", "Head", "face/Position/Hip/Waist/Spine/Chest/Neck"),
        ("expectedFaceHead", "Head", "face/Position/Hip/Waist/Spine/Chest/Neck/Head"),
    };
    var keyPaths = keyPathSpecs
        .Select(item => BuildKeyPathResolution(item.Label, item.PartKind, item.Path, transformByPath))
        .ToList();

    var rendererDiagnostics = setup.PrefabGraphs
        .SelectMany(graph => graph.Renderers.Select(renderer => BuildRendererDiagnostic(graph.PartKind, renderer, transformByPathId)))
        .OrderBy(renderer => renderer.PartKind, StringComparer.Ordinal)
        .ThenBy(renderer => renderer.TransformPath, StringComparer.Ordinal)
        .ThenBy(renderer => renderer.PathId)
        .ToList();

    return new PjskUnityRuntimeAssemblyDiagnostics(
        Version: "0414",
        BodyRootPath: bodyRootPath,
        HeadRootPath: headRootPath,
        BodyAttachPath: bodyAttachPath,
        HeadOriginPath: headOriginPath,
        CoordinateSpaceSource: setup.CoordinateSpace.Source,
        CoordinateSpaceViewer: setup.CoordinateSpace.Viewer,
        KeyPathResolutions: keyPaths,
        RendererDiagnostics: rendererDiagnostics,
        MotionTargetCoverage: BuildMotionTargetCoverage(bodyMotionBindings),
        Warnings: warnings
            .Distinct(StringComparer.Ordinal)
            .OrderBy(warning => warning, StringComparer.Ordinal)
            .ToList()
    );
}

static string? ResolveRootPath(PjskSpringBoneRuntimeUnitySetup setup, string? rootName)
{
    if (string.IsNullOrWhiteSpace(rootName))
    {
        return null;
    }

    return setup.PrefabGraphs
        .SelectMany(graph => graph.Transforms)
        .Where(transform => string.Equals(transform.TransformPath, rootName, StringComparison.Ordinal))
        .Select(transform => transform.TransformPath)
        .FirstOrDefault();
}

static string? ResolveNamedPathInRoot(
    PjskSpringBoneRuntimeUnitySetup setup,
    string rootName,
    string? nodeName
)
{
    if (string.IsNullOrWhiteSpace(nodeName))
    {
        return null;
    }

    return setup.PrefabGraphs
        .SelectMany(graph => graph.Transforms)
        .Where(transform =>
            string.Equals(transform.PoseRoot, rootName, StringComparison.Ordinal) &&
            string.Equals(transform.Name, nodeName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(transform.TransformPath))
        .OrderByDescending(transform => transform.TransformPath!.Count(ch => ch == '/'))
        .Select(transform => transform.TransformPath)
        .FirstOrDefault();
}

static string? ResolveFirstExistingPath(
    IReadOnlyDictionary<string, SpringPrefabTransform> transformByPath,
    IReadOnlyList<string> candidates
)
{
    return candidates.FirstOrDefault(transformByPath.ContainsKey);
}

static void AddMissingWarning(
    List<string> warnings,
    string label,
    string? path,
    IReadOnlyDictionary<string, SpringPrefabTransform> transformByPath
)
{
    if (string.IsNullOrWhiteSpace(path) || !transformByPath.ContainsKey(path))
    {
        warnings.Add($"Unity assembly diagnostic did not resolve {label}: {path ?? "<null>"}.");
    }
}

static PjskUnityRuntimeKeyPathResolution BuildKeyPathResolution(
    string label,
    string partKind,
    string? path,
    IReadOnlyDictionary<string, SpringPrefabTransform> transformByPath
)
{
    SpringPrefabTransform? transform = null;
    var resolved = path is not null && transformByPath.TryGetValue(path, out transform);
    return new PjskUnityRuntimeKeyPathResolution(
        Label: label,
        PartKind: partKind,
        ExpectedPath: path,
        Resolved: resolved,
        PathId: resolved ? transform!.PathId : null,
        NodeName: resolved ? transform!.Name : null,
        ResolvedPath: resolved ? transform!.TransformPath : null
    );
}

static PjskUnityRuntimeRendererDiagnostic BuildRendererDiagnostic(
    string partKind,
    SpringPrefabRenderer renderer,
    IReadOnlyDictionary<long, SpringPrefabTransform> transformByPathId
)
{
    var rootBonePath = renderer.RootBonePathId is long rootBonePathId &&
        transformByPathId.TryGetValue(rootBonePathId, out var rootBone)
        ? rootBone.TransformPath
        : null;
    var resolvedBones = renderer.SkinnedMeshBones
        .Select(pathId => transformByPathId.TryGetValue(pathId, out var transform)
            ? transform.TransformPath
            : null)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Cast<string>()
        .ToList();
    var missingBones = renderer.SkinnedMeshBones
        .Where(pathId => !transformByPathId.ContainsKey(pathId))
        .ToList();

    return new PjskUnityRuntimeRendererDiagnostic(
        PartKind: partKind,
        PathId: renderer.PathId,
        Name: renderer.Name,
        TransformPath: renderer.TransformPath,
        MeshName: renderer.MeshName,
        Enabled: renderer.Enabled,
        RootBonePathId: renderer.RootBonePathId,
        RootBonePath: rootBonePath,
        SkinnedBoneCount: renderer.SkinnedMeshBones.Count,
        ResolvedSkinnedBoneCount: resolvedBones.Count,
        SampleSkinnedBonePaths: resolvedBones.Take(24).ToList(),
        MissingSkinnedBonePathIds: missingBones
    );
}

static PjskUnityRuntimeMotionTargetCoverage BuildMotionTargetCoverage(
    PjskBodyMotionBindingSet? bodyMotionBindings
)
{
    if (bodyMotionBindings is null)
    {
        return new PjskUnityRuntimeMotionTargetCoverage(
            BindingCount: 0,
            ResolvedBindingCount: 0,
            UnresolvedBindingCount: 0,
            TotalTargetCount: 0,
            BodyTargetCount: 0,
            HeadTargetCount: 0,
            SampleUnresolvedBindings: Array.Empty<string>()
        );
    }

    var resolved = bodyMotionBindings.Bindings.Count(binding => binding.TargetCount > 0);
    var unresolved = bodyMotionBindings.Bindings
        .Where(binding => binding.TargetCount <= 0)
        .Select(binding => $"{binding.NodeKey}:{binding.ImportedPath ?? binding.LeafName}")
        .Take(24)
        .ToList();
    var targets = bodyMotionBindings.Bindings.SelectMany(binding => binding.Targets).ToList();
    return new PjskUnityRuntimeMotionTargetCoverage(
        BindingCount: bodyMotionBindings.Bindings.Count,
        ResolvedBindingCount: resolved,
        UnresolvedBindingCount: bodyMotionBindings.Bindings.Count - resolved,
        TotalTargetCount: targets.Count,
        BodyTargetCount: targets.Count(target => string.Equals(target.PoseRoot, "body", StringComparison.Ordinal)),
        HeadTargetCount: targets.Count(target => string.Equals(target.PoseRoot, "face", StringComparison.Ordinal)),
        SampleUnresolvedBindings: unresolved
    );
}

static uint ComputeCurveHash(string sourceName)
{
    var rawName = sourceName.StartsWith("blendShape.", StringComparison.OrdinalIgnoreCase)
        ? sourceName.Substring("blendShape.".Length)
        : sourceName;
    var crc = new SevenZip.CRC();
    var bytes = Encoding.UTF8.GetBytes(rawName);
    crc.Update(bytes, 0, (uint)bytes.Length);
    return crc.GetDigest();
}

static IReadOnlyList<HeadMorphChannel> ReadHeadMorphBindings(IImported importedHead)
{
    if (importedHead is not ModelConverter converter)
    {
        return Array.Empty<HeadMorphChannel>();
    }

    var field = typeof(ModelConverter).GetField("morphChannelNames", BindingFlags.NonPublic | BindingFlags.Instance);
    if (field?.GetValue(converter) is not Dictionary<uint, string> table)
    {
        return Array.Empty<HeadMorphChannel>();
    }

    return table
        .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        .Select(pair => new HeadMorphChannel(
            Name: pair.Value.Split('.').Last(),
            SourceName: pair.Value,
            NameHash: pair.Key,
            CurveHash: ComputeCurveHash(pair.Value)
        ))
        .ToList();
}

static IReadOnlyDictionary<string, float> LoadCharacterHeightMetersById(string masterDirectory)
{
    return CharacterHeightResolver.LoadMetersByCharacterId(masterDirectory);
}

static int RunPartPackageWorkers(ConversionOptions options)
{
    var workers = ResolvePartPackageProcessConcurrency(options);
    var manifestPath = options.ManifestPath
        ?? Path.Combine(options.OutputDirectory, "haruki-3d-export-manifest.json");
    var shardManifestPaths = Enumerable.Range(0, workers)
        .Select(index => $"{manifestPath}.shard-{index}")
        .ToList();
    var processes = new List<Process>();

    foreach (var shardManifestPath in shardManifestPaths)
    {
        File.Delete(shardManifestPath);
        if (File.Exists(manifestPath))
        {
            File.Copy(manifestPath, shardManifestPath);
        }
    }

    for (var index = 0; index < workers; index++)
    {
        var startInfo = CreateCurrentProcessStartInfo(new[]
        {
            "--emit-part-packages",
            "--master", options.MasterDirectory!,
            "--asset-root", options.AssetRoot!,
            "--out", options.OutputDirectory,
            "--manifest", shardManifestPaths[index],
            "--part-package-process-concurrency", "1",
            "--part-package-shard-count", workers.ToString(CultureInfo.InvariantCulture),
            "--part-package-shard-index", index.ToString(CultureInfo.InvariantCulture),
            "--assetstudio-log-level", options.AssetStudioLogLevel,
            "--runtime-json-output", options.RuntimeJsonOutput,
        });
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start part package worker {index}.");
        processes.Add(process);
        Console.WriteLine($"Started part package worker {index + 1}/{workers}: pid {process.Id}");
    }

    var failed = false;
    foreach (var process in processes)
    {
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Part package worker pid {process.Id} exited with code {process.ExitCode}.");
            failed = true;
        }
    }

    if (failed)
    {
        return 2;
    }

    PartPackageExportManifest.Merge(manifestPath, shardManifestPaths);
    Console.WriteLine($"Merged {workers} part package manifest shard(s): {manifestPath}");
    RunTextureCompactionIfEnabled(options);
    return 0;
}

static int RunRoleRuntimeWorkers(ConversionOptions options)
{
    var ids = RoleRuntimeExporter.ResolveExportCharacter3dIds(
        options.MasterDirectory!,
        options.RoleCharacter3dIds
    );
    var workers = Math.Min(ResolvePartPackageProcessConcurrency(options), ids.Count);
    var shards = Enumerable.Range(0, workers)
        .Select(_ => new List<int>())
        .ToList();
    for (var index = 0; index < ids.Count; index++)
    {
        shards[index % workers].Add(ids[index]);
    }

    var processes = new List<Process>();
    for (var index = 0; index < workers; index++)
    {
        var arguments = new List<string>
        {
            "--emit-role-runtimes",
            "--master", options.MasterDirectory!,
            "--asset-root", options.AssetRoot!,
            "--out", options.OutputDirectory,
            "--part-package-process-concurrency", "1",
            "--assetstudio-log-level", options.AssetStudioLogLevel,
            "--runtime-json-output", options.RuntimeJsonOutput,
        };
        if (!string.IsNullOrWhiteSpace(options.MotionPath))
        {
            arguments.Add("--motion");
            arguments.Add(options.MotionPath!);
        }
        foreach (var id in shards[index])
        {
            arguments.Add("--role-character3d-id");
            arguments.Add(id.ToString(CultureInfo.InvariantCulture));
        }

        var process = Process.Start(CreateCurrentProcessStartInfo(arguments))
            ?? throw new InvalidOperationException($"Failed to start role runtime worker {index}.");
        processes.Add(process);
        Console.WriteLine($"Started role runtime worker {index + 1}/{workers}: pid {process.Id}, {shards[index].Count} role(s)");
    }

    var failed = false;
    foreach (var process in processes)
    {
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Role runtime worker pid {process.Id} exited with code {process.ExitCode}.");
            failed = true;
        }
    }

    return failed ? 2 : 0;
}

static void RunTextureCompactionIfEnabled(ConversionOptions options)
{
    if (!options.CompactTextures)
    {
        return;
    }
    if (options.PartPackageShardCount > 1)
    {
        return;
    }
    var compactor = new TextureCompactor();
    var report = compactor.Compact(
        options.OutputDirectory,
        options.RuntimeJsonOutput,
        options.PngOptimizeMode,
        options.TextureCompactWorkers
    );
    Console.WriteLine(
        "Compacted textures: " +
        $"{report.TextureFileCount} file(s), {report.UniqueHashCount} unique hash(es), " +
        $"saved {report.SavedBytes} byte(s), rewrote {report.RewrittenReferenceCount} reference(s)."
    );
}

static int ResolvePartPackageProcessConcurrency(ConversionOptions options)
{
    return options.PartPackageProcessConcurrency == 0
        ? Environment.ProcessorCount
        : options.PartPackageProcessConcurrency;
}

static ProcessStartInfo CreateCurrentProcessStartInfo(IEnumerable<string> arguments)
{
    var processPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not resolve current process path.");
    var assemblyPath = Assembly.GetEntryAssembly()?.Location;
    var startInfo = new ProcessStartInfo(processPath);

    if (Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(assemblyPath))
    {
        startInfo.ArgumentList.Add(assemblyPath);
    }

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }
    startInfo.WorkingDirectory = Environment.CurrentDirectory;
    startInfo.UseShellExecute = false;
    return startInfo;
}

static string? SelectAccessoryRootName(BundleInventory? inventory)
{
    if (inventory is null)
    {
        return null;
    }

    return inventory.Roots.FirstOrDefault(root =>
            root.Name.StartsWith("mdl_acc_", StringComparison.OrdinalIgnoreCase))
        ?.Name
        ?? inventory.Roots.FirstOrDefault(root =>
            !string.Equals(root.Name, "optional", StringComparison.OrdinalIgnoreCase))
        ?.Name
        ?? inventory.Roots.FirstOrDefault()?.Name;
}
