using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using PjskBundle2Parts.Tests;
using PjskBundle2Parts.Services;

var tempDir = Path.Combine(Path.GetTempPath(), $"haruki-exporter-config-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
var configPath = Path.Combine(tempDir, "exporter.config.json");
File.WriteAllText(configPath, JsonSerializer.Serialize(new
{
    master = "/data/master",
    assetRoot = "/data/assets",
    output = "/data/out-from-config",
    character3dId = 5,
    emitCostumeRegistries = true,
    emitPartPackages = true,
    emitRoleRuntimes = true,
    partCostume3dId = 2,
    partType = "Body",
    partUnit = "light_sound",
    roleCharacter3dIds = new[] { 5, 7 },
    manifest = "/data/manifest-from-config.json",
    assetStudioLogLevel = "info",
    runtimeJsonOutput = "both",
    compactTextures = true,
    pngOptimize = "off",
    textureCompactWorkers = 2,
    keepIntermediate = true
}));

var parsed = ConversionOptionsParser.Parse(new[]
{
    "--config", configPath,
    "--out", "/data/out-from-cli",
    "--part-type", "head_optional",
    "--role-character3d-id", "9",
    "--manifest", "/data/manifest-from-cli.json"
});

if (!parsed.IsSuccess || parsed.Options is null)
{
    throw new Exception(parsed.ErrorMessage);
}

var options = parsed.Options;
Expect(options.MasterDirectory == "/data/master", "master comes from config");
Expect(options.AssetRoot == "/data/assets", "asset root comes from config");
Expect(options.OutputDirectory == "/data/out-from-cli", "CLI output overrides config");
Expect(options.Character3dId == 5, "character3d id comes from config");
Expect(options.EmitCostumeRegistries, "emit registries comes from config");
Expect(options.EmitPartPackages, "emit part packages comes from config");
Expect(options.EmitRoleRuntimes, "emit role runtimes comes from config");
Expect(options.PartCostume3dId == 2, "part costume id comes from config");
Expect(options.PartType == "head_optional", "CLI part type overrides and normalizes config");
Expect(options.PartUnit == "light_sound", "part unit comes from config");
Expect(options.RoleCharacter3dIds.SequenceEqual(new[] { 5, 7, 9 }), "role character3d ids merge config and CLI");
Expect(options.ManifestPath == "/data/manifest-from-cli.json", "CLI manifest overrides config");
Expect(options.KeepIntermediate, "keep intermediate comes from config");
Expect(options.PartPackageProcessConcurrency == 1, "part package process concurrency defaults to single process");
Expect(options.PartPackageShardCount == 1, "part package shard count defaults to one");
Expect(options.PartPackageShardIndex == 0, "part package shard index defaults to zero");
Expect(options.AssetStudioLogLevel == "info", "assetstudio log level comes from config");
Expect(options.RuntimeJsonOutput == "both", "runtime JSON output comes from config");
Expect(options.CompactTextures, "texture compaction comes from config");
Expect(options.PngOptimizeMode == "off", "PNG optimization mode comes from config");
Expect(options.TextureCompactWorkers == 2, "texture compaction worker count comes from config");

var workerParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-process-concurrency", "8",
    "--assetstudio-log-level", "debug",
    "--runtime-json-output", "gzip",
    "--compact-textures",
    "--png-optimize", "off",
    "--texture-compact-workers", "3"
});
Expect(workerParsed.IsSuccess && workerParsed.Options is not null, "worker parse succeeds");
Expect(workerParsed.Options!.PartPackageProcessConcurrency == 8, "CLI part package process concurrency parses");
Expect(workerParsed.Options!.AssetStudioLogLevel == "debug", "CLI assetstudio log level parses");
Expect(workerParsed.Options!.RuntimeJsonOutput == "gzip", "CLI runtime JSON output parses");
Expect(workerParsed.Options!.CompactTextures, "CLI compact textures parses");
Expect(workerParsed.Options!.PngOptimizeMode == "off", "CLI PNG optimize mode parses");
Expect(workerParsed.Options!.TextureCompactWorkers == 3, "CLI texture compact workers parses");

var invalidRuntimeJsonOutputParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--runtime-json-output", "brotli"
});
Expect(!invalidRuntimeJsonOutputParsed.IsSuccess, "invalid runtime JSON output is rejected");

var invalidPngOptimizeParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--png-optimize", "webp"
});
Expect(!invalidPngOptimizeParsed.IsSuccess, "invalid PNG optimize mode is rejected");

var autoWorkerParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-process-concurrency", "0"
});
Expect(autoWorkerParsed.IsSuccess && autoWorkerParsed.Options is not null, "auto process concurrency parse succeeds for full part package export");
Expect(autoWorkerParsed.Options!.PartPackageProcessConcurrency == 0, "CLI process concurrency 0 is preserved as auto");

var invalidAutoShardParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-process-concurrency", "0",
    "--part-package-shard-count", "2",
    "--part-package-shard-index", "0"
});
Expect(!invalidAutoShardParsed.IsSuccess, "auto process concurrency cannot combine with manual shard options");

var shardParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-shard-count", "8",
    "--part-package-shard-index", "3"
});
Expect(shardParsed.IsSuccess && shardParsed.Options is not null, "shard parse succeeds");
Expect(shardParsed.Options!.PartPackageShardCount == 8, "CLI part package shard count parses");
Expect(shardParsed.Options!.PartPackageShardIndex == 3, "CLI part package shard index parses");

var invalidSingleAutoParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--part-costume3d-id", "2",
    "--part-type", "body",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-process-concurrency", "0"
});
Expect(!invalidSingleAutoParsed.IsSuccess, "auto process concurrency cannot combine with single part package export");

var allRoleParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-role-runtimes",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out"
});
Expect(allRoleParsed.IsSuccess && allRoleParsed.Options is not null, "role runtime export can default to all character3ds");
Expect(allRoleParsed.Options!.RoleCharacter3dIds.Count == 0, "empty role id list means all character3ds");

var writerDir = Path.Combine(tempDir, "writer");
var writerPath = Path.Combine(writerDir, "part-runtime.json");
RuntimeJsonWriter.Write(writerPath, new { version = "test", value = 7 }, new JsonSerializerOptions(), RuntimeJsonWriter.Gzip);
Expect(!File.Exists(writerPath), "gzip runtime JSON mode does not write plain JSON");
Expect(File.Exists(writerPath + ".gz"), "gzip runtime JSON mode writes gzip file");
using (var stream = new GZipStream(File.OpenRead(writerPath + ".gz"), CompressionMode.Decompress))
using (var document = JsonDocument.Parse(stream))
{
    Expect(document.RootElement.GetProperty("version").GetString() == "test", "gzip runtime JSON can be decompressed and parsed");
}

RuntimeJsonWriter.Write(writerPath, new { version = "both" }, new JsonSerializerOptions(), RuntimeJsonWriter.Both);
Expect(File.Exists(writerPath), "both runtime JSON mode writes plain JSON");
Expect(File.Exists(writerPath + ".gz"), "both runtime JSON mode writes gzip file");

