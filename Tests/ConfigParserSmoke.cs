using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO.Compression;
using System.Diagnostics;
using PjskBundle2Parts.Tests;
using PjskBundle2Parts.Models;
using PjskBundle2Parts.Services;

if (args is ["--compiled-cache-copy-race-worker", var sourcePath, var targetPath, var workerStartGate])
{
    while (!File.Exists(workerStartGate))
    {
        Thread.Sleep(1);
    }
    try
    {
        for (var index = 0; index < 32; index++)
        {
            ContentAddressedFile.Replace(targetPath, temporaryPath => File.Copy(sourcePath, temporaryPath));
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 2;
    }
    return;
}

var tempDir = Path.Combine(Path.GetTempPath(), $"haruki-exporter-config-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
var configPath = Path.Combine(tempDir, "exporter.config.json");
File.WriteAllText(configPath, JsonSerializer.Serialize(new
{
    master = "/data/master",
    assetRoot = "/data/assets",
    output = "/data/out-from-config",
    emitCostumeRegistries = true,
    emitPartPackages = true,
    emitRoleRuntimes = true,
    partCostume3dId = 2,
    partType = "Body",
    partUnit = "light_sound",
    roleCharacter3dIds = new[] { 5, 7 },
    manifest = "/data/manifest-from-config.json",
    assetStudioLogLevel = "info",
    compactTextures = true,
    optimizeTextureStore = false,
    sharedContentStore = "/data/shared-cas-from-config",
    compiledContentStore = "/data/compiled-cas-from-config",
    pngOptimize = "off",
    textureCompactWorkers = 2,
    convertModelTextures = true
}));

var parsed = ConversionOptionsParser.Parse(new[]
{
    "--config", configPath,
    "--out", "/data/out-from-cli",
    "--part-type", "head_optional",
    "--role-character3d-id", "9",
    "--manifest", "/data/manifest-from-cli.json",
    "--shared-content-store", "/data/shared-cas-from-cli",
    "--compiled-content-store", "/data/compiled-cas-from-cli",
    "--part-package-work-list", "/data/work-list.json",
    "--bundle-hash-index", "/data/bundle-hashes.json"
});

if (!parsed.IsSuccess || parsed.Options is null)
{
    throw new Exception(parsed.ErrorMessage);
}

var options = parsed.Options;
Expect(options.MasterDirectory == "/data/master", "master comes from config");
Expect(options.AssetRoot == "/data/assets", "asset root comes from config");
Expect(options.OutputDirectory == "/data/out-from-cli", "CLI output overrides config");
Expect(options.EmitCostumeRegistries, "emit registries comes from config");
Expect(options.EmitPartPackages, "emit part packages comes from config");
Expect(options.EmitRoleRuntimes, "emit role runtimes comes from config");
Expect(options.PartCostume3dId == 2, "part costume id comes from config");
Expect(options.PartType == "head_optional", "CLI part type overrides and normalizes config");
Expect(options.PartUnit == "light_sound", "part unit comes from config");
Expect(options.RoleCharacter3dIds.SequenceEqual(new[] { 5, 7, 9 }), "role character3d ids merge config and CLI");
Expect(options.ManifestPath == "/data/manifest-from-cli.json", "CLI manifest overrides config");
Expect(options.PartPackageProcessConcurrency == 1, "part package process concurrency defaults to single process");
Expect(options.PartPackageShardCount == 1, "part package shard count defaults to one");
Expect(options.PartPackageShardIndex == 0, "part package shard index defaults to zero");
Expect(options.AssetStudioLogLevel == "info", "assetstudio log level comes from config");
Expect(options.CompactTextures, "texture compaction comes from config");
Expect(!options.OptimizeTextureStore, "standalone texture optimization comes from config");
Expect(options.SharedContentStore == "/data/shared-cas-from-cli", "CLI shared content store overrides config");
Expect(options.CompiledContentStore == "/data/compiled-cas-from-cli", "CLI compiled content store parses");
Expect(options.PngOptimizeMode == "off", "PNG optimization mode comes from config");
Expect(options.TextureCompactWorkers == 2, "texture compaction worker count comes from config");
Expect(options.ConvertModelTextures, "model texture conversion comes from config");
Expect(options.PartPackageWorkList == "/data/work-list.json", "part package work list parses");
Expect(!options.OwnsOutputFinalization, "part package work-list workers do not own output finalization");
Expect(options.BundleHashIndex == "/data/bundle-hashes.json", "bundle hash index parses");

var hashAssetRoot = Path.Combine(tempDir, "hash-assets");
var indexedBundle = Path.Combine(hashAssetRoot, "live_pv", "model", "body.bundle");
Directory.CreateDirectory(Path.GetDirectoryName(indexedBundle)!);
File.WriteAllBytes(indexedBundle, new byte[] { 1, 2, 3 });
var expectedBundleHash = new string('a', 64);
var hashIndexPath = Path.Combine(hashAssetRoot, ".haruki-bundle-sha256.json");
File.WriteAllText(hashIndexPath, JsonSerializer.Serialize(new Dictionary<string, string>
{
    ["live_pv/model/body.bundle"] = expectedBundleHash,
    ["invalid.bundle"] = "not-a-hash",
}));
var hashIndex = new BundleHashIndex(hashIndexPath);
Expect(hashIndex.TryGet(hashAssetRoot, indexedBundle, out var indexedHash),
    "bundle hash index resolves exporter-relative path");
Expect(Convert.ToHexString(indexedHash).ToLowerInvariant() == expectedBundleHash,
    "bundle hash index decodes SHA-256 bytes");
Expect(!hashIndex.TryGet(hashAssetRoot, Path.Combine(hashAssetRoot, "invalid.bundle"), out _),
    "bundle hash index ignores invalid digests");
var corruptHashIndexPath = Path.Combine(hashAssetRoot, "corrupt.json");
File.WriteAllText(corruptHashIndexPath, "{");
Expect(!new BundleHashIndex(corruptHashIndexPath).TryGet(hashAssetRoot, indexedBundle, out _),
    "corrupt bundle hash index safely falls back to file hashing");

var plannerRoot = Path.Combine(tempDir, "work-planner");
Directory.CreateDirectory(plannerRoot);
var plannerEntries = new[]
{
    PartEntry(plannerRoot, "heavy-a", 900, "source-a"),
    PartEntry(plannerRoot, "heavy-b", 700, "source-b"),
    PartEntry(plannerRoot, "medium", 400, "source-c"),
    PartEntry(plannerRoot, "small", 100, "source-d"),
    PartEntry(plannerRoot, "small-alias", 100, "source-d", packagePath: "parts/body/small"),
};
var planned = PartPackageWorkPlanner.Plan(plannerEntries, 2);
var plannedAgain = PartPackageWorkPlanner.Plan(plannerEntries, 2);
Expect(planned.SelectMany(worker => worker).Select(entry => entry.PackagePath).Distinct().Count() == 4,
    "work planner emits one representative per package");
Expect(JsonSerializer.Serialize(planned) == JsonSerializer.Serialize(plannedAgain),
    "work planner is deterministic");
var plannedWeights = planned.Select(worker => worker.Sum(entry => new FileInfo(entry.BundlePath!).Length)).ToArray();
Expect(plannedWeights.Max() - plannedWeights.Min() <= 200,
    "work planner balances heavy source groups");
var serializedWorkListPath = Path.Combine(plannerRoot, "worker.json");
File.WriteAllText(serializedWorkListPath, JsonSerializer.Serialize(new PartPackageWorkList(
    new Dictionary<string, float> { ["5"] = 1.56f },
    planned[0]
)));
var serializedWorkList = PartPackageWorkPlanner.Load(serializedWorkListPath);
Expect(serializedWorkList.CharacterHeightMetersById["5"] == 1.56f,
    "worker list carries parent-built character heights");
Expect(serializedWorkList.Entries.Count == planned[0].Count,
    "worker list round trips planned entries");

var booleanOverride = ConversionOptionsParser.Parse(new[]
{
    "--config", configPath,
    "--convert-model-textures", "false"
});
Expect(booleanOverride.IsSuccess && booleanOverride.Options is not null, "model texture CLI override parses");
Expect(!booleanOverride.Options!.ConvertModelTextures, "model texture CLI override wins over config");

var dependencyRoot = Path.Combine(tempDir, "dependencies", "face", "11");
Directory.CreateDirectory(dependencyRoot);
foreach (var fileName in new[] { "0403.bundle", "0403a.bundle", "0403b.bundle", "0403c.bundle", "0403n.bundle", "0509.bundle" })
{
    File.WriteAllText(Path.Combine(dependencyRoot, fileName), fileName);
}
var headInput = new ResolvedBundleInput(
    BundlePartKind.Head,
    Path.Combine(dependencyRoot, "0403c.bundle"),
    Path.Combine(dependencyRoot, "0403c.bundle"),
    "11",
    "0403c"
);
var headDependencies = BundleDependencyResolver.ResolveLoadBundlePaths(headInput)
    .Select(Path.GetFileName)
    .ToArray();
Expect(
    headDependencies.SequenceEqual(new[] { "0403c.bundle", "0403.bundle", "0403a.bundle", "0403b.bundle", "0403n.bundle" }),
    "head dependency resolver loads primary bundle and same numeric family only"
);
var fullHeadDependencies = BundleDependencyResolver.ResolveLoadBundlePaths(headInput, BundleLoadDependencyMode.FullDirectory)
    .Select(Path.GetFileName)
    .ToArray();
Expect(fullHeadDependencies.Contains("0509.bundle"), "full-directory dependency resolver includes unrelated sibling bundles");

var headAllRoot = Path.Combine(tempDir, "dependencies", "face", "12");
Directory.CreateDirectory(headAllRoot);
foreach (var fileName in new[] { "0001.bundle", "0001_head_all.bundle", "0001_mc.bundle", "0101.bundle" })
{
    File.WriteAllText(Path.Combine(headAllRoot, fileName), fileName);
}
var headAllInput = new ResolvedBundleInput(
    BundlePartKind.Head,
    Path.Combine(headAllRoot, "0001_head_all.bundle"),
    Path.Combine(headAllRoot, "0001_head_all.bundle"),
    "12",
    "0001_head_all"
);
var headAllDependencies = BundleDependencyResolver.ResolveLoadBundlePaths(headAllInput)
    .Select(Path.GetFileName)
    .ToArray();
Expect(
    headAllDependencies.SequenceEqual(new[] { "0001_head_all.bundle", "0001.bundle", "0001_mc.bundle" }),
    "head dependency resolver loads underscore siblings for head_all bundles"
);

var workerParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-process-concurrency", "8",
    "--assetstudio-log-level", "debug",
    "--compact-textures",
    "--png-optimize", "off",
    "--texture-compact-workers", "3"
});
Expect(workerParsed.IsSuccess && workerParsed.Options is not null, "worker parse succeeds");
Expect(workerParsed.Options!.PartPackageProcessConcurrency == 8, "CLI part package process concurrency parses");
Expect(workerParsed.Options!.AssetStudioLogLevel == "debug", "CLI assetstudio log level parses");
Expect(workerParsed.Options!.CompactTextures, "CLI compact textures parses");
Expect(workerParsed.Options!.PngOptimizeMode == "off", "CLI PNG optimize mode parses");
Expect(workerParsed.Options!.TextureCompactWorkers == 3, "CLI texture compact workers parses");

