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
if (options.OptimizeTextureStore)
{
    try
    {
        var report = new TextureCompactor().OptimizeStore(
            options.OutputDirectory,
            options.PngOptimizeMode,
            options.TextureCompactWorkers
        );
        Console.WriteLine(
            $"Optimized texture store: {report.OptimizedFileCount}/{report.TextureFileCount} file(s), " +
            $"saved {report.SavedBytes} byte(s)."
        );
        RunOutputFinalization(options);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Texture store optimization failed: {ex.Message}");
        return 2;
    }
}
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

        var roleRuntimeExporter = new RoleRuntimeExporter(options.ConvertModelTextures);
        var results = roleRuntimeExporter.ExportMany(
            options.MasterDirectory!,
            options.AssetRoot!,
            options.OutputDirectory,
            options.RoleCharacter3dIds,
            options.MotionPath
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
            options.OutputDirectory
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
        var partCharacterHeightMetersById = !string.IsNullOrWhiteSpace(options.PartPackageWorkList)
            ? PartPackageWorkPlanner.Load(options.PartPackageWorkList!).CharacterHeightMetersById
            : !string.IsNullOrWhiteSpace(options.MasterDirectory)
                ? LoadCharacterHeightMetersById(options.MasterDirectory!)
                : null;
        var partPackageExporter = new PartPackageExporter(
            partCharacterHeightMetersById,
            options.ConvertModelTextures
        );
        if (options.PartCostume3dId is not null && options.PartType is not null)
        {
            var result = partPackageExporter.ExportOne(
                options.MasterDirectory!,
                options.AssetRoot!,
                options.OutputDirectory,
                options.PartCostume3dId.Value,
                options.PartType,
                options.PartUnit
            );
            Console.WriteLine($"Wrote part runtime package: {result.RuntimePath}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine($"Warnings: {result.Warnings.Count}");
            }
            RunOutputFinalization(options);
        }
        else
        {
            if (ResolvePartPackageProcessConcurrency(options) > 1)
            {
                return RunPartPackageWorkers(options, partCharacterHeightMetersById!);
            }

            var batch = partPackageExporter.ExportAll(
                options.MasterDirectory!,
                options.AssetRoot!,
                options.OutputDirectory,
                options.ManifestPath,
                options.PartPackageShardCount,
                options.PartPackageShardIndex,
                options.PartPackageClaimDirectory,
                options.CompiledContentStore,
                options.SharedContentStore,
                options.PartPackageWorkList,
                options.BundleHashIndex,
                options.TextureFormat == "ktx2"
            );
            var results = batch.Results;
            var succeeded = results.Count(result => result.Succeeded);
            var failed = results.Count - succeeded;
            Console.WriteLine($"Wrote {succeeded} part runtime package(s).");
            Console.WriteLine(
                $"Part export metrics: built={batch.Built}, restored={batch.Restored}, " +
                $"manifestSkipped={batch.ManifestSkipped}, bundleHashIndexHits={batch.BundleHashIndexHits}, " +
                $"fileHashComputations={batch.FileHashComputations}, elapsedMs={batch.ElapsedMilliseconds}."
            );
            if (!string.IsNullOrWhiteSpace(options.PartPackageWorkList))
            {
                File.WriteAllText(
                    PartPackageWorkPlanner.SummaryPath(options.PartPackageWorkList),
                    JsonSerializer.Serialize(PartPackageWorkerSummary.From(batch))
                );
            }
            if (failed > 0)
            {
                Console.Error.WriteLine($"Skipped {failed} part runtime package(s); see part-export-error.json files in the output tree.");
                return 2;
            }
            RunOutputFinalization(options);
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Part package export failed: {ex.Message}");
        return 2;
    }
}

Console.Error.WriteLine("Choose one final pipeline operation: --emit-costume-registries, --emit-part-packages, --emit-role-runtimes, --export-face-motion, or --optimize-texture-store.");
return 1;

static IReadOnlyDictionary<string, float> LoadCharacterHeightMetersById(string masterDirectory)
{
    return CharacterHeightResolver.LoadMetersByCharacterId(masterDirectory);
}

