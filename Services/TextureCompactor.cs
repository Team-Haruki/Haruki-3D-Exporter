using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PjskBundle2Parts.Services;

public sealed class TextureCompactor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public TextureCompactionReport Compact(
        string outputDirectory,
        string runtimeJsonOutput,
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
            runtimeJsonOutput,
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
        return Directory.EnumerateFiles(sourcesRoot, "part-runtime.json*", SearchOption.AllDirectories)
            .Select(path => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? path[..^3]
                : path)
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
        string runtimeJsonOutput,
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
                    RewriteRuntimeJson(runtimePath, outputDirectory, pathMap, runtimeJsonOutput)
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
        IReadOnlyDictionary<string, string> pathMap,
        string runtimeJsonOutput
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
        RuntimeJsonWriter.Write(runtimeJsonPath, node, JsonOptions, runtimeJsonOutput);
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
        if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out _))
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