var compactDir = Path.Combine(tempDir, "compact");
var packageA = Path.Combine(compactDir, "parts", "_sources", "body", "a");
var packageB = Path.Combine(compactDir, "parts", "_sources", "head", "b");
var packageC = Path.Combine(compactDir, "parts", "_sources", "hair", "c");
WriteRuntimePackage(packageA, "textures/body/a.png", new byte[] { 1, 2, 3, 4 });
WriteRuntimePackage(packageB, "textures/head/b.png", new byte[] { 1, 2, 3, 4 });
WriteRuntimePackage(packageC, "textures/hair/c.png", new byte[] { 9, 8, 7 });
var compactReport = new TextureCompactor().Compact(compactDir, RuntimeJsonWriter.Gzip, "off", 1);
Expect(compactReport.TextureFileCount == 3, "texture compactor scans package textures");
Expect(compactReport.UniqueHashCount == 2, "texture compactor groups by exact SHA-256");
Expect(compactReport.DuplicateFileCount == 1, "texture compactor counts duplicate files");
Expect(compactReport.SavedBytes == 4, "texture compactor saves only exact duplicate bytes with optimization off");
Expect(File.Exists(Path.Combine(compactDir, "texture-compaction-report.json")), "texture compactor writes report");
Expect(!File.Exists(Path.Combine(packageA, "textures", "body", "a.png")), "texture compactor removes package-local texture A");
Expect(!File.Exists(Path.Combine(packageB, "textures", "head", "b.png")), "texture compactor removes package-local texture B");
var rewrittenA = ReadRuntimePackage(Path.Combine(packageA, "part-runtime.json"));
var rewrittenB = ReadRuntimePackage(Path.Combine(packageB, "part-runtime.json"));
var rewrittenC = ReadRuntimePackage(Path.Combine(packageC, "part-runtime.json"));
var textureA = rewrittenA["characterTextures"]!["main"]!.GetValue<string>();
var textureB = rewrittenB["characterTextures"]!["main"]!.GetValue<string>();
var textureC = rewrittenC["characterTextures"]!["main"]!.GetValue<string>();
Expect(textureA.StartsWith("/_texture_store/sha256/"), "texture compactor rewrites texture A to root store");
Expect(textureA == textureB, "texture compactor points same-hash textures at same store path");
Expect(textureA != textureC, "texture compactor keeps different hashes separate");
Expect(rewrittenA["materialSlots"]![0]!["mainTex"]!.GetValue<string>() == textureA, "texture compactor rewrites material slot texture");
Expect(rewrittenA["textureRoles"]![0]!["uri"]!.GetValue<string>() == textureA, "texture compactor rewrites texture role URI");
Expect(File.Exists(Path.Combine(compactDir, textureA.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))), "texture compactor writes store texture");

var registryMasterDir = Path.Combine(tempDir, "registry-master");
var registryAssetRoot = Path.Combine(tempDir, "registry-assets");
Directory.CreateDirectory(registryMasterDir);
WriteJsonFile(Path.Combine(registryMasterDir, "character3ds.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "costume3ds.json"), new[]
{
    new
    {
        id = 11001,
        costume3dGroupId = 11000,
        partType = "head",
        characterId = 21,
        colorId = 2,
        colorName = "test",
        name = "legacy accessory",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "unused",
        howToObtain = "test"
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModels.json"), new[]
{
    new
    {
        costume3dId = 11001,
        unit = "light_sound",
        assetbundleName = "0019/a03",
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = "01",
        part = "a03",
        thumbnailAssetbundleName = "unused"
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "gameCharacters.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "cards.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "cardCostume3ds.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModelAvailablePatterns.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModelNotAvailablePatterns.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModelDefaultHairs.json"), Array.Empty<object>());
var legacyAccessory = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "character",
    "head_optional",
    "0019",
    "a03.bundle"
);
var legacyAccessoryColor = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "character",
    "color_variation",
    "head_optional",
    "0019",
    "a03",
    "01.bundle"
);
Directory.CreateDirectory(Path.GetDirectoryName(legacyAccessory)!);
Directory.CreateDirectory(Path.GetDirectoryName(legacyAccessoryColor)!);
File.WriteAllBytes(legacyAccessory, new byte[] { 1 });
File.WriteAllBytes(legacyAccessoryColor, new byte[] { 2 });
var registryExport = new CostumeRegistryExporter().ExportInMemory(registryMasterDir, registryAssetRoot);
var legacyAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001);
Expect(legacyAccessoryEntry.PartType == "head_optional", "head_only registry rows are exported as head_optional");
Expect(legacyAccessoryEntry.BundlePath == legacyAccessory, "head_optional registry resolves legacy character base bundle");
Expect(legacyAccessoryEntry.ColorVariationBundlePath == legacyAccessoryColor, "head_optional registry resolves legacy character color variation bundle");
Expect(legacyAccessoryEntry.PackagePath.StartsWith("parts/_sources/head_optional/"), "head_optional registry writes shared source package path");

PartMaterialMetadataSmoke.Run();

var repoRoot = FindRepoRoot();
var partPackageExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "PartPackageExporter.cs"));
var roleRuntimeExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "RoleRuntimeExporter.cs"));
Expect(partPackageExporterSource.Contains("name.Contains(\"eyelash\")"), "part package exporter classifies eyelash separately");
Expect(partPackageExporterSource.Contains("return \"eyelash\""), "part package exporter returns eyelash material kind");
Expect(partPackageExporterSource.Contains("name.Contains(\"eyebrow\")"), "part package exporter classifies eyebrow separately");
Expect(partPackageExporterSource.Contains("return \"eyebrow\""), "part package exporter returns eyebrow material kind");
Expect(partPackageExporterSource.Contains("name.Contains(\"_acc_\")"), "part package exporter classifies head acc materials as accessory");
Expect(partPackageExporterSource.Contains("\"eyelash\" or \"eyebrow\""), "part package exporter uses full-runtime render order for face detail layers");
Expect(partPackageExporterSource.Contains("BuildDeferredColliderFlagBindings"), "part package exporter preserves deferred head colliderFlag bindings");
Expect(partPackageExporterSource.Contains("deferred_body_colliderFlag"), "part package exporter labels head colliderFlag bindings as deferred to viewer composer");
Expect(partPackageExporterSource.Contains("ResolveColliderFlagPrefixes"), "part package exporter resolves colliderFlag matched prefixes for viewer rebinding");
Expect(partPackageExporterSource.Contains("MatchedPrefixes: prefixes"), "part package exporter writes colliderFlag matched prefixes for viewer rebinding");
Expect(partPackageExporterSource.Contains("IsSumOfForcesOnBone: ReadBool(manager.Raw, \"isSumOfForcesOnBone\", defaultValue: true)"), "part package exporter defaults SpringManager force summing on like full runtime export");
Expect(partPackageExporterSource.Contains("RawAngleLimits: new VrmSpringBoneAngleLimitsCandidate("), "part package exporter preserves per-bone angle limits");
Expect(partPackageExporterSource.Contains("Y: ReadAxisLimit(bone.Raw, \"yAngleLimits\")"), "part package exporter reads y angle limits from SpringBone raw data");
Expect(partPackageExporterSource.Contains("Z: ReadAxisLimit(bone.Raw, \"zAngleLimits\")"), "part package exporter reads z angle limits from SpringBone raw data");
Expect(partPackageExporterSource.Contains("ReadOptionalBool(axis, \"active\") ??"), "part package exporter reads explicit angle limit active flags");
Expect(partPackageExporterSource.Contains("ReadOptionalBool(axis, \"m_Enabled\") ??"), "part package exporter reads Unity enabled angle limit flags");
Expect(partPackageExporterSource.Contains("                true,"), "part package exporter defaults present angle limits to active like full runtime output");