var optimizeStoreParsed = ConversionOptionsParser.Parse(new[]
{
    "--optimize-texture-store",
    "--out", "/data/out",
    "--png-optimize", "off",
    "--texture-compact-workers", "2",
});
Expect(optimizeStoreParsed.IsSuccess && optimizeStoreParsed.Options!.OptimizeTextureStore, "standalone texture store optimization parses without asset inputs");

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

var claimDirectory = Path.Combine(tempDir, "claims");
var claimParsed = ConversionOptionsParser.Parse(new[]
{
    "--emit-part-packages",
    "--master", "/data/master",
    "--asset-root", "/data/assets",
    "--out", "/data/out",
    "--part-package-claim-directory", claimDirectory,
});
Expect(claimParsed.IsSuccess && claimParsed.Options?.PartPackageClaimDirectory == claimDirectory, "dynamic package claim directory parses");
var claims = new PartPackageWorkClaims(claimDirectory);
Expect(claims.TryClaim("parts/_sources/body/a"), "first worker claims a package");
Expect(!claims.TryClaim("parts/_sources/body/a"), "a package can only be claimed by one worker");

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
RuntimeJsonWriter.Write(
    writerPath,
    new
    {
        version = "msgpack",
        value = 7,
        nested = new { ok = true },
        items = new object?[] { "a", 2, null }
    },
    new JsonSerializerOptions()
);
var writerMessagePackPath = RuntimeJsonWriter.MessagePackBrotliPath(writerPath);
Expect(!File.Exists(writerPath), "msgpack-br runtime JSON mode does not write plain JSON");
Expect(!File.Exists(writerPath + ".gz"), "msgpack-br runtime JSON mode does not write gzip");
Expect(File.Exists(writerMessagePackPath), "msgpack-br runtime JSON mode writes MessagePack Brotli file");
using (var document = RuntimeJsonWriter.ReadJsonDocument(writerPath))
{
    Expect(document.RootElement.GetProperty("version").GetString() == "msgpack", "msgpack-br runtime JSON can be decoded and parsed");
    Expect(document.RootElement.GetProperty("nested").GetProperty("ok").GetBoolean(), "msgpack-br runtime JSON preserves nested objects");
    Expect(document.RootElement.GetProperty("items").GetArrayLength() == 3, "msgpack-br runtime JSON preserves arrays");
}
Expect(RuntimeJsonWriter.PrimaryPath(writerPath) == writerMessagePackPath, "runtime primary path points at .msgpack.br");
Expect(RuntimeJsonWriter.PrimaryPath(writerMessagePackPath) == writerMessagePackPath, "runtime primary path keeps final .msgpack.br paths unchanged");
Expect(RuntimeJsonWriter.DefaultBrotliQuality == 6, "runtime MessagePack defaults to Brotli quality 6");

var raceRoot = Path.Combine(writerDir, "compiled-cache-race");
var raceStore = Path.Combine(writerDir, "compiled-cache-race-store");
var raceSource = Path.Combine(writerDir, "compiled-cache-source.msgpack.br");
var startGate = Path.Combine(writerDir, "shared-core.start");
var raceBytes = Enumerable.Range(0, 256 * 1024).Select(index => (byte)(index % 251)).ToArray();
File.WriteAllBytes(raceSource, raceBytes);
var raceTargets = Enumerable.Range(0, 48).Select(index =>
{
    var target = Path.Combine(raceRoot, "parts", index.ToString(), "part-runtime-core.msgpack.br");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, raceBytes);
    return target;
}).ToArray();
new ContentAddressedStore().Compact(raceRoot, raceStore);
var raceWorkers = raceTargets.Select(target =>
{
    var startInfo = new ProcessStartInfo(Environment.ProcessPath!);
    startInfo.ArgumentList.Add("--compiled-cache-copy-race-worker");
    startInfo.ArgumentList.Add(raceSource);
    startInfo.ArgumentList.Add(target);
    startInfo.ArgumentList.Add(startGate);
    return Process.Start(startInfo) ?? throw new Exception("failed to start runtime writer race worker");
}).ToArray();
File.WriteAllText(startGate, "start");
foreach (var worker in raceWorkers)
{
    worker.WaitForExit();
    Expect(worker.ExitCode == 0, "parallel processes can publish the same immutable runtime file");
    worker.Dispose();
}
foreach (var target in raceTargets)
{
    Expect(File.ReadAllBytes(target).SequenceEqual(raceBytes), "parallel compiled-cache publication leaves valid content");
}

var directWriterPath = Path.Combine(writerDir, "direct-writer.json");
RuntimeJsonWriter.Write(
    directWriterPath,
    new DirectWriterFixture("direct", DirectWriterState.Ready, null),
    new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    }
);
using (var document = RuntimeJsonWriter.ReadJsonDocument(directWriterPath))
{
    Expect(document.RootElement.GetProperty("displayName").GetString() == "direct", "direct MessagePack writer honors JSON property naming");
    Expect(document.RootElement.GetProperty("state").GetString() == "Ready", "direct MessagePack writer honors string enums");
    Expect(!document.RootElement.TryGetProperty("optional", out _), "direct MessagePack writer honors null ignore conditions");
}

var binaryWriterPath = Path.Combine(writerDir, "binary-arrays.json");
var binaryPositions = Enumerable.Range(0, 12).Select(index => index / 10f).ToArray();
var binaryIndices = Enumerable.Range(0, 20).Select(index => index == 19 ? 70_000 : index * 2).ToArray();
RuntimeJsonWriter.Write(
    binaryWriterPath,
    new
    {
        nativeMeshes = new
        {
            meshes = new[]
            {
                new
                {
                    positions = binaryPositions,
                    skinIndices = Enumerable.Range(0, 20).ToArray(),
                    submeshes = new[] { new { indices = binaryIndices } }
                }
            }
        },
        gravityDir = new[] { 0f, -1f, 0f }
    },
    new JsonSerializerOptions(),
    binaryArraySchema: RuntimeBinaryArraySchema.PartRuntime
);
var binaryMessagePack = ReadBrotliBytes(RuntimeJsonWriter.MessagePackBrotliPath(binaryWriterPath));
Expect(ContainsRuntimeBinaryExtension(binaryMessagePack), "runtime mesh arrays are emitted as MessagePack binary extensions");
using (var document = RuntimeJsonWriter.ReadJsonDocument(binaryWriterPath))
{
    var mesh = document.RootElement.GetProperty("nativeMeshes").GetProperty("meshes")[0];
    var positions = mesh.GetProperty("positions").EnumerateArray().Select(item => item.GetSingle()).ToArray();
    var indices = mesh.GetProperty("submeshes")[0].GetProperty("indices").EnumerateArray().Select(item => item.GetInt32()).ToArray();
    var skinIndices = mesh.GetProperty("skinIndices").EnumerateArray().Select(item => item.GetInt32()).ToArray();
    Expect(positions.SequenceEqual(binaryPositions), "binary float32 arrays round-trip exact source float values");
    Expect(indices.SequenceEqual(binaryIndices), "binary index arrays round-trip exact integer values");
    Expect(skinIndices.SequenceEqual(Enumerable.Range(0, 20)), "binary uint16 arrays round-trip exact integer values");
    Expect(document.RootElement.GetProperty("gravityDir").GetArrayLength() == 3, "small semantic vectors remain ordinary arrays");
}

