using PjskBundle2Parts.Models;
using System.Text.Json;

namespace PjskBundle2Parts.Services;

public static class ConversionOptionsParser
{
    public static string Usage =>
        "Usage:\n" +
        "  Haruki-3D-Exporter --emit-costume-registries --master <master-directory> --asset-root <AssetBundles-root> --out <directory>\n" +
        "  Haruki-3D-Exporter --emit-part-packages --part-costume3d-id <id> --part-type <body|head|hair|head_optional> --master <master-directory> --asset-root <AssetBundles-root> --out <directory> [--part-unit <unit>]\n\n" +
        "  Haruki-3D-Exporter --emit-role-runtimes [--role-character3d-id <id>] --master <master-directory> --asset-root <AssetBundles-root> --out <directory> [--motion <bundle-or-export-folder>]\n" +
        "  Haruki-3D-Exporter --export-face-motion --motion <bundle-or-decoded-folder-or-json> --out <face_motion.json-or-directory> [--source-path <bundle-path>]\n\n" +
        "  Add --config <json> to load defaults from haruki-3d-exporter.config.json.\n\n" +
        "Notes:\n" +
        "  --master provides the masterdata used to resolve runtime roles and parts\n" +
        "  --asset-root points at the AssetBundles root containing live_pv/model/characterv2\n" +
        "  --emit-costume-registries writes .msgpack.br character, part, compatibility, and unlock registries\n" +
        "  --emit-part-packages writes core+delta part-runtime.msgpack.br packages for runtime custom assembly\n" +
        "  --emit-role-runtimes writes roles/<characterId>/<unit>/role-runtime.msgpack.br with motion metadata; without --role-character3d-id it exports one representative row per character+unit role\n" +
        "  --manifest records part package input file stamps for incremental --emit-part-packages runs\n" +
        "  --part-package-process-concurrency runs role or full part exports across N workers; 0 = auto CPU count\n" +
        "  --part-package-workers and --part-package-core-count are aliases for --part-package-process-concurrency\n" +
        "  --part-package-shard-count and --part-package-shard-index run one deterministic package shard\n" +
        "  --assetstudio-log-level controls AssetStudio logs: warning, info, or debug\n" +
        "  --convert-model-textures controls AssetStudio model texture conversion: true or false\n" +
        "  --compact-textures deduplicates package textures by exact SHA-256 and rewrites runtime package paths\n" +
        "  --shared-content-store hard-links exact texture and part-runtime bytes into a shared cross-region CAS\n" +
        "  --bundle-hash-index reuses updater-provided SHA-256 values when fingerprinting source bundles\n" +
        "  --png-optimize controls lossless PNG optimization during compaction: oxipng or off\n" +
        "  --texture-compact-workers limits concurrent PNG optimizers; 0 = min(4, CPU count)\n" +
        "  --export-face-motion writes face_motion.json from a costume_setting bundle or decoded AnimationClip JSON without Python helpers\n" +
        "  --motion accepts a costume_setting bundle or a folder containing unity-motion.json/face_motion.json/light_motion.json\n" +
        "  runtime metadata is always emitted as Brotli-compressed MessagePack";

    public static ParseResult Parse(string[] args)
    {
        string? output = null;
        string? motion = null;
        string? masterDirectory = null;
        string? assetRoot = null;
        var emitCostumeRegistries = false;
        var emitPartPackages = false;
        var emitRoleRuntimes = false;
        var exportFaceMotion = false;
        int? partCostume3dId = null;
        string? partType = null;
        string? partUnit = null;
        var roleCharacter3dIds = new List<int>();
        string? sourcePath = null;
        string? manifestPath = null;
        string? configPath = null;
        var partPackageProcessConcurrency = 1;
        var partPackageShardCount = 1;
        var partPackageShardIndex = 0;
        string? partPackageClaimDirectory = null;
        var assetStudioLogLevel = "warning";
        var compactTextures = false;
        var optimizeTextureStore = false;
        string? sharedContentStore = null;
        string? compiledContentStore = null;
        var pngOptimize = "oxipng";
        var textureCompactWorkers = 0;
        var convertModelTextures = false;
        string? partPackageWorkList = null;
        string? bundleHashIndex = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--config")
            {
                configPath = ReadValue(args, ref i, args[i]);
            }
        }