var costumeRegistryModelsSource = File.ReadAllText(Path.Combine(repoRoot, "Models", "CostumeRegistryModels.cs"));
var costumeRegistryExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "CostumeRegistryExporter.cs"));
Expect(costumeRegistryModelsSource.Contains("headCompositionKind"), "head-hair compatibility rules expose composition kind");
Expect(costumeRegistryModelsSource.Contains("activeContributors"), "head-hair compatibility rules expose active contributors");
Expect(costumeRegistryModelsSource.Contains("PartSourceMap"), "costume registry exposes part source map");
Expect(costumeRegistryModelsSource.Contains("baseSourceKey"), "part registry entries expose base source keys");
Expect(costumeRegistryModelsSource.Contains("sourcePackagePath"), "part registry entries expose shared source package paths");
Expect(costumeRegistryExporterSource.Contains("ResolveHeadHairComposition"), "registry exporter resolves head-hair composition metadata");
Expect(costumeRegistryExporterSource.Contains("complete_head"), "registry exporter marks complete head compositions");
Expect(costumeRegistryExporterSource.Contains("part-source-map.json"), "registry exporter writes part source map");
Expect(costumeRegistryExporterSource.Contains("BuildSourceIdentity"), "registry exporter builds source identities");
Expect(costumeRegistryExporterSource.Contains("SHA256.HashData"), "registry exporter uses stable source key hashes");
Expect(costumeRegistryExporterSource.Contains("parts/_sources/"), "registry exporter points duplicate part ids at shared source package paths");
Expect(costumeRegistryExporterSource.Contains("ResolveAssetBaseDirectoryCandidates"), "registry exporter checks v2, legacy, and flat asset roots");
Expect(costumeRegistryExporterSource.Contains("\"model\", \"character\""), "registry exporter includes legacy character model fallback roots");
Expect(partPackageExporterSource.Contains("SelectRepresentativePartEntries"), "part package exporter exports each shared source package once");
Expect(partPackageExporterSource.Contains("GroupBy(entry => entry.PackagePath"), "part package exporter groups export work by package path");
Expect(roleRuntimeExporterSource.Contains("ResolveDefaultCostumeSettingMotionPath"), "role runtime exporter auto-resolves costume_setting motion bundles");
Expect(roleRuntimeExporterSource.Contains("LoadAllCharacter3dIds"), "role runtime exporter can export every character3d role by default");
Expect(roleRuntimeExporterSource.Contains("character\", \"motion\", \"costume_setting\""), "role runtime exporter scans character motion costume_setting directory");
Expect(roleRuntimeExporterSource.Contains("\"light_sound\" => 27"), "role runtime exporter maps Leo/need Miku motion to 27_00");
Expect(roleRuntimeExporterSource.Contains("\"idol\" => 28"), "role runtime exporter maps idol Miku motion to 28_00");
Expect(roleRuntimeExporterSource.Contains("\"street\" => 29"), "role runtime exporter maps street Miku motion to 29_00");
Expect(roleRuntimeExporterSource.Contains("\"theme_park\" => 30"), "role runtime exporter maps theme park Miku motion to 30_00");
Expect(roleRuntimeExporterSource.Contains("\"school_refusal\" => 31"), "role runtime exporter maps school refusal Miku motion to 31_00");
Expect(roleRuntimeExporterSource.Contains("_ => 21"), "role runtime exporter keeps piapro/default Miku motion at 21_00");
var inventoryModelsSource = File.ReadAllText(Path.Combine(repoRoot, "Models", "InventoryModels.cs"));
var assetStudioBundleParserSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "AssetStudioBundleParser.cs"));
Expect(inventoryModelsSource.Contains("RenderMaterialSlotInventory"), "inventory records renderer material slots with identity");
Expect(inventoryModelsSource.Contains("MaterialKey"), "inventory exposes material identity keys");
Expect(assetStudioBundleParserSource.Contains("m_FileID"), "bundle parser preserves renderer material file ids");
Expect(assetStudioBundleParserSource.Contains("m_PathID"), "bundle parser preserves renderer material path ids");
Expect(partPackageExporterSource.Contains("MaterialIdentityLookup"), "part package exporter resolves materials by identity");
Expect(!partPackageExporterSource.Contains("BuildMaterialMap"), "part package exporter no longer indexes materials by display name");
Expect(partPackageExporterSource.Contains("part-export-error.json"), "part package exporter writes per-package errors during full export");
Expect(partPackageExporterSource.Contains("Part package export skipped"), "part package exporter continues after per-package export failures");
Expect(partPackageExporterSource.Contains("IsInShard"), "part package exporter can filter deterministic shards");
Expect(partPackageExporterSource.Contains("public static void Merge"), "part package exporter can merge worker manifests");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