var genericValuesPath = Path.Combine(writerDir, "generic-values.json");
RuntimeJsonWriter.Write(
    genericValuesPath,
    new { values = Enumerable.Range(0, 20).Select(index => (double)index).ToArray() },
    new JsonSerializerOptions()
);
Expect(
    !ContainsRuntimeBinaryExtension(ReadBrotliBytes(RuntimeJsonWriter.MessagePackBrotliPath(genericValuesPath))),
    "generic JSON properties are not binary-encoded by name alone"
);

var directTextureRoot = Path.Combine(tempDir, "direct-texture-store");
var directTextureStore = new RuntimeTextureStore(directTextureRoot);
var directTextureBytes = new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 };
var directTexturePath = directTextureStore.StorePng(directTextureBytes);
Expect(directTexturePath.StartsWith("/_texture_store/sha256/", StringComparison.Ordinal), "direct texture store returns a root-relative CAS URI");
Expect(directTextureStore.StorePng(directTextureBytes) == directTexturePath, "direct texture store reuses exact texture bytes");
Expect(Directory.EnumerateFiles(directTextureRoot, "*.png", SearchOption.AllDirectories).Count() == 1, "direct texture store writes one file per exact texture hash");

var concurrentPublishRoot = Path.Combine(tempDir, "concurrent-content-publish");
var concurrentPublishSource = Path.Combine(concurrentPublishRoot, "source.png");
var concurrentPublishTarget = Path.Combine(concurrentPublishRoot, "store", "texture.png");
Directory.CreateDirectory(concurrentPublishRoot);
File.WriteAllBytes(concurrentPublishSource, directTextureBytes);
var concurrentPublishHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(directTextureBytes)).ToLowerInvariant();
const int concurrentPublishers = 8;
using (var publishersReady = new CountdownEvent(concurrentPublishers))
{
    var publishTasks = Enumerable.Range(0, concurrentPublishers)
        .Select(_ => Task.Factory.StartNew(
            () => ContentAddressedFile.Ensure(
                concurrentPublishTarget,
                concurrentPublishHash,
                temporaryPath =>
                {
                    File.Copy(concurrentPublishSource, temporaryPath);
                    publishersReady.Signal();
                    publishersReady.Wait();
                }
            ),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ))
        .ToArray();
    Task.WaitAll(publishTasks);
}
Expect(File.ReadAllBytes(concurrentPublishTarget).SequenceEqual(directTextureBytes),
    "concurrent exact-content publishers converge on one valid file");
Expect(!Directory.EnumerateFiles(Path.GetDirectoryName(concurrentPublishTarget)!, "*.tmp").Any(),
    "concurrent content publishing cleans temporary files");

var storeOptimization = new TextureCompactor().OptimizeStore(
    directTextureRoot,
    "off",
    2
);
Expect(storeOptimization.TextureFileCount == 1 && storeOptimization.OptimizedFileCount == 0, "standalone texture optimizer scans the direct store without rewriting in off mode");
var resolveTexturePath = typeof(TextureCompactor).GetMethod(
    "ResolveTexturePath",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
) ?? throw new Exception("missing texture path resolver");
var rootRelativeTexture = "/_texture_store/sha256/ab/abc.png";
var resolvedRootRelativeTexture = (string?)resolveTexturePath.Invoke(
    null,
    new object[] { directTextureRoot, directTextureRoot, rootRelativeTexture }
);
Expect(
    resolvedRootRelativeTexture == Path.Combine(directTextureRoot, rootRelativeTexture.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)),
    "texture optimizer resolves root-relative CAS paths instead of treating them as external URIs"
);

var motionValuesPath = Path.Combine(writerDir, "motion-values.json");
RuntimeJsonWriter.Write(
    motionValuesPath,
    new
    {
        clips = new[]
        {
            new
            {
                tracks = new[]
                {
                    new
                    {
                        times = Enumerable.Range(0, 20).Select(index => index / 60f).ToArray(),
                        values = Enumerable.Range(0, 20).Select(index => index / 10f).ToArray()
                    }
                }
            }
        }
    },
    new JsonSerializerOptions(),
    binaryArraySchema: RuntimeBinaryArraySchema.UnityMotion
);
Expect(
    ContainsRuntimeBinaryExtension(ReadBrotliBytes(RuntimeJsonWriter.MessagePackBrotliPath(motionValuesPath))),
    "Unity motion track arrays are binary-encoded under the explicit motion schema"
);

var compactDir = Path.Combine(tempDir, "compact");
var packageA = Path.Combine(compactDir, "parts", "_sources", "body", "a");
var packageB = Path.Combine(compactDir, "parts", "_sources", "head", "b");
var packageC = Path.Combine(compactDir, "parts", "_sources", "hair", "c");
WriteRuntimePackage(packageA, "textures/body/a.png", new byte[] { 1, 2, 3, 4 });
WriteRuntimePackage(packageB, "textures/head/b.png", new byte[] { 1, 2, 3, 4 });
WriteRuntimePackage(packageC, "textures/hair/c.png", new byte[] { 9, 8, 7 });
var compactReport = new TextureCompactor().Compact(compactDir, "off", 3);
Expect(compactReport.TextureFileCount == 3, "texture compactor scans package textures");
Expect(compactReport.UniqueHashCount == 2, "texture compactor groups by exact SHA-256");
Expect(compactReport.DuplicateFileCount == 1, "texture compactor counts duplicate files");
Expect(compactReport.SavedBytes == 4, "texture compactor saves only exact duplicate bytes with optimization off");
Expect(compactReport.WorkerCount == 3, "texture compactor reports parallel cleanup worker count");
Expect(File.Exists(Path.Combine(compactDir, "texture-compaction-report.json")), "texture compactor writes report");
Expect(!File.Exists(Path.Combine(packageA, "textures", "body", "a.png")), "texture compactor removes package-local texture A");
Expect(!File.Exists(Path.Combine(packageB, "textures", "head", "b.png")), "texture compactor removes package-local texture B");
Expect(!Directory.Exists(Path.Combine(packageA, "textures")), "texture compactor removes empty nested texture directory A");
Expect(!Directory.Exists(Path.Combine(packageB, "textures")), "texture compactor removes empty nested texture directory B");
Expect(!Directory.Exists(Path.Combine(packageC, "textures")), "texture compactor removes empty nested texture directory C");
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

var compactMessagePackDir = Path.Combine(tempDir, "compact-msgpack");
var messagePackPackage = Path.Combine(compactMessagePackDir, "parts", "_sources", "body", "a");
WriteRuntimePackage(
    messagePackPackage,
    "textures/body/a.png",
    new byte[] { 1, 2, 3, 4 }
);
var compactMessagePackReport = new TextureCompactor().Compact(
    compactMessagePackDir,
    "off",
    1
);
Expect(compactMessagePackReport.RewrittenReferenceCount == 3, "texture compactor rewrites MessagePack runtime references");
Expect(!File.Exists(Path.Combine(messagePackPackage, "textures", "body", "a.png")), "MessagePack compaction removes replaced source texture");
var rewrittenMessagePack = ReadRuntimePackage(
    Path.Combine(messagePackPackage, "part-runtime.json")
);
var messagePackTexture = rewrittenMessagePack["characterTextures"]!["main"]!.GetValue<string>();
Expect(messagePackTexture.StartsWith("/_texture_store/sha256/"), "MessagePack runtime points at compacted texture store");
Expect(
    File.Exists(Path.Combine(compactMessagePackDir, messagePackTexture.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))),
    "MessagePack compacted texture exists"
);