        if (string.IsNullOrWhiteSpace(configPath) && File.Exists("haruki-3d-exporter.config.json"))
        {
            configPath = "haruki-3d-exporter.config.json";
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            try
            {
                var config = LoadConfig(configPath);
                output = config.Output;
                motion = config.Motion;
                masterDirectory = config.Master;
                assetRoot = config.AssetRoot;
                emitCostumeRegistries = config.EmitCostumeRegistries ?? false;
                emitPartPackages = config.EmitPartPackages ?? false;
                emitRoleRuntimes = config.EmitRoleRuntimes ?? false;
                exportFaceMotion = config.ExportFaceMotion ?? false;
                partCostume3dId = config.PartCostume3dId;
                partType = config.PartType;
                partUnit = config.PartUnit;
                roleCharacter3dIds = config.RoleCharacter3dIds?.Distinct().ToList() ?? new List<int>();
                sourcePath = config.SourcePath;
                manifestPath = config.Manifest;
                partPackageProcessConcurrency =
                    config.PartPackageProcessConcurrency ??
                    config.PartPackageWorkers ??
                    config.PartPackageCoreCount ??
                    1;
                partPackageShardCount = config.PartPackageShardCount ?? 1;
                partPackageShardIndex = config.PartPackageShardIndex ?? 0;
                partPackageClaimDirectory = config.PartPackageClaimDirectory;
                assetStudioLogLevel = string.IsNullOrWhiteSpace(config.AssetStudioLogLevel)
                    ? "warning"
                    : config.AssetStudioLogLevel!;
                compactTextures = config.CompactTextures ?? false;
                optimizeTextureStore = config.OptimizeTextureStore ?? false;
                sharedContentStore = config.SharedContentStore;
                compiledContentStore = config.CompiledContentStore;
                pngOptimize = string.IsNullOrWhiteSpace(config.PngOptimize)
                    ? "oxipng"
                    : config.PngOptimize!;
                textureCompactWorkers = config.TextureCompactWorkers ?? 0;
                convertModelTextures = config.ConvertModelTextures ?? false;
                partPackageWorkList = config.PartPackageWorkList;
                bundleHashIndex = config.BundleHashIndex;
            }
            catch (Exception ex)
            {
                return new ParseResult(false, null, $"Failed to read --config {configPath}: {ex.Message}");
            }
        }