static string FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Services", "PartPackageExporter.cs")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
    }
    throw new DirectoryNotFoundException("Could not locate Haruki-3D-Exporter repo root.");
}

static void WriteRuntimePackage(string packageDirectory, string texturePath, byte[] textureBytes)
{
    var textureFile = Path.Combine(packageDirectory, texturePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(textureFile)!);
    File.WriteAllBytes(textureFile, textureBytes);
    RuntimeJsonWriter.Write(
        Path.Combine(packageDirectory, "part-runtime.json"),
        new
        {
            characterTextures = new Dictionary<string, string>
            {
                ["main"] = texturePath
            },
            materialSlots = new[]
            {
                new
                {
                    mainTex = texturePath,
                    shadowTex = (string?)null,
                    valueTex = (string?)null,
                    faceShadowTex = (string?)null
                }
            },
            textureRoles = new[]
            {
                new
                {
                    part = "body",
                    materialKey = "0:1",
                    materialFileId = 0,
                    materialPathId = 1,
                    materialName = "mat",
                    materialKind = "body",
                    role = "main",
                    uri = texturePath
                }
            }
        },
        new JsonSerializerOptions(),
        RuntimeJsonWriter.Gzip
    );
}

static void WriteJsonFile(string path, object payload)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(payload));
}

static JsonObject ReadRuntimePackage(string runtimeJsonPath)
{
    using var stream = new GZipStream(File.OpenRead(runtimeJsonPath + ".gz"), CompressionMode.Decompress);
    return JsonNode.Parse(stream)!.AsObject();
}