var sharedCas = Path.Combine(tempDir, "shared-cas");
var casRegionA = Path.Combine(tempDir, "cas-region-a");
var casRegionB = Path.Combine(tempDir, "cas-region-b");
WriteCasFixture(casRegionA);
WriteCasFixture(casRegionB);
var firstCasReport = new ContentAddressedStore().Compact(casRegionA, sharedCas);
var secondCasReport = new ContentAddressedStore().Compact(casRegionB, sharedCas);
var unchangedCasReport = new ContentAddressedStore().Compact(casRegionB, sharedCas);
Expect(firstCasReport.TextureFileCount == 1, "shared CAS scans compacted textures");
Expect(firstCasReport.PartRuntimeFileCount == 1, "shared CAS scans part runtime packages");
Expect(firstCasReport.NewContentCount == 2, "first region seeds exact content in the shared CAS");
Expect(secondCasReport.ReusedContentCount == 2, "second region reuses exact texture and part runtime bytes");
Expect(secondCasReport.ReusedBytes > 0, "shared CAS reports bytes reused across regions");
Expect(unchangedCasReport.UnchangedFileCount == 2, "repeated CAS runs skip unchanged files without hashing or relinking");
if (!OperatingSystem.IsWindows())
{
    var canonicalModes = File.GetUnixFileMode(CasPartRuntimePath(casRegionA));
    Expect(
        (canonicalModes & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0,
        "CAS-linked content is read-only to prevent in-place mutation of shared inodes"
    );
}
Expect(File.ReadAllBytes(CasTexturePath(casRegionA)).SequenceEqual(File.ReadAllBytes(CasTexturePath(casRegionB))), "CAS-linked textures preserve exact bytes");
Expect(File.ReadAllBytes(CasPartRuntimePath(casRegionA)).SequenceEqual(File.ReadAllBytes(CasPartRuntimePath(casRegionB))), "CAS-linked part runtimes preserve exact bytes");
var regionAPartBytes = File.ReadAllBytes(CasPartRuntimePath(casRegionA));
RuntimeJsonWriter.Write(
    Path.Combine(casRegionB, "parts", "_sources", "body", "source", "part-runtime.json"),
    new { version = "changed", positions = new[] { 3f, 4f, 5f } },
    new JsonSerializerOptions()
);
Expect(File.ReadAllBytes(CasPartRuntimePath(casRegionA)).SequenceEqual(regionAPartBytes), "atomic runtime writes do not mutate another region's CAS link");
using (var changedRuntime = RuntimeJsonWriter.ReadJsonDocument(
    Path.Combine(casRegionB, "parts", "_sources", "body", "source", "part-runtime.json")
))
{
    Expect(changedRuntime.RootElement.GetProperty("version").GetString() == "changed", "atomic runtime writes replace only the requested region path");
}

var registryMasterDir = Path.Combine(tempDir, "registry-master");
var registryAssetRoot = Path.Combine(tempDir, "registry-assets");
Directory.CreateDirectory(registryMasterDir);
WriteJsonFile(Path.Combine(registryMasterDir, "character3ds.json"), new[]
{
    new
    {
        id = 9001,
        characterId = 23,
        unit = "light_sound",
        name = "cross-role official preset",
        headCostume3dId = 11001,
        hairCostume3dId = 202,
        bodyCostume3dId = 13000
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "costume3ds.json"), new[]
{
    new
    {
        id = 13000,
        costume3dGroupId = 13000,
        partType = "body",
        characterId = 21,
        colorId = 1,
        colorName = "test",
        name = "cross-role body",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "unused",
        howToObtain = "test"
    },
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
    },
    new
    {
        id = 11009,
        costume3dGroupId = 11000,
        partType = "head",
        characterId = 21,
        colorId = 1,
        colorName = "test",
        name = "fallback accessory",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "0020/a04",
        howToObtain = "test"
    },
    new
    {
        id = 1,
        costume3dGroupId = 1,
        partType = "head",
        characterId = 1,
        colorId = 1,
        colorName = "default",
        name = "empty accessory slot",
        costume3dType = "normal",
        costume3dRarity = "rarity_1",
        assetbundleName = "head_default_01",
        howToObtain = "default"
    },
    new
    {
        id = 202,
        costume3dGroupId = 202,
        partType = "hair",
        characterId = 2,
        colorId = 1,
        colorName = "default",
        name = "default hair fallback",
        costume3dType = "normal",
        costume3dRarity = "rarity_1",
        assetbundleName = "unused",
        howToObtain = "default"
    },
    new
    {
        id = 203,
        costume3dGroupId = 203,
        partType = "hair",
        characterId = 21,
        colorId = 1,
        colorName = "default",
        name = "same-character target hair",
        costume3dType = "normal",
        costume3dRarity = "rarity_1",
        assetbundleName = "unused",
        howToObtain = "default"
    },
    new
    {
        id = 12000,
        costume3dGroupId = 12000,
        partType = "head",
        characterId = 2,
        colorId = 1,
        colorName = "missing",
        name = "missing complete head",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "unused",
        howToObtain = "test"
    },
    new
    {
        id = 12001,
        costume3dGroupId = 12001,
        partType = "head",
        characterId = 2,
        colorId = 1,
        colorName = "test",
        name = "split accessory",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "unused",
        howToObtain = "test"
    },
    new
    {
        id = 797001,
        costume3dGroupId = 797001,
        partType = "head",
        characterId = 1,
        colorId = 1,
        colorName = "original",
        name = "shared accessory source",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "cos0797_head",
        howToObtain = "card"
    },
    new
    {
        id = 797009,
        costume3dGroupId = 797002,
        partType = "head",
        characterId = 2,
        colorId = 1,
        colorName = "original",
        name = "exclusive accessory",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "cos0797_unique_head",
        howToObtain = "card"
    },
    new
    {
        id = 797011,
        costume3dGroupId = 797002,
        partType = "head",
        characterId = 2,
        colorId = 2,
        colorName = "another 1",
        name = "exclusive accessory",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "cos0797_unique_head_01",
        howToObtain = "card"
    },
    new
    {
        id = 797161,
        costume3dGroupId = 797021,
        partType = "head",
        characterId = 2,
        colorId = 1,
        colorName = "original",
        name = "shared accessory",
        costume3dType = "normal",
        costume3dRarity = "rarity_4",
        assetbundleName = "cos0797_head",
        howToObtain = "card"
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModels.json"), new object[]
{
    new
    {
        costume3dId = 13000,
        unit = "light_sound",
        assetbundleName = "99/0081",
        headCostume3dAssetbundleType = (string?)null,
        colorAssetbundleName = (string?)null,
        part = (string?)null,
        thumbnailAssetbundleName = "unused"
    },
    new
    {
        costume3dId = 11001,
        unit = "light_sound",
        assetbundleName = "0019/a03",
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = "01",
        part = "a03",
        thumbnailAssetbundleName = "unused"
    },
    new
    {
        costume3dId = 11009,
        unit = "light_sound",
        assetbundleName = (string?)null,
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = "02",
        part = "a04",
        thumbnailAssetbundleName = "unused"
    },
    new
    {
        costume3dId = 1,
        unit = "light_sound",
        assetbundleName = (string?)null,
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = (string?)null,
        part = (string?)null,
        thumbnailAssetbundleName = "head_default_01"
    },
    new
    {
        costume3dId = 202,
        unit = (string?)null,
        assetbundleName = "02/0000",
        headCostume3dAssetbundleType = (string?)null,
        colorAssetbundleName = (string?)null,
        part = (string?)null,
        thumbnailAssetbundleName = "unused"
    },
    new
    {
        costume3dId = 12000,
        unit = "light_sound",
        assetbundleName = "0710/a05",
        headCostume3dAssetbundleType = "head",
        colorAssetbundleName = (string?)null,
        part = (string?)null,
        thumbnailAssetbundleName = "unused"
    },
    new
    {
        costume3dId = 12001,
        unit = "light_sound",
        assetbundleName = "0083/a05",
        headCostume3dAssetbundleType = "head_all",
        colorAssetbundleName = (string?)null,
        part = "a05",
        thumbnailAssetbundleName = "unused"
    },
    new
    {
        costume3dId = 797001,
        unit = "light_sound",
        assetbundleName = "0924/a03",
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = (string?)null,
        part = "a03",
        thumbnailAssetbundleName = "cos0797_head"
    },
    new
    {
        costume3dId = 797009,
        unit = "light_sound",
        assetbundleName = "02/0924",
        headCostume3dAssetbundleType = "head_and_hair",
        colorAssetbundleName = (string?)null,
        part = (string?)null,
        thumbnailAssetbundleName = "cos0797_unique_head"
    },
    new
    {
        costume3dId = 797009,
        unit = "idol",
        assetbundleName = "0924/a03",
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = (string?)null,
        part = "a03",
        thumbnailAssetbundleName = "cos0797_head"
    },
    new
    {
        costume3dId = 797011,
        unit = "light_sound",
        assetbundleName = "02/0924a",
        headCostume3dAssetbundleType = "head_and_hair",
        colorAssetbundleName = "01",
        part = (string?)null,
        thumbnailAssetbundleName = "cos0797_unique_head_01"
    },
    new
    {
        costume3dId = 797161,
        unit = "light_sound",
        assetbundleName = "0924/a03",
        headCostume3dAssetbundleType = "head_only",
        colorAssetbundleName = (string?)null,
        part = "a03",
        thumbnailAssetbundleName = "cos0797_head"
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "gameCharacters.json"), new[]
{
    new
    {
        id = 23,
        resourceId = 23,
        gender = "male",
        height = 1.7,
        figure = "mens",
        breastSize = "none",
        modelName = "test",
        unit = "light_sound",
        supportUnitType = (string?)null,
        faceModelType = "Special",
        prefabType = "Default",
        isHeelOffset = false
    },
    new
    {
        id = 2,
        resourceId = 2,
        gender = "female",
        height = 1.6,
        figure = "ladies",
        breastSize = "m",
        modelName = "test",
        unit = "light_sound",
        supportUnitType = (string?)null,
        faceModelType = "0",
        prefabType = "Default",
        isHeelOffset = false
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "cards.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "cardCostume3ds.json"), Array.Empty<object>());
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModelAvailablePatterns.json"), new[]
{
    new
    {
        headCostume3dId = 11001,
        hairCostume3dId = 202,
        unit = "light_sound",
        isDefault = false
    },
    new
    {
        headCostume3dId = 11009,
        hairCostume3dId = 202,
        unit = "idol",
        isDefault = false
    },
    new
    {
        headCostume3dId = 11001,
        hairCostume3dId = 202,
        unit = "idol",
        isDefault = false
    },
    new
    {
        headCostume3dId = 11009,
        hairCostume3dId = 203,
        unit = "idol",
        isDefault = false
    },
    new
    {
        headCostume3dId = 11001,
        hairCostume3dId = 203,
        unit = "idol",
        isDefault = false
    },
    new
    {
        headCostume3dId = 11001,
        hairCostume3dId = 202,
        unit = "piapro",
        isDefault = false
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModelNotAvailablePatterns.json"), new[]
{
    new
    {
        headCostume3dId = 999001,
        hairCostume3dId = 999002,
        unit = "light_sound",
        isDefault = false
    }
});
WriteJsonFile(Path.Combine(registryMasterDir, "costume3dModelDefaultHairs.json"), Array.Empty<object>());
var legacyAccessory = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "head_optional",
    "0019",
    "a03.bundle"
);
var legacyAccessoryColor = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "color_variation",
    "head_optional",
    "0019",
    "a03",
    "01.bundle"
);
var fallbackAccessory = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "head_optional",
    "0020",
    "a04.bundle"
);
var fallbackAccessoryColor = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "color_variation",
    "head_optional",
    "0020",
    "a04",
    "02.bundle"
);
var splitAccessory = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "head_optional",
    "0083",
    "a05.bundle"
);
var sharedAccessory = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "head_optional",
    "0924",
    "a03.bundle"
);
var exclusiveAccessory = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "face",
    "02",
    "0924.bundle"
);
var exclusiveAccessoryColor = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "face",
    "02",
    "0924a.bundle"
);
var defaultHairFallback = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "face",
    "02",
    "0001.bundle"
);
var faceModelTypeVariant = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "face",
    "02",
    "0000_special.bundle"
);
var presetBody = Path.Combine(
    registryAssetRoot,
    "live_pv",
    "model",
    "characterv2",
    "body",
    "99",
    "0081",
    "mens.bundle"
);
Directory.CreateDirectory(Path.GetDirectoryName(legacyAccessory)!);
Directory.CreateDirectory(Path.GetDirectoryName(legacyAccessoryColor)!);
Directory.CreateDirectory(Path.GetDirectoryName(fallbackAccessory)!);
Directory.CreateDirectory(Path.GetDirectoryName(fallbackAccessoryColor)!);
Directory.CreateDirectory(Path.GetDirectoryName(splitAccessory)!);
Directory.CreateDirectory(Path.GetDirectoryName(sharedAccessory)!);
Directory.CreateDirectory(Path.GetDirectoryName(exclusiveAccessory)!);
Directory.CreateDirectory(Path.GetDirectoryName(exclusiveAccessoryColor)!);
Directory.CreateDirectory(Path.GetDirectoryName(defaultHairFallback)!);
Directory.CreateDirectory(Path.GetDirectoryName(faceModelTypeVariant)!);
Directory.CreateDirectory(Path.GetDirectoryName(presetBody)!);
File.WriteAllBytes(legacyAccessory, new byte[] { 1 });
File.WriteAllBytes(legacyAccessoryColor, new byte[] { 2 });
File.WriteAllBytes(fallbackAccessory, new byte[] { 3 });
File.WriteAllBytes(fallbackAccessoryColor, new byte[] { 4 });
File.WriteAllBytes(splitAccessory, new byte[] { 8 });
File.WriteAllBytes(sharedAccessory, new byte[] { 9 });
File.WriteAllBytes(exclusiveAccessory, new byte[] { 10 });
File.WriteAllBytes(exclusiveAccessoryColor, new byte[] { 11 });
File.WriteAllBytes(defaultHairFallback, new byte[] { 5 });
File.WriteAllBytes(faceModelTypeVariant, new byte[] { 6 });
File.WriteAllBytes(presetBody, new byte[] { 7 });
var registryExport = new CostumeRegistryExporter().ExportInMemory(registryMasterDir, registryAssetRoot);
var registryOutput = Path.Combine(tempDir, "registry-output");
new CostumeRegistryExporter().Export(
    registryMasterDir,
    registryAssetRoot,
    registryOutput
);
using (var scopedCompatibility = RuntimeJsonWriter.ReadJsonDocument(Path.Combine(
    registryOutput,
    "parts",
    "compat",
    "by-unit",
    "light_sound",
    "head-hair-compatibility.json"
)))
{
    var scopedRules = scopedCompatibility.RootElement.GetProperty("rules").EnumerateArray().ToArray();
    Expect(scopedRules.Length == 1, "scoped head-hair compatibility omits positive and default rules");
    Expect(scopedRules[0].GetProperty("state").GetString() == "not_available", "scoped head-hair compatibility keeps deny rules");
}
using (var compactRegistry = RuntimeJsonWriter.ReadJsonDocument(
    Path.Combine(registryOutput, "parts", "part-registry-compact.json")
))
{
    var root = compactRegistry.RootElement;
    Expect(root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 3, "compact part registry uses a versioned array envelope");
    var rootItems = root.EnumerateArray().ToArray();
    Expect(rootItems[0].GetInt32() == 1, "compact part registry schema version is stable");
    Expect(rootItems[1].GetInt32() == registryExport.PartRegistry.Version, "compact part registry keeps the source registry version");
    var firstRow = rootItems[2].EnumerateArray().First();
    Expect(firstRow.ValueKind == JsonValueKind.Array && firstRow.GetArrayLength() == 15, "compact part registry rows omit repeated field names");
}
using (var compactCompatibility = RuntimeJsonWriter.ReadJsonDocument(
    Path.Combine(registryOutput, "parts", "head-hair-compatibility-compact.json")
))
{
    var rootItems = compactCompatibility.RootElement.EnumerateArray().ToArray();
    Expect(rootItems.Length == 2 && rootItems[0].GetInt32() == 1, "compact compatibility uses a versioned array envelope");
    var firstRow = rootItems[1].EnumerateArray().First();
    Expect(firstRow.ValueKind == JsonValueKind.Array && firstRow.GetArrayLength() == 5, "compact compatibility rows omit unused metadata and repeated field names");
}
Expect(
    registryExport.Character3dIndex.Entries.All(entry => entry.RoleRuntimePath.EndsWith(".msgpack.br", StringComparison.Ordinal)),
    "default character registry points at MessagePack Brotli role runtimes"
);
Expect(registryExport.PartRegistry.Version == 2, "part registry marks source-based accessory identity schema");
var presetEntry = registryExport.Character3dIndex.Entries.Single(entry => entry.Character3dId == 9001);
Expect(presetEntry.AssetBundleNames.Contains("02/0000_special"), "preset index records existing faceModelType face variant");
Expect(presetEntry.AssetBundlePaths.Contains("live_pv/model/characterv2/face/02/0000_special.bundle"), "preset index records actual faceModelType bundle path");
Expect(presetEntry.AssetBundlePaths.Contains("live_pv/model/characterv2/body/99/0081/mens.bundle"), "preset index records actual body bundle path");
var outfitBodyEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 13000 && entry.CharacterId == 21);
Expect(outfitBodyEntry.OutfitId == 13, "body registry derives stable outfit id from costume group family");
var legacyAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001 && entry.CharacterId == 21 && entry.Unit == "light_sound");
Expect(legacyAccessoryEntry.OutfitId == 0, "non-body registry rows do not expose an outfit id");
Expect(legacyAccessoryEntry.AccessoryId == 11000, "head_optional color inherits its original-color source accessory id");
Expect(legacyAccessoryEntry.PartType == "head_optional", "head_only registry rows are exported as head_optional");
Expect(legacyAccessoryEntry.BundlePath == legacyAccessory, "head_optional registry resolves characterv2 base bundle");
Expect(legacyAccessoryEntry.ColorVariationBundlePath == legacyAccessoryColor, "head_optional registry resolves characterv2 color variation bundle");
Expect(legacyAccessoryEntry.PackagePath.StartsWith("parts/_sources/head_optional/"), "head_optional registry writes shared source package path");
var fallbackAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11009 && entry.CharacterId == 21 && entry.Unit == "light_sound");
Expect(fallbackAccessoryEntry.Status == "planned", "head_optional fallback accessory is planned");
Expect(fallbackAccessoryEntry.BundlePath == fallbackAccessory, "head_optional registry resolves costume assetbundleName fallback");
Expect(fallbackAccessoryEntry.ColorVariationBundlePath == fallbackAccessoryColor, "head_optional registry resolves fallback color variation");
Expect(fallbackAccessoryEntry.AttachNode == "a04", "head_optional fallback accessory keeps attach node");
var emptyAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 1);
Expect(emptyAccessoryEntry.PartType == "head_optional", "empty head_default slot is exported as head_optional");
Expect(emptyAccessoryEntry.Status == "empty", "empty head_default slot is a valid empty part");
Expect(emptyAccessoryEntry.BundlePath is null, "empty head_default slot does not point at a bundle");
Expect(emptyAccessoryEntry.SourceKey is null, "empty head_default slot does not create a source package");
Expect(emptyAccessoryEntry.Warnings.Count == 0, "empty head_default slot is not a warning");
var defaultHairEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 202 && entry.CharacterId == 2);
Expect(defaultHairEntry.Status == "planned", "default hair 0000 row falls back to existing 0001 bundle");
Expect(defaultHairEntry.BundlePath == defaultHairFallback, "default hair fallback points at the existing 0001 bundle");
Expect(defaultHairEntry.PackagePath.StartsWith("parts/_sources/hair/"), "default hair fallback keeps a source package");
var missingHeadEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 12000);
Expect(missingHeadEntry.Status == "missing", "missing complete head remains missing after fallback attempts");
Expect(missingHeadEntry.BundlePath is null, "missing complete head does not keep a fabricated bundle path");
Expect(missingHeadEntry.SourceKey is null, "missing complete head does not create a dangling source key");
Expect(missingHeadEntry.Warnings.Any(warning => warning.Contains("face bundle not found")), "missing complete head records a file warning");
var splitAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 12001);
Expect(splitAccessoryEntry.PartType == "head_optional", "head_all registry rows are exported as head_optional");
Expect(splitAccessoryEntry.BundlePath == splitAccessory, "head_all registry resolves its optional accessory bundle");
var canonicalSharedAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 797001);
var exclusiveAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 797009 && entry.Unit == "light_sound");
var sameRawSharedAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 797009 && entry.Unit == "idol");
var exclusiveAccessoryColorEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 797011 && entry.Unit == "light_sound");
var sharedAccessoryEntry = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 797161 && entry.CharacterId == 2);
Expect(canonicalSharedAccessoryEntry.AccessoryId == 797001, "shared accessory uses its smallest original-color costume group id");
Expect(sharedAccessoryEntry.AccessoryId == 797001, "shared accessory aliases keep the canonical source accessory id");
Expect(sameRawSharedAccessoryEntry.PartType == "head_optional", "the shared-unit model resolves the same raw costume as head_optional");
Expect(sameRawSharedAccessoryEntry.AccessoryId == 797001, "the same raw costume follows its resolved shared source");
Expect(exclusiveAccessoryEntry.PartType == "head", "the exclusive-unit model resolves the same raw costume as a complete head");
Expect(exclusiveAccessoryEntry.AccessoryId == 797002, "character-exclusive head uses its original-color costume group id");
Expect(exclusiveAccessoryColorEntry.BaseSourceKey != exclusiveAccessoryEntry.BaseSourceKey, "exclusive color fixture uses a distinct resolved base source");
Expect(exclusiveAccessoryColorEntry.AccessoryId == 797002, "character-exclusive head colors inherit the original-color accessory id");
Expect(sameRawSharedAccessoryEntry.BaseSourceKey == canonicalSharedAccessoryEntry.BaseSourceKey, "shared unit entries reuse the shared base source");
Expect(exclusiveAccessoryEntry.BaseSourceKey != sameRawSharedAccessoryEntry.BaseSourceKey, "different units of the same raw costume retain distinct resolved sources");
Expect(exclusiveAccessoryEntry.AccessoryId != sharedAccessoryEntry.AccessoryId, "exclusive and shared heads remain separate accessories");
var roleHeadOptionalAlias = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001 && entry.CharacterId == 23);
Expect(roleHeadOptionalAlias.PartType == "head_optional", "official cross-role head_only preset aliases as head_optional");
Expect(roleHeadOptionalAlias.Unit == "light_sound", "official cross-role alias keeps model unit");
Expect(roleHeadOptionalAlias.PackagePath == legacyAccessoryEntry.PackagePath, "official cross-role alias reuses source package path");
Expect(roleHeadOptionalAlias.AccessoryId == 11000, "official alias receives the canonical accessory id");
var compatibleHeadAlias = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001 && entry.CharacterId == 2 && entry.Unit == "light_sound");
Expect(compatibleHeadAlias.PackagePath == legacyAccessoryEntry.PackagePath, "available cross-role head/hair pair reuses the accessory source package");
Expect(compatibleHeadAlias.AccessoryId == 11000, "compatible alias receives the canonical accessory id");
var crossUnitCompatibleBase = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11009 && entry.CharacterId == 2 && entry.Unit == "idol");
var crossUnitCompatibleColor = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001 && entry.CharacterId == 2 && entry.Unit == "idol");
Expect(crossUnitCompatibleBase.AccessoryId == 11000, "cross-character cross-unit alias receives the canonical accessory id");
Expect(crossUnitCompatibleColor.AccessoryId == 11000, "cross-character cross-unit color inherits the canonical accessory id");
var sameCharacterCrossUnitBase = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11009 && entry.CharacterId == 21 && entry.Unit == "idol");
var sameCharacterCrossUnitColor = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001 && entry.CharacterId == 21 && entry.Unit == "idol");
Expect(sameCharacterCrossUnitBase.AccessoryId == 11000, "same-character cross-unit alias is retained");
Expect(sameCharacterCrossUnitColor.AccessoryId == 11000, "same-character cross-unit color inherits the canonical accessory id");
var directSourceColorAlias = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 11001 && entry.CharacterId == 2 && entry.Unit == "piapro");
Expect(directSourceColorAlias.AccessoryId == 11000, "color-only cross-unit alias inherits its original source accessory id");
var roleHairAlias = registryExport.PartRegistry.Entries.Single(entry => entry.Costume3dId == 202 && entry.CharacterId == 23);
Expect(roleHairAlias.Unit == "light_sound", "official cross-role alias promotes default-unit rows into the preset role unit");
Expect(roleHairAlias.PackagePath == defaultHairEntry.PackagePath, "official cross-role hair alias reuses source package path");

