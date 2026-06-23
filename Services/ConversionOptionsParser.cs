using PjskBundle2Parts.Models;
using System.Text.Json;

namespace PjskBundle2Parts.Services;

public static class ConversionOptionsParser
{
    public static string Usage =>
        "Usage:\n" +
        "  Haruki-3D-Exporter --body <path> --head <path> --out <directory> [--master <master-directory>] [--motion <bundle-or-export-folder>] [--head-root <name>] [--keep-intermediate]\n" +
        "  Haruki-3D-Exporter --character3d-id <id> --master <master-directory> --asset-root <AssetBundles-root> --out <directory> [--motion <bundle-or-export-folder>] [--keep-intermediate]\n" +
        "  Haruki-3D-Exporter --emit-costume-registries --master <master-directory> --asset-root <AssetBundles-root> --out <directory>\n" +
        "  Haruki-3D-Exporter --emit-part-packages --part-costume3d-id <id> --part-type <body|head|hair|head_optional> --master <master-directory> --asset-root <AssetBundles-root> --out <directory> [--part-unit <unit>]\n\n" +
        "  Haruki-3D-Exporter --export-face-motion --motion <bundle-or-decoded-folder-or-json> --out <face_motion.json-or-directory> [--source-path <bundle-path>]\n\n" +
        "  Add --config <json> to load defaults from haruki-3d-exporter.config.json.\n\n" +
        "Notes:\n" +
        "  --body accepts either a bundle file or a body directory like .../body/05/0001\n" +
        "  --head accepts either a bundle file or a head directory like .../face/05\n" +
        "  --master reads gameCharacters.json for character heights; with --character3d-id it also resolves character3ds.json and costume3dModels.json\n" +
        "  --character3d-id resolves body/hair/head from gameCharacters.json, character3ds.json, and costume3dModels.json\n" +
        "  --asset-root points at the AssetBundles root containing live_pv/model/characterv2\n" +
        "  --emit-costume-registries writes character3d-index.json, parts/part-registry.json, parts/head-hair-compatibility.json, and parts/card-costume-unlocks.json\n" +
        "  --emit-part-packages writes one parts/<partType>/<costume3dId>/<unit>/part-runtime.json for runtime custom assembly\n" +
        "  --manifest records part package input file stamps for incremental --emit-part-packages runs\n" +
        "  --export-face-motion writes face_motion.json from a costume_setting bundle or decoded AnimationClip JSON without Python helpers\n" +
        "  --motion accepts a costume_setting bundle or a folder containing unity-motion.json/face_motion.json/light_motion.json\n" +
        "  --head-root selects a specific root GameObject from the head bundle, for example face or mdl_chr_IDL_A_00\n" +
        "  lean output is the default; use --keep-intermediate to keep diagnostic manifests, inventories, and reports";

    public static ParseResult Parse(string[] args)
    {
        string? body = null;
        string? head = null;
        string? output = null;
        string? motion = null;
        string? headRoot = null;
        string? masterDirectory = null;
        string? assetRoot = null;
        int? character3dId = null;
        var keepIntermediate = false;
        var emitCostumeRegistries = false;
        var emitPartPackages = false;
        var exportFaceMotion = false;
        int? partCostume3dId = null;
        string? partType = null;
        string? partUnit = null;
        string? sourcePath = null;
        string? manifestPath = null;
        string? configPath = null;

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
                body = config.Body;
                head = config.Head;
                output = config.Output;
                motion = config.Motion;
                headRoot = config.HeadRoot;
                masterDirectory = config.Master;
                assetRoot = config.AssetRoot;
                character3dId = config.Character3dId;
                keepIntermediate = config.KeepIntermediate ?? false;
                emitCostumeRegistries = config.EmitCostumeRegistries ?? false;
                emitPartPackages = config.EmitPartPackages ?? false;
                exportFaceMotion = config.ExportFaceMotion ?? false;
                partCostume3dId = config.PartCostume3dId;
                partType = config.PartType;
                partUnit = config.PartUnit;
                sourcePath = config.SourcePath;
                manifestPath = config.Manifest;
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

            if (arg is "--body" or "-b")
            {
                body = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--head" or "-h")
            {
                head = ReadValue(args, ref i, arg);
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

            if (arg is "--character3d-id")
            {
                var value = ReadValue(args, ref i, arg);
                if (!int.TryParse(value, out var parsed))
                {
                    return new ParseResult(false, null, $"Option {arg} must be an integer.");
                }
                character3dId = parsed;
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

            if (arg is "--head-root")
            {
                headRoot = ReadValue(args, ref i, arg);
                continue;
            }

            if (arg is "--keep-intermediate")
            {
                keepIntermediate = true;
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
        else if (emitCostumeRegistries || emitPartPackages)
        {
            if (string.IsNullOrWhiteSpace(masterDirectory))
            {
                return new ParseResult(false, null, $"Missing --master for {(emitPartPackages ? "--emit-part-packages" : "--emit-costume-registries")}.");
            }

            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                return new ParseResult(false, null, $"Missing --asset-root for {(emitPartPackages ? "--emit-part-packages" : "--emit-costume-registries")}.");
            }

            if (emitPartPackages && !emitCostumeRegistries && (partCostume3dId is null) != string.IsNullOrWhiteSpace(partType))
            {
                return new ParseResult(false, null, "--part-costume3d-id and --part-type must be used together.");
            }
        }
        else if (character3dId is not null)
        {
            if (string.IsNullOrWhiteSpace(masterDirectory))
            {
                return new ParseResult(false, null, "Missing --master for --character3d-id.");
            }

            if (string.IsNullOrWhiteSpace(assetRoot))
            {
                return new ParseResult(false, null, "Missing --asset-root for --character3d-id.");
            }
        }
        else if (string.IsNullOrWhiteSpace(body))
        {
            return new ParseResult(false, null, "Missing --body.");
        }

        if (!exportFaceMotion &&
            !emitCostumeRegistries &&
            !emitPartPackages &&
            character3dId is null &&
            string.IsNullOrWhiteSpace(head))
        {
            return new ParseResult(false, null, "Missing --head.");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return new ParseResult(false, null, "Missing --out.");
        }

        return new ParseResult(
            true,
            new ConversionOptions(
                body,
                head,
                output,
                motion,
                headRoot,
                keepIntermediate,
                character3dId,
                masterDirectory,
                assetRoot,
                emitCostumeRegistries,
                emitPartPackages,
                exportFaceMotion,
                partCostume3dId,
                NormalizePartType(partType),
                string.IsNullOrWhiteSpace(partUnit) ? null : partUnit,
                string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath,
                string.IsNullOrWhiteSpace(manifestPath) ? null : manifestPath
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
}