        if (args.Length == 0 && string.IsNullOrWhiteSpace(configPath))
        {
            return new ParseResult(false, null, "Missing arguments.");
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--config")
            {
                _ = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--out" or "-o")
            {
                output = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--motion" or "-m")
            {
                motion = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--master")
            {
                masterDirectory = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--asset-root")
            {
                assetRoot = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--emit-costume-registries")
            {
                emitCostumeRegistries = true;
                continue;
            }

            if (arg is "--emit-part-packages")
            {
                emitPartPackages = true;
                continue;
            }

            if (arg is "--emit-role-runtimes")
            {
                emitRoleRuntimes = true;
                continue;
            }

            if (arg is "--export-face-motion")
            {
                exportFaceMotion = true;
                continue;
            }

            if (arg is "--part-costume3d-id")
            {
                var value = ReadValue(args, ref i, arg);
                if (!int.TryParse(value, out var parsed))
                {
                    return new ParseResult(false, null, $"Option {arg} must be an integer.");
                }
                partCostume3dId = parsed;
                continue;
            }

            if (arg is "--part-type")
            {
                partType = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--part-unit")
            {
                partUnit = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--role-character3d-id")
            {
                var value = ReadValue(args, ref i, arg);
                if (!int.TryParse(value, out var parsed))
                {
                    return new ParseResult(false, null, $"Option {arg} must be an integer.");
                }
                roleCharacter3dIds.Add(parsed);
                continue;
            }

            if (arg is "--source-path")
            {
                sourcePath = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--manifest")
            {
                manifestPath = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--part-package-process-concurrency" or "--part-package-workers" or "--part-package-core-count")
            {
                partPackageProcessConcurrency = ReadIntValue(args, ref i, arg);
                continue;
            }

            if (arg is "--assetstudio-log-level")
            {
                assetStudioLogLevel = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--compact-textures")
            {
                compactTextures = true;
                continue;
            }

            if (arg is "--optimize-texture-store")
            {
                optimizeTextureStore = true;
                continue;
            }

            if (arg is "--shared-content-store")
            {
                sharedContentStore = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--compiled-content-store")
            {
                compiledContentStore = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--png-optimize")
            {
                pngOptimize = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--texture-compact-workers")
            {
                textureCompactWorkers = ReadIntValue(args, ref i, arg);
                continue;
            }

            if (arg is "--convert-model-textures")
            {
                convertModelTextures = ReadBoolValue(args, ref i, arg);
                continue;
            }

            if (arg is "--part-package-shard-count")
            {
                partPackageShardCount = ReadIntValue(args, ref i, arg);
                continue;
            }

            if (arg is "--part-package-shard-index")
            {
                partPackageShardIndex = ReadIntValue(args, ref i, arg);
                continue;
            }

            if (arg is "--part-package-claim-directory")
            {
                partPackageClaimDirectory = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--part-package-work-list")
            {
                partPackageWorkList = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--bundle-hash-index")
            {
                bundleHashIndex = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--help" or "-?")
            {
                return new ParseResult(false, null, "Help requested.");
            }

            return new ParseResult(false, null, $"Unknown argument: {arg}");
        }

        if (exportFaceMotion)
        {
            if (string.IsNullOrWhiteSpace(motion))
            {
                return new ParseResult(false, null, "Missing --motion for --export-face-motion.");
            }
        }
        else if (optimizeTextureStore)
        {
        }
        else if (emitCostumeRegistries || emitPartPackages || emitRoleRuntimes)
        {
            if (string.IsNullOrWhiteSpace(masterDirectory))
            {
                return new ParseResult(false, null, $"Missing --master for {ResolveRegistryModeName(emitPartPackages, emitRoleRuntimes)}.");
            }

            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                return new ParseResult(false, null, $"Missing --asset-root for {ResolveRegistryModeName(emitPartPackages, emitRoleRuntimes)}.");
            }

            if (emitPartPackages && !emitCostumeRegistries && (partCostume3dId is null) != string.IsNullOrWhiteSpace(partType))
            {
                return new ParseResult(false, null, "--part-costume3d-id and --part-type must be used together.");
            }

        }
        else
        {
            return new ParseResult(false, null, "Missing final pipeline operation.");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return new ParseResult(false, null, "Missing --out.");
        }

        if (partPackageProcessConcurrency < 0)
        {
            return new ParseResult(false, null, "--part-package-process-concurrency must be 0 or greater.");
        }

        if (partPackageShardCount < 1)
        {
            return new ParseResult(false, null, "--part-package-shard-count must be at least 1.");
        }

        if (partPackageShardIndex < 0 || partPackageShardIndex >= partPackageShardCount)
        {
            return new ParseResult(false, null, "--part-package-shard-index must be between 0 and shard-count - 1.");
        }

        if (!IsValidAssetStudioLogLevel(assetStudioLogLevel))
        {
            return new ParseResult(false, null, "--assetstudio-log-level must be warning, info, or debug.");
        }

        if (!IsValidPngOptimizeMode(pngOptimize))
        {
            return new ParseResult(false, null, "--png-optimize must be oxipng or off.");
        }

        if (textureCompactWorkers < 0)
        {
            return new ParseResult(false, null, "--texture-compact-workers must be 0 or greater.");
        }

        if (partPackageProcessConcurrency != 1 && partPackageShardCount > 1)
        {
            return new ParseResult(false, null, "--part-package-process-concurrency cannot be combined with manual shard options.");
        }

        if (emitPartPackages && partCostume3dId is not null &&
            (partPackageProcessConcurrency != 1 || partPackageShardCount > 1 || partPackageShardIndex != 0))
        {
            return new ParseResult(false, null, "Part package process concurrency and shards are only supported for full --emit-part-packages.");
        }

        return new ParseResult(
            true,
            new ConversionOptions(
                output,
                motion,
                masterDirectory,
                assetRoot,
                emitCostumeRegistries,
                emitPartPackages,
                emitRoleRuntimes,
                exportFaceMotion,
                partCostume3dId,
                NormalizePartType(partType),
                string.IsNullOrWhiteSpace(partUnit) ? null : partUnit,
                roleCharacter3dIds.Distinct().ToList(),
                string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath,
                string.IsNullOrWhiteSpace(manifestPath) ? null : manifestPath,
                partPackageProcessConcurrency,
                partPackageShardCount,
                partPackageShardIndex,
                string.IsNullOrWhiteSpace(partPackageClaimDirectory) ? null : partPackageClaimDirectory,
                assetStudioLogLevel.Trim().ToLowerInvariant(),
                compactTextures,
                optimizeTextureStore,
                string.IsNullOrWhiteSpace(sharedContentStore) ? null : sharedContentStore,
                string.IsNullOrWhiteSpace(compiledContentStore) ? null : compiledContentStore,
                NormalizePngOptimizeMode(pngOptimize),
                textureCompactWorkers,
                convertModelTextures,
                string.IsNullOrWhiteSpace(partPackageWorkList) ? null : partPackageWorkList,
                string.IsNullOrWhiteSpace(bundleHashIndex) ? null : bundleHashIndex
            ),
            string.Empty
        );
    }

    private static string? NormalizePartType(string? partType)
    {
        if (string.IsNullOrWhiteSpace(partType))
        {
            return null;
        }

        return partType.Trim().ToLowerInvariant() switch
        {
            "body" => "body",
            "head" => "head",
            "hair" => "hair",
            "head_optional" or "accessory" => "head_optional",
            var value => value,
        };
    }

    private static string ResolveRegistryModeName(bool emitPartPackages, bool emitRoleRuntimes)
    {
        if (emitRoleRuntimes)
        {
            return "--emit-role-runtimes";
        }
        return emitPartPackages ? "--emit-part-packages" : "--emit-costume-registries";
    }

    private static bool IsValidAssetStudioLogLevel(string value)
    {
        return value.Trim().ToLowerInvariant() is "warning" or "info" or "debug";
    }

    private static bool IsValidPngOptimizeMode(string value)
    {
        return value.Trim().ToLowerInvariant() is "oxipng" or "off";
    }

    private static string NormalizePngOptimizeMode(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static ExporterConfig LoadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<ExporterConfig>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        ) ?? throw new InvalidOperationException("config JSON is empty.");
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option {optionName} requires a value.");
        }
        index += 1;
        return args[index];
    }

    private static int ReadIntValue(string[] args, ref int index, string optionName)
    {
        var value = ReadValue(args, ref index, optionName);
        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Option {optionName} must be an integer.");
        }
        return parsed;
    }

    private static bool ReadBoolValue(string[] args, ref int index, string optionName)
    {
        var value = ReadValue(args, ref index, optionName);
        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Option {optionName} must be true or false.");
        }
        return parsed;
    }
}