PartMaterialMetadataSmoke.Run();

var repoRoot = FindRepoRoot();
var programSource = File.ReadAllText(Path.Combine(repoRoot, "Program.cs"));
var partPackageExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "PartPackageExporter.cs"));
var compiledPartCacheSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "CompiledPartCache.cs"));
var conversionPlannerSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "ConversionPlanner.cs"));
var roleRuntimeExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "RoleRuntimeExporter.cs"));
var assetStudioLoadedBundleSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "AssetStudioLoadedBundle.cs"));
var assetStudioImportedModelFactorySource = File.ReadAllText(Path.Combine(repoRoot, "Services", "AssetStudioImportedModelFactory.cs"));
var bundleDependencyResolverSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "BundleDependencyResolver.cs"));
var materialIdentityLookupSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "MaterialIdentityLookup.cs"));
var bundleInputResolverSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "BundleInputResolver.cs"));
var sekaiBundleDecryptorSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "SekaiBundleDecryptor.cs"));
var character3dCostumeResolverSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "Character3dCostumeResolver.cs"));
Expect(partPackageExporterSource.Contains("part-runtime-core.msgpack.br"), "part package corePath uses the final MessagePack Brotli filename");
Expect(compiledPartCacheSource.Contains("part-runtime-core.msgpack.br"), "compiled part cache restores the final MessagePack Brotli corePath");
Expect(!partPackageExporterSource.Contains("part-runtime-core.json"), "part package exporter omits logical JSON core paths");
Expect(!compiledPartCacheSource.Contains("part-runtime-core.json"), "compiled part cache omits logical JSON core paths");
Expect(partPackageExporterSource.Contains("coreRelativePath.EndsWith(\".msgpack.br\""), "incremental export rejects old logical corePath manifests");
Expect(partPackageExporterSource.Contains("name.Contains(\"eyelash\")"), "part package exporter classifies eyelash separately");
Expect(partPackageExporterSource.Contains("return \"eyelash\""), "part package exporter returns eyelash material kind");
Expect(partPackageExporterSource.Contains("name.Contains(\"eyebrow\")"), "part package exporter classifies eyebrow separately");
Expect(partPackageExporterSource.Contains("return \"eyebrow\""), "part package exporter returns eyebrow material kind");
Expect(partPackageExporterSource.Contains("name.Contains(\"_acc_\")"), "part package exporter classifies head acc materials as accessory");
Expect(
    partPackageExporterSource.IndexOf("name.Contains(\"_hair_\")", StringComparison.Ordinal) <
    partPackageExporterSource.IndexOf("if (hasFaceShadowTex)", StringComparison.Ordinal),
    "part package exporter classifies explicit head hair materials before FaceSDF fallback"
);
Expect(
    partPackageExporterSource.IndexOf("name.Contains(\"_acc_\")", StringComparison.Ordinal) <
    partPackageExporterSource.IndexOf("if (hasFaceShadowTex)", StringComparison.Ordinal),
    "part package exporter classifies explicit head accessory materials before FaceSDF fallback"
);
Expect(
    conversionPlannerSource.IndexOf("name.Contains(\"_hair_\")", StringComparison.Ordinal) <
    conversionPlannerSource.IndexOf("if (hasFaceShadowTex)", StringComparison.Ordinal),
    "conversion planner classifies explicit head hair materials before FaceSDF fallback"
);
Expect(
    conversionPlannerSource.IndexOf("name.Contains(\"_acc_\")", StringComparison.Ordinal) <
    conversionPlannerSource.IndexOf("if (hasFaceShadowTex)", StringComparison.Ordinal),
    "conversion planner classifies explicit head accessory materials before FaceSDF fallback"
);
Expect(partPackageExporterSource.Contains("\"eyelash\" or \"eyebrow\""), "part package exporter uses full-runtime render order for face detail layers");
Expect(partPackageExporterSource.Contains("BuildDeferredColliderFlagBindings"), "part package exporter preserves deferred head colliderFlag bindings");
Expect(partPackageExporterSource.Contains("deferred_body_colliderFlag"), "part package exporter labels head colliderFlag bindings as deferred to viewer composer");
Expect(partPackageExporterSource.Contains("ResolveColliderFlagPrefixes"), "part package exporter resolves colliderFlag matched prefixes for viewer rebinding");
Expect(partPackageExporterSource.Contains("prefixes.Add(\"CL_Hip\")"), "part package exporter maps colliderFlag Hip");
Expect(partPackageExporterSource.Contains("prefixes.Add(\"CL_Chest\")"), "part package exporter maps colliderFlag Chest");
Expect(partPackageExporterSource.Contains("prefixes.Add(\"CL_Left_Arm\")"), "part package exporter maps colliderFlag L_Arm");
Expect(partPackageExporterSource.Contains("prefixes.Add(\"CL_Right_Arm\")"), "part package exporter maps colliderFlag R_Arm");
Expect(partPackageExporterSource.Contains("prefixes.Add(\"CL_Left_Elbow\")"), "part package exporter maps colliderFlag L_Elbow");
Expect(partPackageExporterSource.Contains("prefixes.Add(\"CL_Right_Elbow\")"), "part package exporter maps colliderFlag R_Elbow");
Expect(partPackageExporterSource.Contains("MatchedPrefixes: prefixes"), "part package exporter writes colliderFlag matched prefixes for viewer rebinding");
Expect(partPackageExporterSource.Contains("IsSumOfForcesOnBone: ReadBool(manager.Raw, \"isSumOfForcesOnBone\", defaultValue: true)"), "part package exporter defaults SpringManager force summing on like full runtime export");
Expect(partPackageExporterSource.Contains("RawAngleLimits: new VrmSpringBoneAngleLimitsCandidate("), "part package exporter preserves per-bone angle limits");
Expect(partPackageExporterSource.Contains("Y: ReadAxisLimit(bone.Raw, \"yAngleLimits\")"), "part package exporter reads y angle limits from SpringBone raw data");
Expect(partPackageExporterSource.Contains("Z: ReadAxisLimit(bone.Raw, \"zAngleLimits\")"), "part package exporter reads z angle limits from SpringBone raw data");
Expect(partPackageExporterSource.Contains("ReadOptionalBool(axis, \"active\") ??"), "part package exporter reads explicit angle limit active flags");
Expect(partPackageExporterSource.Contains("ReadOptionalBool(axis, \"m_Enabled\") ??"), "part package exporter reads Unity enabled angle limit flags");
Expect(partPackageExporterSource.Contains("                true,"), "part package exporter defaults present angle limits to active like full runtime output");
Expect(partPackageExporterSource.Contains("AccessoryTransformAdjustments: accessoryTransformAdjustments"), "part package exporter writes head_optional accessory transform adjustments");
Expect(partPackageExporterSource.Contains("root.Name, \"optional\""), "part package exporter prefers official head_optional prefab resource name");

