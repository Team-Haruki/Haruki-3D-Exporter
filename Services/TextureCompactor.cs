using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PjskBundle2Parts.Services;

public sealed class TextureCompactor
{
    public const string Ktx2EncoderVersion = "uastc-q2-zstd5-mip-v1";

    private readonly ConcurrentDictionary<string, string> ktx2ContentHashes = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public TextureStoreOptimizationReport OptimizeStore(
        string outputDirectory,
        string pngOptimizeMode,
        int workers
    )
    {
        var root = Path.Combine(Path.GetFullPath(outputDirectory), "_texture_store", "sha256");
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).ToList()
            : new List<string>();
        var mode = NormalizePngOptimizeMode(pngOptimizeMode);
        var workerCount = ResolveWorkerCount(workers);
        var results = new ConcurrentBag<TextureOptimizationResult>();
        var errors = new ConcurrentBag<Exception>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, path =>
        {
            try
            {
                var before = new FileInfo(path).Length;
                if (mode != "oxipng")
                {
                    results.Add(new TextureOptimizationResult(path, null, before, before));
                    return;
                }
                var temporaryPath = path + $".{Guid.NewGuid():N}.optimize.png";
                try
                {
                    File.Copy(path, temporaryPath);
                    RunOxipng(temporaryPath);
                    var after = new FileInfo(temporaryPath).Length;
                    if (after < before)
                    {
                        var optimizedHash = ComputeSha256Hex(temporaryPath);
                        var optimizedDirectory = Path.Combine(root, optimizedHash[..2]);
                        var optimizedPath = Path.Combine(optimizedDirectory, optimizedHash + ".png");
                        Directory.CreateDirectory(optimizedDirectory);
                        try
                        {
                            File.Move(temporaryPath, optimizedPath);
                        }
                        catch (IOException) when (File.Exists(optimizedPath))
                        {
                            File.Delete(temporaryPath);
                        }
                        results.Add(new TextureOptimizationResult(
                            path,
                            $"/_texture_store/sha256/{optimizedHash[..2]}/{optimizedHash}.png",
                            before,
                            new FileInfo(optimizedPath).Length
                        ));
                    }
                    else
                    {
                        results.Add(new TextureOptimizationResult(path, null, before, before));
                    }
                }
                finally
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });
        ThrowIfAny(errors, "Texture store optimization failed");
        var replacements = results
            .Where(result => result.RuntimePath is not null)
            .ToDictionary(
                result => Path.GetFullPath(result.OriginalPath),
                result => result.RuntimePath!,
                StringComparer.Ordinal
            );
        if (replacements.Count > 0)
        {
            var runtimeFiles = EnumerateRuntimeJsonFiles(outputDirectory).ToList();
            RewriteRuntimeJsonFiles(
                runtimeFiles,
                outputDirectory,
                replacements,
                workerCount
            );
            ValidateRuntimeTexturePaths(runtimeFiles, outputDirectory, workerCount);
            DeleteReplacedTextureFiles(replacements.Keys, outputDirectory, workerCount);
        }
        var report = new TextureStoreOptimizationReport(
            Version: 1,
            TextureFileCount: files.Count,
            OptimizedFileCount: replacements.Count,
            OriginalBytes: results.Sum(result => result.Before),
            StoredBytes: results.Sum(result => result.After),
            SavedBytes: results.Sum(result => result.Before - result.After),
            PngOptimizeMode: mode,
            WorkerCount: workerCount
        );
        File.WriteAllBytes(
            Path.Combine(outputDirectory, "texture-store-optimization-report.json"),
            JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions)
        );
        return report;
    }

    private sealed record TextureOptimizationResult(
        string OriginalPath,
        string? RuntimePath,
        long Before,
        long After
    );

    public Ktx2TranscodeReport TranscodeStoreToKtx2(
        string outputDirectory,
        int workers,
        string? sharedCacheDirectory = null
    )
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        sharedCacheDirectory = string.IsNullOrWhiteSpace(sharedCacheDirectory)
            ? null
            : Path.GetFullPath(sharedCacheDirectory);
        var runtimeFiles = EnumerateRuntimeJsonFiles(outputDirectory).ToList();
        var variants = CollectKtx2Variants(runtimeFiles, outputDirectory);
        var workerCount = ResolveWorkerCount(workers);
        var converted = new ConcurrentDictionary<Ktx2VariantKey, Ktx2VariantResult>();
        var errors = new ConcurrentBag<Exception>();
        Parallel.ForEach(
            variants,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount },
            variant =>
            {
                try
                {
                    converted[variant] = EncodeKtx2Variant(
                        outputDirectory,
                        variant,
                        sharedCacheDirectory
                    );
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        );
        ThrowIfAny(errors, "KTX2 texture conversion failed");
        DeleteDirectoryIfEmpty(Path.Combine(outputDirectory, ".ktx2-work"));

        var rewritten = 0;
        Parallel.ForEach(
            runtimeFiles,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount },
            runtimePath =>
            {
                try
                {
                    Interlocked.Add(
                        ref rewritten,
                        RewriteRuntimeJsonToKtx2(runtimePath, outputDirectory, converted)
                    );
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        );
        ThrowIfAny(errors, "KTX2 runtime JSON rewrite failed");
        ValidateRuntimeTexturePaths(runtimeFiles, outputDirectory, workerCount);

        var sourcePaths = variants
            .Select(variant => variant.SourcePath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var originalBytes = sourcePaths.Sum(path => new FileInfo(path).Length);
        DeleteReplacedTextureFiles(sourcePaths, outputDirectory, workerCount);
        var storedPaths = converted.Values
            .Select(result => result.StoredPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var report = new Ktx2TranscodeReport(
            Version: 1,
            SourceTextureCount: sourcePaths.Count,
            ConvertedVariantCount: converted.Count,
            RewrittenReferenceCount: rewritten,
            OriginalBytes: originalBytes,
            StoredBytes: storedPaths.Sum(path => new FileInfo(path).Length),
            WorkerCount: workerCount
        );
        File.WriteAllBytes(
            Path.Combine(outputDirectory, "ktx2-transcode-report.json"),
            JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions)
        );
        return report;
    }

    public bool TryRestoreCachedKtx2(
        JsonObject runtime,
        string packageDirectory,
        string outputDirectory,
        string sharedCacheDirectory
    )
    {
        packageDirectory = Path.GetFullPath(packageDirectory);
        outputDirectory = Path.GetFullPath(outputDirectory);
        sharedCacheDirectory = Path.GetFullPath(sharedCacheDirectory);
        var variants = CollectKtx2Variants(runtime, packageDirectory, outputDirectory, requireSource: false);
        if (variants.Count == 0)
        {
            return false;
        }

        var cached = new List<(Ktx2VariantKey Variant, string Path)>();
        foreach (var variant in variants)
        {
            var sourceHash = Path.GetFileNameWithoutExtension(variant.SourcePath);
            if (sourceHash.Length != 64 || sourceHash.Any(character => !Uri.IsHexDigit(character)))
            {
                return false;
            }
            var transfer = variant.Transfer.ToString().ToLowerInvariant();
            var cachePath = Path.Combine(
                sharedCacheDirectory,
                "ktx2",
                Ktx2EncoderVersion,
                transfer,
                sourceHash[..2],
                sourceHash + ".ktx2"
            );
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length == 0)
            {
                return false;
            }
            cached.Add((variant, cachePath));
        }

        var restored = new Dictionary<Ktx2VariantKey, Ktx2VariantResult>();
        foreach (var item in cached)
        {
            var contentHash = ktx2ContentHashes.GetOrAdd(item.Path, ComputeSha256Hex);
            var storePath = Path.Combine(
                outputDirectory,
                "_texture_store",
                "sha256",
                contentHash[..2],
                contentHash + ".ktx2"
            );
            ContentAddressedFile.Ensure(
                storePath,
                contentHash,
                temporaryPath => File.Copy(item.Path, temporaryPath)
            );
            restored[item.Variant] = new Ktx2VariantResult(
                storePath,
                $"/_texture_store/sha256/{contentHash[..2]}/{contentHash}.ktx2"
            );
        }

        var rewritten = RewriteRuntimeNodeToKtx2(
            runtime,
            packageDirectory,
            outputDirectory,
            restored
        );
        return rewritten > 0 &&
            CollectKtx2Variants(runtime, packageDirectory, outputDirectory, requireSource: false).Count == 0;
    }

    private static IReadOnlyList<Ktx2VariantKey> CollectKtx2Variants(
        IReadOnlyList<string> runtimeFiles,
        string outputDirectory
    )
    {
        var transfers = new Dictionary<string, HashSet<Ktx2Transfer>>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var runtimePath in runtimeFiles)
        {
            var packageDirectory = Path.GetDirectoryName(runtimePath)!;
            var node = ReadRuntimeJson(runtimePath);
            CollectKtx2Variants(
                node,
                packageDirectory,
                outputDirectory,
                requireSource: true,
                transfers,
                aliases
            );
        }
        foreach (var alias in aliases)
        {
            if (!transfers.ContainsKey(alias))
            {
                GetTransfers(transfers, alias).Add(Ktx2Transfer.Srgb);
            }
        }
        return transfers
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.OrderBy(value => value).Select(value => new Ktx2VariantKey(pair.Key, value)))
            .ToList();
    }

    private static IReadOnlyList<Ktx2VariantKey> CollectKtx2Variants(
        JsonObject runtime,
        string packageDirectory,
        string outputDirectory,
        bool requireSource
    )
    {
        var transfers = new Dictionary<string, HashSet<Ktx2Transfer>>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        CollectKtx2Variants(
            runtime,
            packageDirectory,
            outputDirectory,
            requireSource,
            transfers,
            aliases
        );
        foreach (var alias in aliases)
        {
            if (!transfers.ContainsKey(alias))
            {
                GetTransfers(transfers, alias).Add(Ktx2Transfer.Srgb);
            }
        }
        return transfers
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.OrderBy(value => value).Select(value => new Ktx2VariantKey(pair.Key, value)))
            .ToList();
    }

    private static void CollectKtx2Variants(
        JsonObject runtime,
        string packageDirectory,
        string outputDirectory,
        bool requireSource,
        Dictionary<string, HashSet<Ktx2Transfer>> transfers,
        HashSet<string> aliases
    )
    {
        if (runtime["characterTextures"] is JsonObject characterTextures)
        {
            foreach (var value in characterTextures.Select(pair => pair.Value).OfType<JsonValue>())
            {
                if (value.TryGetValue<string>(out var text) &&
                    ResolvePngTexturePath(packageDirectory, outputDirectory, text, requireSource) is { } path)
                {
                    aliases.Add(path);
                }
            }
        }
        if (runtime["materialSlots"] is JsonArray materialSlots)
        {
            foreach (var slot in materialSlots.OfType<JsonObject>())
            {
                AddKtx2Variant(slot, "mainTex", Ktx2Transfer.Srgb, packageDirectory, outputDirectory, requireSource, transfers);
                AddKtx2Variant(slot, "shadowTex", Ktx2Transfer.Srgb, packageDirectory, outputDirectory, requireSource, transfers);
                AddKtx2Variant(slot, "valueTex", Ktx2Transfer.Linear, packageDirectory, outputDirectory, requireSource, transfers);
                AddKtx2Variant(slot, "faceShadowTex", Ktx2Transfer.Linear, packageDirectory, outputDirectory, requireSource, transfers);
            }
        }
        if (runtime["textureRoles"] is not JsonArray textureRoles)
        {
            return;
        }
        foreach (var role in textureRoles.OfType<JsonObject>())
        {
            if (role["uri"] is not JsonValue valueNode ||
                !valueNode.TryGetValue<string>(out var value) ||
                ResolvePngTexturePath(packageDirectory, outputDirectory, value, requireSource) is not { } path)
            {
                continue;
            }
            var transfer = role["role"]?.GetValue<string>() is "value" or "faceShadow"
                ? Ktx2Transfer.Linear
                : Ktx2Transfer.Srgb;
            GetTransfers(transfers, path).Add(transfer);
        }
    }

    private static void AddKtx2Variant(
        JsonObject node,
        string propertyName,
        Ktx2Transfer transfer,
        string packageDirectory,
        string outputDirectory,
        bool requireSource,
        Dictionary<string, HashSet<Ktx2Transfer>> transfers
    )
    {
        if (node[propertyName] is JsonValue valueNode &&
            valueNode.TryGetValue<string>(out var value) &&
            ResolvePngTexturePath(packageDirectory, outputDirectory, value, requireSource) is { } path)
        {
            GetTransfers(transfers, path).Add(transfer);
        }
    }

    private static HashSet<Ktx2Transfer> GetTransfers(
        Dictionary<string, HashSet<Ktx2Transfer>> transfers,
        string path
    )
    {
        if (!transfers.TryGetValue(path, out var values))
        {
            values = new HashSet<Ktx2Transfer>();
            transfers[path] = values;
        }
        return values;
    }

    private static string? ResolvePngTexturePath(
        string packageDirectory,
        string outputDirectory,
        string value,
        bool requireSource = true
    )
    {
        var path = ResolveTexturePath(packageDirectory, outputDirectory, value);
        return path is not null &&
            Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            (!requireSource || File.Exists(path))
                ? path
                : null;
    }

    private static Ktx2VariantResult EncodeKtx2Variant(
        string outputDirectory,
        Ktx2VariantKey variant,
        string? sharedCacheDirectory
    )
    {
        var temporaryDirectory = Path.Combine(outputDirectory, ".ktx2-work");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.ktx2");
        try
        {
            string encodedPath;
            if (sharedCacheDirectory is null)
            {
                RunKtxCreate(variant.SourcePath, temporaryPath, variant.Transfer);
                encodedPath = temporaryPath;
            }
            else
            {
                var sourceHash = ComputeSha256Hex(variant.SourcePath);
                var transfer = variant.Transfer.ToString().ToLowerInvariant();
                var cachePath = Path.Combine(
                    sharedCacheDirectory,
                    "ktx2",
                    Ktx2EncoderVersion,
                    transfer,
                    sourceHash[..2],
                    sourceHash + ".ktx2"
                );
                if (!File.Exists(cachePath) || new FileInfo(cachePath).Length == 0)
                {
                    RunKtxCreate(variant.SourcePath, temporaryPath, variant.Transfer);
                    ContentAddressedFile.Replace(
                        cachePath,
                        publishPath => File.Copy(temporaryPath, publishPath)
                    );
                }
                encodedPath = cachePath;
            }

            var hash = ComputeSha256Hex(encodedPath);
            var storePath = Path.Combine(
                outputDirectory,
                "_texture_store",
                "sha256",
                hash[..2],
                hash + ".ktx2"
            );
            ContentAddressedFile.Ensure(
                storePath,
                hash,
                publishPath => File.Copy(encodedPath, publishPath)
            );
            return new Ktx2VariantResult(
                storePath,
                $"/_texture_store/sha256/{hash[..2]}/{hash}.ktx2"
            );
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void RunKtxCreate(string sourcePath, string outputPath, Ktx2Transfer transfer)
    {
        var executable = Environment.GetEnvironmentVariable("HARUKI_KTX_TOOL");
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = "ktx";
        }
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[]
        {
            "create",
            "--format", transfer == Ktx2Transfer.Srgb ? "R8G8B8A8_SRGB" : "R8G8B8A8_UNORM",
            "--assign-tf", transfer == Ktx2Transfer.Srgb ? "srgb" : "linear",
            "--encode", "uastc",
            "--uastc-quality", "2",
            "--threads", "1",
            "--zstd", "5",
            "--generate-mipmap",
            "--assign-texcoord-origin", "top-left",
            sourcePath,
            outputPath,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ktx.");
        if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"ktx timed out for {sourcePath}");
        }
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"ktx failed for {sourcePath}: {stderr.Trim()}");
        }
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException($"ktx produced no output for {sourcePath}");
        }
    }

    private static int RewriteRuntimeJsonToKtx2(
        string runtimeJsonPath,
        string outputDirectory,
        IReadOnlyDictionary<Ktx2VariantKey, Ktx2VariantResult> variants
    )
    {
        var packageDirectory = Path.GetDirectoryName(runtimeJsonPath)!;
        var node = ReadRuntimeJson(runtimeJsonPath);
        var rewritten = RewriteRuntimeNodeToKtx2(
            node,
            packageDirectory,
            outputDirectory,
            variants
        );
        if (rewritten > 0)
        {
            RuntimeJsonWriter.Write(
                runtimeJsonPath,
                node,
                JsonOptions,
                binaryArraySchema: RuntimeBinaryArraySchema.PartRuntime
            );
        }
        return rewritten;
    }

    private static int RewriteRuntimeNodeToKtx2(
        JsonObject node,
        string packageDirectory,
        string outputDirectory,
        IReadOnlyDictionary<Ktx2VariantKey, Ktx2VariantResult> variants
    )
    {
        var rewritten = 0;
        if (node["characterTextures"] is JsonObject characterTextures)
        {
            foreach (var pair in characterTextures.ToList())
            {
                if (pair.Value is JsonValue valueNode && valueNode.TryGetValue<string>(out var value) &&
                    TryResolveKtx2Variant(packageDirectory, outputDirectory, value, Ktx2Transfer.Srgb, variants, true, out var uri))
                {
                    characterTextures[pair.Key] = uri;
                    rewritten += 1;
                }
            }
        }
        if (node["materialSlots"] is JsonArray materialSlots)
        {
            foreach (var slot in materialSlots.OfType<JsonObject>())
            {
                rewritten += RewriteKtx2Property(slot, "mainTex", Ktx2Transfer.Srgb, packageDirectory, outputDirectory, variants);
                rewritten += RewriteKtx2Property(slot, "shadowTex", Ktx2Transfer.Srgb, packageDirectory, outputDirectory, variants);
                rewritten += RewriteKtx2Property(slot, "valueTex", Ktx2Transfer.Linear, packageDirectory, outputDirectory, variants);
                rewritten += RewriteKtx2Property(slot, "faceShadowTex", Ktx2Transfer.Linear, packageDirectory, outputDirectory, variants);
            }
        }
        if (node["textureRoles"] is JsonArray textureRoles)
        {
            foreach (var role in textureRoles.OfType<JsonObject>())
            {
                var transfer = role["role"]?.GetValue<string>() is "value" or "faceShadow"
                    ? Ktx2Transfer.Linear
                    : Ktx2Transfer.Srgb;
                rewritten += RewriteKtx2Property(role, "uri", transfer, packageDirectory, outputDirectory, variants);
            }
        }
        return rewritten;
    }

    private static int RewriteKtx2Property(
        JsonObject node,
        string propertyName,
        Ktx2Transfer transfer,
        string packageDirectory,
        string outputDirectory,
        IReadOnlyDictionary<Ktx2VariantKey, Ktx2VariantResult> variants
    )
    {
        if (node[propertyName] is JsonValue valueNode && valueNode.TryGetValue<string>(out var value) &&
            TryResolveKtx2Variant(packageDirectory, outputDirectory, value, transfer, variants, false, out var uri))
        {
            node[propertyName] = uri;
            return 1;
        }
        return 0;
    }

    private static bool TryResolveKtx2Variant(
        string packageDirectory,
        string outputDirectory,
        string value,
        Ktx2Transfer transfer,
        IReadOnlyDictionary<Ktx2VariantKey, Ktx2VariantResult> variants,
        bool allowOtherTransfer,
        out string uri
    )
    {
        uri = value;
        var sourcePath = ResolveTexturePath(packageDirectory, outputDirectory, value);
        if (sourcePath is null)
        {
            return false;
        }
        if (!variants.TryGetValue(new Ktx2VariantKey(sourcePath, transfer), out var result) &&
            (!allowOtherTransfer || !variants.TryGetValue(
                new Ktx2VariantKey(sourcePath, transfer == Ktx2Transfer.Srgb ? Ktx2Transfer.Linear : Ktx2Transfer.Srgb),
                out result
            )))
        {
            return false;
        }
        uri = result.RuntimePath;
        return true;
    }

    public TextureCompactionReport Compact(
        string outputDirectory,
        string pngOptimizeMode,
        int workers
    )
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        var textureFiles = EnumerateTextureFiles(outputDirectory).ToList();
        var entries = textureFiles
            .AsParallel()
            .Select(path => TextureFileEntry.FromPath(path))
            .ToList();

        var groups = entries
            .GroupBy(entry => entry.OriginalSha256, StringComparer.Ordinal)
            .Select(group => new TextureHashGroup(group.Key, group.ToList()))
            .ToList();
        var storeRoot = Path.Combine(outputDirectory, "_texture_store", "sha256");
        Directory.CreateDirectory(storeRoot);
        var workerCount = ResolveWorkerCount(workers);
        var optimized = OptimizeGroups(groups, storeRoot, pngOptimizeMode, workerCount);
        var pathMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in optimized)
        {
            foreach (var sourcePath in result.SourcePaths)
            {
                pathMap[Path.GetFullPath(sourcePath)] = result.RuntimePath;
            }
        }

        var runtimeFiles = EnumerateRuntimeJsonFiles(outputDirectory).ToList();
        var rewritten = RewriteRuntimeJsonFiles(
            runtimeFiles,
            outputDirectory,
            pathMap,
            workerCount
        );
        DeleteReplacedTextureFiles(entries.Select(entry => entry.Path), outputDirectory, workerCount);
        ValidateRuntimeTexturePaths(runtimeFiles, outputDirectory, workerCount);

        var report = new TextureCompactionReport(
            Version: 1,
            TextureFileCount: entries.Count,
            UniqueHashCount: groups.Count,
            DuplicateFileCount: groups.Sum(group => group.Entries.Count - 1),
            OriginalBytes: entries.Sum(entry => entry.Size),
            StoredBytes: optimized.Sum(result => result.StoredBytes),
            SavedBytes: entries.Sum(entry => entry.Size) - optimized.Sum(result => result.StoredBytes),
            RewrittenReferenceCount: rewritten,
            PngOptimizeMode: NormalizePngOptimizeMode(pngOptimizeMode),
            WorkerCount: workerCount,
            Groups: optimized
                .OrderByDescending(result => result.SourcePaths.Count)
                .ThenBy(result => result.OriginalSha256, StringComparer.Ordinal)
                .Select(result => new TextureCompactionGroupReport(
                    OriginalSha256: result.OriginalSha256,
                    OptimizedSha256: result.OptimizedSha256,
                    SourceCount: result.SourcePaths.Count,
                    OriginalBytes: result.OriginalBytes,
                    StoredBytes: result.StoredBytes,
                    RuntimePath: result.RuntimePath
                ))
                .ToList()
        );
        var reportPath = Path.Combine(outputDirectory, "texture-compaction-report.json");
        File.WriteAllBytes(reportPath, JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions));
        return report;
    }

    private static IEnumerable<string> EnumerateTextureFiles(string outputDirectory)
    {
        var sourcesRoot = Path.Combine(outputDirectory, "parts", "_sources");
        if (!Directory.Exists(sourcesRoot))
        {
            return Array.Empty<string>();
        }
        return Directory.EnumerateFiles(sourcesRoot, "*.png", SearchOption.AllDirectories)
            .Where(path => path.Split(Path.DirectorySeparatorChar).Contains("textures"));
    }

    private static IEnumerable<string> EnumerateRuntimeJsonFiles(string outputDirectory)
    {
        var sourcesRoot = Path.Combine(outputDirectory, "parts", "_sources");
        if (!Directory.Exists(sourcesRoot))
        {
            return Array.Empty<string>();
        }
        return Directory.EnumerateFiles(sourcesRoot, "part-runtime.msgpack.br", SearchOption.AllDirectories)
            .Select(path => path[..^".msgpack.br".Length] + ".json")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<TextureStoreResult> OptimizeGroups(
        IReadOnlyList<TextureHashGroup> groups,
        string storeRoot,
        string pngOptimizeMode,
        int workers
    )
    {
        var results = new ConcurrentBag<TextureStoreResult>();
        var errors = new ConcurrentBag<Exception>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
        Parallel.ForEach(groups, options, group =>
        {
            try
            {
                results.Add(StoreGroup(group, storeRoot, pngOptimizeMode));
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });
        if (!errors.IsEmpty)
        {
            throw new InvalidOperationException($"Texture compaction failed: {errors.First().Message}", errors.First());
        }
        return results
            .OrderBy(result => result.OriginalSha256, StringComparer.Ordinal)
            .ToList();
    }

    private static TextureStoreResult StoreGroup(
        TextureHashGroup group,
        string storeRoot,
        string pngOptimizeMode
    )
    {
        var canonical = group.Entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).First();
        var shard = canonical.OriginalSha256[..2];
        var storeDirectory = Path.Combine(storeRoot, shard);
        Directory.CreateDirectory(storeDirectory);
        var storePath = Path.Combine(storeDirectory, canonical.OriginalSha256 + ".png");
        var tempPath = Path.Combine(storeDirectory, $".{canonical.OriginalSha256}.{Guid.NewGuid():N}.tmp.png");
        try
        {
            File.Copy(canonical.Path, tempPath, overwrite: true);
            if (NormalizePngOptimizeMode(pngOptimizeMode) == "oxipng")
            {
                RunOxipng(tempPath);
                if (new FileInfo(tempPath).Length > canonical.Size)
                {
                    File.Copy(canonical.Path, tempPath, overwrite: true);
                }
            }
            var optimizedSha256 = ComputeSha256Hex(tempPath);
            if (!File.Exists(storePath))
            {
                File.Move(tempPath, storePath);
            }
            else
            {
                File.Delete(tempPath);
            }
            return new TextureStoreResult(
                OriginalSha256: canonical.OriginalSha256,
                OptimizedSha256: optimizedSha256,
                OriginalBytes: group.Entries.Sum(entry => entry.Size),
                StoredBytes: new FileInfo(storePath).Length,
                RuntimePath: $"/_texture_store/sha256/{shard}/{canonical.OriginalSha256}.png",
                SourcePaths: group.Entries.Select(entry => entry.Path).ToList()
            );
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static int RewriteRuntimeJsonFiles(
        IReadOnlyList<string> runtimeFiles,
        string outputDirectory,
        IReadOnlyDictionary<string, string> pathMap,
        int workers
    )
    {
        var rewritten = 0;
        var errors = new ConcurrentBag<Exception>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
        Parallel.ForEach(runtimeFiles, options, runtimePath =>
        {
            try
            {
                Interlocked.Add(
                    ref rewritten,
                    RewriteRuntimeJson(runtimePath, outputDirectory, pathMap)
                );
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });
        ThrowIfAny(errors, "Texture runtime JSON rewrite failed");
        return rewritten;
    }

    private static int RewriteRuntimeJson(
        string runtimeJsonPath,
        string outputDirectory,
        IReadOnlyDictionary<string, string> pathMap
    )
    {
        var packageDirectory = Path.GetDirectoryName(runtimeJsonPath)
            ?? throw new InvalidOperationException($"Runtime JSON has no parent directory: {runtimeJsonPath}");
        var node = ReadRuntimeJson(runtimeJsonPath);
        var rewritten = 0;
        if (node["characterTextures"] is JsonObject characterTextures)
        {
            foreach (var pair in characterTextures.ToList())
            {
                if (pair.Value is not JsonValue valueNode ||
                    !valueNode.TryGetValue<string>(out var value))
                {
                    continue;
                }
                if (TryRewriteTexturePath(packageDirectory, outputDirectory, value, pathMap, out var rewrittenPath))
                {
                    characterTextures[pair.Key] = rewrittenPath;
                    rewritten += 1;
                }
            }
        }
        if (node["materialSlots"] is JsonArray materialSlots)
        {
            foreach (var materialSlot in materialSlots.OfType<JsonObject>())
            {
                foreach (var propertyName in new[] { "mainTex", "shadowTex", "valueTex", "faceShadowTex" })
                {
                    if (materialSlot[propertyName] is not JsonValue valueNode ||
                        !valueNode.TryGetValue<string>(out var value))
                    {
                        continue;
                    }
                    if (TryRewriteTexturePath(packageDirectory, outputDirectory, value, pathMap, out var rewrittenPath))
                    {
                        materialSlot[propertyName] = rewrittenPath;
                        rewritten += 1;
                    }
                }
            }
        }
        if (node["textureRoles"] is JsonArray textureRoles)
        {
            foreach (var textureRole in textureRoles.OfType<JsonObject>())
            {
                if (textureRole["uri"] is not JsonValue valueNode ||
                    !valueNode.TryGetValue<string>(out var value))
                {
                    continue;
                }
                if (TryRewriteTexturePath(packageDirectory, outputDirectory, value, pathMap, out var rewrittenPath))
                {
                    textureRole["uri"] = rewrittenPath;
                    rewritten += 1;
                }
            }
        }
        RuntimeJsonWriter.Write(
            runtimeJsonPath,
            node,
            JsonOptions,
            binaryArraySchema: RuntimeBinaryArraySchema.PartRuntime
        );
        return rewritten;
    }

    private static JsonObject ReadRuntimeJson(string runtimeJsonPath)
    {
        return RuntimeJsonWriter.ReadJsonObject(runtimeJsonPath);
    }

    private static bool TryRewriteTexturePath(
        string packageDirectory,
        string outputDirectory,
        string value,
        IReadOnlyDictionary<string, string> pathMap,
        out string rewrittenPath
    )
    {
        rewrittenPath = value;
        var resolved = ResolveTexturePath(packageDirectory, outputDirectory, value);
        if (resolved is null)
        {
            return false;
        }
        if (!pathMap.TryGetValue(resolved, out var mapped))
        {
            return false;
        }
        rewrittenPath = mapped;
        return true;
    }

    private static string? ResolveTexturePath(string packageDirectory, string outputDirectory, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return null;
        }
        var path = value.StartsWith("/", StringComparison.Ordinal)
            ? Path.Combine(outputDirectory, value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(packageDirectory, value.Replace('/', Path.DirectorySeparatorChar));
        return Path.GetFullPath(path);
    }

    private static void DeleteReplacedTextureFiles(IEnumerable<string> paths, string outputDirectory, int workers)
    {
        var errors = new ConcurrentBag<Exception>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
        Parallel.ForEach(paths.Distinct(StringComparer.Ordinal), options, path =>
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });
        ThrowIfAny(errors, "Texture file cleanup failed");

        var sourcesRoot = Path.Combine(outputDirectory, "parts", "_sources");
        if (!Directory.Exists(sourcesRoot))
        {
            return;
        }
        DeleteEmptyTextureDirectories(sourcesRoot, workerCount: workers);
    }

    private static void DeleteEmptyTextureDirectories(string sourcesRoot, int workerCount)
    {
        var textureRoots = Directory
            .EnumerateDirectories(sourcesRoot, "textures", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToList();
        if (textureRoots.Count == 0)
        {
            return;
        }

        var directories = textureRoots
            .SelectMany(root => Directory
                .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Append(root))
            .Distinct(StringComparer.Ordinal)
            .Select(path => new
            {
                Path = path,
                Depth = GetDirectoryDepth(path)
            })
            .GroupBy(entry => entry.Depth)
            .OrderByDescending(group => group.Key)
            .ToList();

        var options = new ParallelOptions { MaxDegreeOfParallelism = workerCount };
        var errors = new ConcurrentBag<Exception>();
        foreach (var depthGroup in directories)
        {
            Parallel.ForEach(depthGroup, options, entry =>
            {
                try
                {
                    DeleteDirectoryIfEmpty(entry.Path);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });
        }
        ThrowIfAny(errors, "Texture directory cleanup failed");
    }

    private static int GetDirectoryDepth(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static void DeleteDirectoryIfEmpty(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }
        try
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException) when (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
        }
    }

    private static void ValidateRuntimeTexturePaths(IReadOnlyList<string> runtimeFiles, string outputDirectory, int workers)
    {
        var errors = new ConcurrentBag<Exception>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
        Parallel.ForEach(runtimeFiles, options, runtimePath =>
        {
            try
            {
                var packageDirectory = Path.GetDirectoryName(runtimePath)
                    ?? throw new InvalidOperationException($"Runtime JSON has no parent directory: {runtimePath}");
                var node = ReadRuntimeJson(runtimePath);
                foreach (var value in EnumerateTextureValues(node))
                {
                    var resolved = ResolveTexturePath(packageDirectory, outputDirectory, value);
                    if (resolved is not null && !File.Exists(resolved))
                    {
                        throw new InvalidOperationException($"Runtime JSON references missing texture: {runtimePath} -> {value}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });
        ThrowIfAny(errors, "Texture runtime JSON validation failed");
    }

    private static IEnumerable<string> EnumerateTextureValues(JsonObject node)
    {
        if (node["characterTextures"] is JsonObject characterTextures)
        {
            foreach (var value in characterTextures.Select(pair => pair.Value).OfType<JsonValue>())
            {
                if (value.TryGetValue<string>(out var text))
                {
                    yield return text;
                }
            }
        }
        if (node["materialSlots"] is JsonArray materialSlots)
        {
            foreach (var materialSlot in materialSlots.OfType<JsonObject>())
            {
                foreach (var propertyName in new[] { "mainTex", "shadowTex", "valueTex", "faceShadowTex" })
                {
                    if (materialSlot[propertyName] is JsonValue value &&
                        value.TryGetValue<string>(out var text))
                    {
                        yield return text;
                    }
                }
            }
        }
        if (node["textureRoles"] is JsonArray textureRoles)
        {
            foreach (var textureRole in textureRoles.OfType<JsonObject>())
            {
                if (textureRole["uri"] is JsonValue value &&
                    value.TryGetValue<string>(out var text))
                {
                    yield return text;
                }
            }
        }
    }

    private static void RunOxipng(string pngPath)
    {
        var startInfo = new ProcessStartInfo("oxipng")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "-o", "2", "--strip", "safe", "--threads", "1", "--quiet", pngPath })
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start oxipng.");
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"oxipng timed out for {pngPath}");
        }
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"oxipng failed for {pngPath}: {stderr.Trim()}");
        }
    }

    private static int ResolveWorkerCount(int workers)
    {
        if (workers > 0)
        {
            return workers;
        }
        return Math.Max(1, Math.Min(4, Environment.ProcessorCount));
    }

    private static string NormalizePngOptimizeMode(string mode)
    {
        return string.IsNullOrWhiteSpace(mode) ? "oxipng" : mode.Trim().ToLowerInvariant();
    }

    private static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ThrowIfAny(ConcurrentBag<Exception> errors, string message)
    {
        if (!errors.IsEmpty)
        {
            throw new InvalidOperationException($"{message}: {errors.First().Message}", errors.First());
        }
    }

    private sealed record TextureFileEntry(string Path, long Size, string OriginalSha256)
    {
        public static TextureFileEntry FromPath(string path)
        {
            return new TextureFileEntry(
                System.IO.Path.GetFullPath(path),
                new FileInfo(path).Length,
                ComputeSha256Hex(path)
            );
        }
    }

    private sealed record TextureHashGroup(string OriginalSha256, IReadOnlyList<TextureFileEntry> Entries);

    private sealed record TextureStoreResult(
        string OriginalSha256,
        string OptimizedSha256,
        long OriginalBytes,
        long StoredBytes,
        string RuntimePath,
        IReadOnlyList<string> SourcePaths
    );

    private enum Ktx2Transfer
    {
        Srgb,
        Linear,
    }

    private sealed record Ktx2VariantKey(string SourcePath, Ktx2Transfer Transfer);

    private sealed record Ktx2VariantResult(string StoredPath, string RuntimePath);
}

public sealed record TextureCompactionReport(
    int Version,
    int TextureFileCount,
    int UniqueHashCount,
    int DuplicateFileCount,
    long OriginalBytes,
    long StoredBytes,
    long SavedBytes,
    int RewrittenReferenceCount,
    string PngOptimizeMode,
    int WorkerCount,
    IReadOnlyList<TextureCompactionGroupReport> Groups
);

public sealed record TextureCompactionGroupReport(
    string OriginalSha256,
    string OptimizedSha256,
    int SourceCount,
    long OriginalBytes,
    long StoredBytes,
    string RuntimePath
);

public sealed record TextureStoreOptimizationReport(
    int Version,
    int TextureFileCount,
    int OptimizedFileCount,
    long OriginalBytes,
    long StoredBytes,
    long SavedBytes,
    string PngOptimizeMode,
    int WorkerCount
);

public sealed record Ktx2TranscodeReport(
    int Version,
    int SourceTextureCount,
    int ConvertedVariantCount,
    int RewrittenReferenceCount,
    long OriginalBytes,
    long StoredBytes,
    int WorkerCount
);