static int RunPartPackageWorkers(
    ConversionOptions options,
    IReadOnlyDictionary<string, float> characterHeightMetersById
)
{
    var requestedWorkers = ResolvePartPackageProcessConcurrency(options);
    var manifestPath = options.ManifestPath
        ?? Path.Combine(options.OutputDirectory, "haruki-3d-export-manifest.json");
    var workListDirectory = $"{manifestPath}.work-{Guid.NewGuid():N}";
    var processes = new List<Process>();
    var totalStopwatch = Stopwatch.StartNew();

    Directory.CreateDirectory(workListDirectory);

    try
    {
        var planningStopwatch = Stopwatch.StartNew();
        var registry = new CostumeRegistryExporter().ExportInMemory(
            options.MasterDirectory!,
            options.AssetRoot!
        );
        var partitions = PartPackageWorkPlanner.Plan(registry.PartRegistry.Entries, requestedWorkers);
        var workers = partitions.Count;
        var workListPaths = new List<string>(workers);
        for (var index = 0; index < workers; index++)
        {
            var path = Path.Combine(workListDirectory, $"worker-{index:D3}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new PartPackageWorkList(
                characterHeightMetersById,
                partitions[index]
            )));
            workListPaths.Add(path);
        }
        planningStopwatch.Stop();
        Console.WriteLine(
            $"Planned {registry.PartRegistry.Entries.Count} registry row(s) across {workers} worker(s) " +
            $"in {planningStopwatch.ElapsedMilliseconds} ms."
        );

        for (var index = 0; index < workers; index++)
        {
            var startInfo = CreateCurrentProcessStartInfo(new[]
            {
                "--emit-part-packages",
                "--master", options.MasterDirectory!,
                "--asset-root", options.AssetRoot!,
                "--out", options.OutputDirectory,
                "--manifest", manifestPath,
                "--part-package-process-concurrency", "1",
                "--part-package-work-list", workListPaths[index],
                "--assetstudio-log-level", options.AssetStudioLogLevel,
                "--convert-model-textures", options.ConvertModelTextures.ToString(),
                "--texture-format", options.TextureFormat,
            });
            if (!string.IsNullOrWhiteSpace(options.CompiledContentStore))
            {
                startInfo.ArgumentList.Add("--compiled-content-store");
                startInfo.ArgumentList.Add(options.CompiledContentStore);
            }
            if (!string.IsNullOrWhiteSpace(options.SharedContentStore))
            {
                startInfo.ArgumentList.Add("--shared-content-store");
                startInfo.ArgumentList.Add(options.SharedContentStore);
            }
            if (!string.IsNullOrWhiteSpace(options.BundleHashIndex))
            {
                startInfo.ArgumentList.Add("--bundle-hash-index");
                startInfo.ArgumentList.Add(options.BundleHashIndex);
            }
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

        var workerSummaries = workListPaths
            .Select(path => JsonSerializer.Deserialize<PartPackageWorkerSummary>(
                File.ReadAllText(PartPackageWorkPlanner.SummaryPath(path))
            ) ?? throw new InvalidOperationException($"Invalid worker summary for {path}."))
            .ToList();

        var finalizingStopwatch = Stopwatch.StartNew();
        PartPackageExportManifest.Rebuild(
            manifestPath,
            options.OutputDirectory,
            registry.PartRegistry.Entries
        );
        finalizingStopwatch.Stop();
        totalStopwatch.Stop();
        Console.WriteLine($"Rebuilt part package manifest: {manifestPath}");
        Console.WriteLine(
            $"Part export parent metrics: planningMs={planningStopwatch.ElapsedMilliseconds}, " +
            $"finalizingMs={finalizingStopwatch.ElapsedMilliseconds}, totalMs={totalStopwatch.ElapsedMilliseconds}, " +
            $"built={workerSummaries.Sum(summary => summary.Built)}, " +
            $"restored={workerSummaries.Sum(summary => summary.Restored)}, " +
            $"manifestSkipped={workerSummaries.Sum(summary => summary.ManifestSkipped)}, " +
            $"bundleHashIndexHits={workerSummaries.Sum(summary => summary.BundleHashIndexHits)}, " +
            $"fileHashComputations={workerSummaries.Sum(summary => summary.FileHashComputations)}."
        );
        RunOutputFinalization(options);
        return 0;
    }
    finally
    {
        foreach (var process in processes)
        {
            if (!process.HasExited)
            {
                process.WaitForExit();
            }
            process.Dispose();
        }
        if (Directory.Exists(workListDirectory))
        {
            Directory.Delete(workListDirectory, recursive: true);
        }
    }
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
            "--convert-model-textures", options.ConvertModelTextures.ToString(),
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
        options.PngOptimizeMode,
        options.TextureCompactWorkers
    );
    Console.WriteLine(
        "Compacted textures: " +
        $"{report.TextureFileCount} file(s), {report.UniqueHashCount} unique hash(es), " +
        $"saved {report.SavedBytes} byte(s), rewrote {report.RewrittenReferenceCount} reference(s)."
    );
}

static void RunOutputFinalization(ConversionOptions options)
{
    RunTextureCompactionIfEnabled(options);
    RunContentAddressedStoreIfEnabled(options);
    RunKtx2TranscodeIfEnabled(options);
    if (options.TextureFormat == "ktx2")
    {
        RunContentAddressedStoreIfEnabled(options);
    }
}

static void RunKtx2TranscodeIfEnabled(ConversionOptions options)
{
    if (options.TextureFormat != "ktx2" ||
        !options.OwnsOutputFinalization ||
        options.PartPackageShardCount > 1)
    {
        return;
    }
    var report = new TextureCompactor().TranscodeStoreToKtx2(
        options.OutputDirectory,
        options.TextureCompactWorkers,
        options.SharedContentStore
    );
    Console.WriteLine(
        $"Transcoded KTX2 textures: sources={report.SourceTextureCount}, " +
        $"variants={report.ConvertedVariantCount}, rewrites={report.RewrittenReferenceCount}, " +
        $"bytes={report.OriginalBytes}->{report.StoredBytes}."
    );
}

static void RunContentAddressedStoreIfEnabled(ConversionOptions options)
{
    if (string.IsNullOrWhiteSpace(options.SharedContentStore) ||
        !options.OwnsOutputFinalization)
    {
        return;
    }

    var report = new ContentAddressedStore().Compact(
        options.OutputDirectory,
        options.SharedContentStore
    );
    Console.WriteLine(
        $"Shared content CAS: textures={report.TextureFileCount}, " +
        $"part-runtimes={report.PartRuntimeFileCount}, new={report.NewContentCount}, " +
        $"reused={report.ReusedContentCount}, unchanged={report.UnchangedFileCount}, " +
        $"reused-bytes={report.ReusedBytes}"
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