var partRuntimeModelsSource = File.ReadAllText(Path.Combine(repoRoot, "Models", "PartRuntimeModels.cs"));
var springBoneExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "SpringBoneExporter.cs"));
var costumeRegistryModelsSource = File.ReadAllText(Path.Combine(repoRoot, "Models", "CostumeRegistryModels.cs"));
var costumeRegistryExporterSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "CostumeRegistryExporter.cs"));
var pjskRuntimeModelsSource = File.ReadAllText(Path.Combine(repoRoot, "Models", "PjskSekaiRuntimeModels.cs"));
var pjskRuntimeBuilderSource = File.ReadAllText(Path.Combine(repoRoot, "Services", "PjskSekaiRuntimeExtensionBuilder.cs"));
Expect(partRuntimeModelsSource.Contains("accessoryTransformAdjustments"), "part runtime mount exposes accessory transform adjustment map");
Expect(pjskRuntimeModelsSource.Contains("JsonPropertyName(\"isAccessory\")"), "runtime material slots expose official IS_ACCESSORY_ID metadata");
Expect(pjskRuntimeBuilderSource.Contains("IsAccessory: true"), "runtime builder marks accessory material slots with IS_ACCESSORY_ID metadata");
Expect(pjskRuntimeBuilderSource.Contains("IsAccessory: false"), "runtime builder keeps body/head material slots non-accessory");
Expect(partPackageExporterSource.Contains("IsAccessory: partType == \"head_optional\""), "part package exporter marks head_optional materials as accessories");
Expect(springBoneExporterSource.Contains("CharacterAccessoryTransformController"), "spring bone exporter keeps accessory transform controller mono behaviours");
Expect(springBoneExporterSource.Contains("CharacterAccessoryTransformData"), "spring bone exporter keeps accessory transform data mono behaviours");
Expect(springBoneExporterSource.Contains("\"ForceVolume\""), "spring bone exporter keeps standard UTJ ForceVolume providers");
Expect(springBoneExporterSource.Contains("\"WindVolume\""), "spring bone exporter keeps standard UTJ WindVolume providers");
Expect(springBoneExporterSource.Contains("\"WindVolumeOneSelf\""), "spring bone exporter keeps PJSK WindVolumeOneSelf providers");
Expect(springBoneExporterSource.Contains("BuildAccessoryTransformAdjustments"), "spring bone exporter extracts accessory transform adjustments");
Expect(springBoneExporterSource.Contains("_faceIdAccessoryTransformDict"), "spring bone exporter reads official face-id accessory transform dictionary");
Expect(springBoneExporterSource.Contains("string.Equals(entry.ScriptName, \"ExtraBone\", StringComparison.OrdinalIgnoreCase)"), "spring bone exporter serializes ExtraBone only from real MonoBehaviour records");
Expect(!springBoneExporterSource.Contains("StartsWith(\"EX_\""), "spring bone exporter does not infer ExtraBone records from EX_* transform names");
Expect(partRuntimeModelsSource.Contains("JsonPropertyName(\"extraBones\")"), "part runtime spring payload exposes ExtraBone records");
Expect(partPackageExporterSource.Contains("ExtraBones: springBone.ExtraBones"), "part package exporter preserves ExtraBone records for custom composition");
Expect(partPackageExporterSource.Contains("partType is \"head\" or \"hair\""), "head and hair color variations target hair materials");
Expect(character3dCostumeResolverSource.Contains("ResolveFaceColorVariationPath"), "character resolver resolves real face color variation bundles");
var failedPartGuard = programSource.IndexOf("if (failed > 0)", StringComparison.Ordinal);
var failedPartExit = failedPartGuard < 0
    ? -1
    : programSource.IndexOf("return 2;", failedPartGuard, StringComparison.Ordinal);
