using System.Text.Json;
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

var workerParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-process-concurrency", "8",
    "--assetstudio-log-level", "debug"
});
Expect(workerParsed.IsSuccess && workerParsed.Options is not null, "worker parse succeeds");
Expect(workerParsed.Options!.PartPackageProcessConcurrency == 8, "CLI part package process concurrency parses");
Expect(workerParsed.Options!.AssetStudioLogLevel == "debug", "CLI assetstudio log level parses");

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

PartMaterialMetadataSmoke.Run();

var repoRoot = FindRepoRoot();
var partPackageExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "PartPackageExporter.cs"));
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
Expect(partPackageExporterSource.Contains("SelectRepresentativePartEntries"), "part package exporter exports each shared source package once");
Expect(partPackageExporterSource.Contains("GroupBy(entry => entry.PackagePath"), "part package exporter groups export work by package path");
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