var failedPartCompaction = failedPartGuard < 0
    ? -1
    : programSource.IndexOf("RunTextureCompactionIfEnabled(options);", failedPartGuard, StringComparison.Ordinal);
Expect(
    failedPartGuard >= 0 && failedPartExit > failedPartGuard && failedPartExit < failedPartCompaction,
    "part package failures exit nonzero before texture compaction"
);
Expect(!programSource.Contains("shardManifestPaths"), "part worker orchestration does not create manifest shards");
Expect(programSource.Contains("PartPackageExportManifest.Rebuild("), "parent rebuilds one canonical manifest after worker success");
Expect(
    partPackageExporterSource.Contains("if (claims is null && string.IsNullOrWhiteSpace(workListPath))"),
    "claim and work-list workers treat the shared baseline manifest as read-only"
);
Expect(pjskRuntimeBuilderSource.Contains("SpringManager.FindSpringBones(true) ownership is authoritative"), "runtime builder documents hierarchy-based SpringManager ownership");
Expect(!pjskRuntimeBuilderSource.Contains("SpringManager.springBones references remain authoritative"), "runtime builder does not treat serialized springBones as authoritative");
Expect(
    pjskRuntimeBuilderSource.IndexOf("\"ModelUtility.SpringBoneSetup\"", StringComparison.Ordinal) <
    pjskRuntimeBuilderSource.IndexOf("\"CharacterModel.SetupSpringBone\"", StringComparison.Ordinal),
    "runtime builder setup plan follows official SpringBoneSetup before SetupSpringBone order"
);
Expect(partPackageExporterSource.Contains("rebuild SpringManager ownership from composed hierarchy"), "part package setup plan rebuilds manager ownership after composition");
Expect(partRuntimeModelsSource.Contains("JsonPropertyName(\"funit\")"), "part runtime spring payload exposes FUnit metadata separately");
Expect(pjskRuntimeModelsSource.Contains("JsonPropertyName(\"funit\")"), "runtime unity setup exposes FUnit metadata separately");
Expect(springBoneExporterSource.Contains("BuildFUnitSummary"), "spring bone exporter detects FUnit metadata");
Expect(springBoneExporterSource.Contains("ScriptNamespace"), "spring bone exporter distinguishes FUnit by MonoScript namespace");
Expect(springBoneExporterSource.Contains("metadata_only; do not merge with UTJ/Sekai SpringBone runtime"), "FUnit detection is explicitly metadata-only");
Expect(!springBoneExporterSource.Contains("FUnit.SpringBone runtime"), "spring bone exporter does not route FUnit into the UTJ runtime path");
Expect(pjskRuntimeModelsSource.Contains("faceRendererName"), "runtime body-head assembly exposes official face renderer predicate name");
Expect(pjskRuntimeModelsSource.Contains("combineNodeAName"), "runtime body-head assembly exposes official combine node A");
Expect(pjskRuntimeModelsSource.Contains("combineNodeBName"), "runtime body-head assembly exposes official combine node B");
Expect(pjskRuntimeModelsSource.Contains("childMoveSuffix"), "runtime body-head assembly exposes official child move suffix");
Expect(pjskRuntimeModelsSource.Contains("PjskUnityRuntimeConstraintSetup"), "runtime unity setup exposes constraint setup metadata");
Expect(pjskRuntimeModelsSource.Contains("PjskUnityRuntimeConstraint"), "runtime unity setup exposes constraint records");
Expect(pjskRuntimeModelsSource.Contains("PjskUnityRuntimeConstraintSource"), "runtime unity setup exposes multi-source constraint records");
Expect(springBoneExporterSource.Contains("Enum.TryParse<ClassIDType>"), "spring exporter probes AssetStudio constraint ClassID support dynamically");
Expect(springBoneExporterSource.Contains("SpringPrefabConstraintCapability"), "spring exporter records AssetStudio constraint capability");
Expect(springBoneExporterSource.Contains("ReadConstraintSources"), "spring exporter reads Unity constraint sources");
Expect(springBoneExporterSource.Contains("m_AimVector"), "spring exporter reads Unity AimConstraint axis fields");
Expect(springBoneExporterSource.Contains("m_WorldUpObject"), "spring exporter reads Unity AimConstraint world-up object");
Expect(springBoneExporterSource.Contains("m_RotationOffsets"), "spring exporter reads Unity per-source rotation offsets");
Expect(pjskRuntimeModelsSource.Contains("aimVector"), "runtime constraint records expose aim vector");
Expect(pjskRuntimeModelsSource.Contains("worldUpObjectPath"), "runtime constraint records expose world-up object path");
Expect(pjskRuntimeModelsSource.Contains("rotationOffset"), "runtime constraint records expose rotation offsets");
Expect(pjskRuntimeBuilderSource.Contains("BuildConstraintSetup"), "runtime builder emits constraint setup metadata");
Expect(pjskRuntimeBuilderSource.Contains("ModelUtility.ConstraintSetup"), "runtime setup plan includes official constraint setup step");
Expect(partPackageExporterSource.Contains("repair constraints after composition"), "part package setup plan carries constraint repair through viewer composition");
Expect(pjskRuntimeBuilderSource.Contains("ParentingMode: \"model_combine_setup\""), "full runtime setup declares official ModelCombineSetup parenting mode");
Expect(pjskRuntimeBuilderSource.Contains("FaceRendererName: \"Face\""), "full runtime setup writes official face renderer predicate");
Expect(pjskRuntimeBuilderSource.Contains("ChildMoveSuffix: \"_target\""), "full runtime setup writes official child move suffix");
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
Expect(costumeRegistryExporterSource.Contains("ResolveAssetBaseDirectoryCandidates"), "registry exporter resolves the characterv2 asset root");
Expect(!costumeRegistryExporterSource.Contains("\"model\", \"character\""), "registry exporter omits legacy character model roots");
Expect(!costumeRegistryExporterSource.Contains("Path.Combine(assetRoot, part)"), "registry exporter omits flat asset roots");
Expect(partPackageExporterSource.Contains("SelectRepresentativePartEntries"), "part package exporter exports each shared source package once");
Expect(partPackageExporterSource.Contains("GroupBy(entry => entry.PackagePath"), "part package exporter groups export work by package path");
Expect(roleRuntimeExporterSource.Contains("ResolveDefaultCostumeSettingMotionPath"), "role runtime exporter auto-resolves costume_setting motion bundles");
Expect(roleRuntimeExporterSource.Contains("LoadRepresentativeRoleCharacter3dIds"), "role runtime exporter exports one representative row per character+unit by default");
Expect(roleRuntimeExporterSource.Contains("LoadCanonicalRoleKeys"), "role runtime exporter filters role output to canonical character+unit roles");
Expect(roleRuntimeExporterSource.Contains("MikuUnitRoles"), "role runtime exporter keeps Miku unit variants");
Expect(roleRuntimeExporterSource.Contains("entry.Id >= 22 && entry.Id <= 26"), "role runtime exporter keeps non-Miku virtual singers on piapro only");
Expect(programSource.Contains("RunRoleRuntimeWorkers"), "program can export representative roles through worker processes");
Expect(programSource.Contains("Started role runtime worker"), "role runtime worker mode reports process shards");
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
Expect(assetStudioBundleParserSource.Contains("ConvertToStream(ImageFormat.Png, false)"), "bundle parser exports correctly oriented PNGs once");
Expect(!assetStudioBundleParserSource.Contains("Image.Load"), "bundle parser does not decode and re-encode exported PNGs");
Expect(assetStudioImportedModelFactorySource.Contains("new ModelConverter(preferredRoot, ImageFormat.Png, null, convertModelTextures)"), "part model conversion uses the final AssetStudio constructor directly");
Expect(!assetStudioImportedModelFactorySource.Contains("GetConstructor"), "part model conversion omits reflective AssetStudio fallbacks");
Expect(!assetStudioImportedModelFactorySource.Contains("NormalizeTextureOrientation"), "part model conversion omits legacy texture repair");
Expect(partPackageExporterSource.Contains("MaterialIdentityLookup"), "part package exporter resolves materials by identity");
Expect(!partPackageExporterSource.Contains("BuildMaterialMap"), "part package exporter no longer indexes materials by display name");
Expect(partPackageExporterSource.Contains("part-export-error.json"), "part package exporter writes per-package errors during full export");
Expect(partPackageExporterSource.Contains("Part package export skipped"), "part package exporter continues after per-package export failures");
Expect(partPackageExporterSource.Contains("DeletePartExportError"), "part package exporter removes stale per-package errors after success");
Expect(partPackageExporterSource.Contains("IsInShard"), "part package exporter can filter deterministic shards");
Expect(partPackageExporterSource.Contains("public static void Rebuild"), "part package exporter rebuilds one canonical worker manifest");
Expect(!partPackageExporterSource.Contains("bundle-open-summary.json"), "part package exporter omits per-package debug summaries from production output");
Expect(partPackageExporterSource.Contains("missing_after_fallback"), "part package exporter marks material failures after full-directory fallback");
Expect(assetStudioLoadedBundleSource.Contains("BundleDependencyResolver.ResolveLoadBundlePaths"), "loaded bundle uses shared dependency resolver");
Expect(bundleDependencyResolverSource.Contains("BundleLoadDependencyMode.FullDirectory"), "bundle dependency resolver supports full-directory fallback");
var nonexistentCompressedBundlePattern = "\"*.bundle" + ".gz\"";
Expect(!bundleDependencyResolverSource.Contains(nonexistentCompressedBundlePattern), "bundle dependency resolver only scans plain bundles");
Expect(!bundleInputResolverSource.Contains(nonexistentCompressedBundlePattern), "bundle input resolver only accepts plain bundle inputs");
Expect(!sekaiBundleDecryptorSource.Contains("IsGzipBundle"), "bundle decryptor does not special-case nonexistent gzip bundles");
Expect(partPackageExporterSource.Contains("MissingMaterialReferenceException"), "part package exporter retries missing material references");
Expect(partPackageExporterSource.Contains("Recovered missing material reference"), "part package exporter records material dependency fallback warnings");
Expect(materialIdentityLookupSource.Contains("MissingMaterialReferenceException"), "material lookup raises a typed missing reference error");

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

static void WriteRuntimePackage(
    string packageDirectory,
    string texturePath,
    byte[] textureBytes
)
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
        new JsonSerializerOptions()
    );
}

static string CasTexturePath(string outputDirectory) =>
    Path.Combine(outputDirectory, "_texture_store", "sha256", "aa", "texture.png");

static string CasPartRuntimePath(string outputDirectory) =>
    Path.Combine(outputDirectory, "parts", "_sources", "body", "source", "part-runtime.msgpack.br");

static void WriteCasFixture(string outputDirectory)
{
    var texturePath = CasTexturePath(outputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(texturePath)!);
    File.WriteAllBytes(texturePath, new byte[] { 1, 3, 3, 7 });
    RuntimeJsonWriter.Write(
        Path.Combine(outputDirectory, "parts", "_sources", "body", "source", "part-runtime.json"),
        new { version = "cas", positions = new[] { 0f, 1f, 2f } },
        new JsonSerializerOptions()
    );
}

static void WriteJsonFile(string path, object payload)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(payload));
}

static PartRegistryEntry PartEntry(
    string root,
    string name,
    int bytes,
    string sourceKey,
    string? packagePath = null
)
{
    var path = Path.Combine(root, $"haruki-work-{Guid.NewGuid():N}.bundle");
    File.WriteAllBytes(path, new byte[bytes]);
    return new PartRegistryEntry(
        1, "body", 1, null, name, 1, null, 1, 1, 0,
        null, null, null, null, null, path, null, sourceKey, sourceKey, null,
        packagePath ?? $"parts/body/{name}", null, "ready", Array.Empty<string>()
    );
}

static JsonObject ReadRuntimePackage(string runtimeJsonPath)
{
    using var document = RuntimeJsonWriter.ReadJsonDocument(runtimeJsonPath);
    return JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
}

static byte[] ReadBrotliBytes(string path)
{
    using var input = File.OpenRead(path);
    using var brotli = new BrotliStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    brotli.CopyTo(output);
    return output.ToArray();
}

static bool ContainsRuntimeBinaryExtension(byte[] messagePack)
{
    for (var index = 0; index + 2 < messagePack.Length; index += 1)
    {
        if (messagePack[index] == 0xc7 && messagePack[index + 2] == RuntimeJsonWriter.BinaryArrayExtensionType)
        {
            return true;
        }
        if (index + 3 < messagePack.Length && messagePack[index] == 0xc8 && messagePack[index + 3] == RuntimeJsonWriter.BinaryArrayExtensionType)
        {
            return true;
        }
        if (index + 5 < messagePack.Length && messagePack[index] == 0xc9 && messagePack[index + 5] == RuntimeJsonWriter.BinaryArrayExtensionType)
        {
            return true;
        }
    }
    return false;
}

enum DirectWriterState
{
    Ready,
}

sealed record DirectWriterFixture(
    string DisplayName,
    DirectWriterState State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Optional
);
