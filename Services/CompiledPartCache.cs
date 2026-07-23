using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class CompiledPartCache
{
    private const string Schema = "0415-compiled-part-8";
    private static readonly JsonSerializerOptions RuntimeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly string cacheRoot;
    private readonly string sharedContentRoot;
    private readonly string assetRoot;
    private readonly BundleHashIndex bundleHashes;
    private readonly bool restoreKtx2;
    private readonly TextureCompactor textureCompactor = new();
    private readonly ConcurrentDictionary<string, CachedFileHash> fileHashes = new(StringComparer.Ordinal);

    public int BundleHashIndexHits { get; private set; }
    public int FileHashComputations { get; private set; }

    public CompiledPartCache(
        string cacheRoot,
        string sharedContentRoot,
        string assetRoot,
        string? bundleHashIndex,
        bool restoreKtx2 = false
    )
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot);
        this.sharedContentRoot = Path.GetFullPath(sharedContentRoot);
        this.assetRoot = Path.GetFullPath(assetRoot);
        this.restoreKtx2 = restoreKtx2;
        bundleHashes = new BundleHashIndex(bundleHashIndex);
    }

    public bool TryRestore(
        PartRegistryEntry entry,
        ResolvedBundleInput input,
        string assetRoot,
        string outputDirectory,
        IReadOnlyDictionary<string, float> characterHeightMetersById,
        out PartPackageExportResult? result
    )
    {
        result = null;
        var cachePath = EntryPath(Fingerprint(entry, input, BundleLoadDependencyMode.Default));
        if (!File.Exists(cachePath))
        {
            return false;
        }
        var cached = JsonSerializer.Deserialize<CompiledPartCacheEntry>(File.ReadAllText(cachePath));
        if (cached is null || cached.Schema != Schema)
        {
            return false;
        }
        if (!string.Equals(
                cached.InputFingerprint,
                Fingerprint(entry, input, cached.DependencyMode),
                StringComparison.Ordinal
            ))
        {
            return false;
        }
        var coreObject = ObjectPath(cached.CoreHash);
        var deltaObject = ObjectPath(cached.DeltaHash);
        if (!File.Exists(coreObject) || !File.Exists(deltaObject))
        {
            return false;
        }

        var packageDirectory = Path.Combine(
            outputDirectory,
            entry.PackagePath.Replace('/', Path.DirectorySeparatorChar)
        );
        var delta = RuntimeJsonWriter.ReadJsonObject(deltaObject);
        var restoredKtx2 = restoreKtx2 && textureCompactor.TryRestoreCachedKtx2(
            delta,
            packageDirectory,
            outputDirectory,
            sharedContentRoot
        );
        if (!restoredKtx2 && cached.TextureHashes.Any(hash => !File.Exists(SharedTexturePath(hash))))
        {
            return false;
        }
        var runtimePath = Path.Combine(packageDirectory, "part-runtime.json");
        var coreRelativePath = CoreRelativePath(entry);
        var corePath = Path.Combine(
            outputDirectory,
            coreRelativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        Directory.CreateDirectory(Path.GetDirectoryName(corePath)!);
        ContentAddressedFile.Replace(
            RuntimeJsonWriter.MessagePackBrotliPath(corePath),
            temporaryPath => File.Copy(coreObject, temporaryPath)
        );
        foreach (var hash in restoredKtx2 ? Array.Empty<string>() : cached.TextureHashes)
        {
            var target = Path.Combine(
                outputDirectory,
                "_texture_store",
                "sha256",
                hash[..2],
                hash + ".png"
            );
            if (!File.Exists(target))
            {
                ContentAddressedFile.Ensure(
                    target,
                    hash,
                    temporaryPath => File.Copy(SharedTexturePath(hash), temporaryPath)
                );
            }
        }

        delta["version"] = "0415-part-delta-3";
        delta["corePath"] = coreRelativePath;
        delta["part"] = JsonSerializer.SerializeToNode(BuildIdentity(entry), RuntimeJsonOptions);
        delta["source"] = JsonSerializer.SerializeToNode(new PartRuntimeSource(
            BundlePath: input.ResolvedBundlePath,
            ColorVariationBundlePath: entry.ColorVariationBundlePath,
            AssetRootRelativeBundlePath: TryRelativePath(assetRoot, input.ResolvedBundlePath)
        ), RuntimeJsonOptions);
        PatchMount(delta, entry);
        PatchManifest(delta, entry, input, characterHeightMetersById);
        RuntimeJsonWriter.Write(
            runtimePath,
            delta,
            RuntimeJsonOptions,
            binaryArraySchema: RuntimeBinaryArraySchema.PartRuntime
        );
        var warnings = delta["warnings"] is JsonArray warningArray
            ? warningArray.Select(node => node?.GetValue<string>()).Where(value => value is not null).Cast<string>().ToList()
            : new List<string>();
        result = new PartPackageExportResult(
            entry,
            RuntimeJsonWriter.MessagePackBrotliPath(runtimePath),
            warnings,
            CoreRuntimePath: RuntimeJsonWriter.MessagePackBrotliPath(corePath),
            TextureHashes: cached.TextureHashes
        );
        return true;
    }

    public void Store(
        PartRegistryEntry entry,
        ResolvedBundleInput input,
        PartPackageExportResult result,
        BundleLoadDependencyMode dependencyMode
    )
    {
        var runtimePath = result.RuntimePath;
        if (!runtimePath.EndsWith(".msgpack.br", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (result.CoreRuntimePath is not { } corePath ||
            !corePath.EndsWith(".msgpack.br", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(corePath))
        {
            return;
        }
        var lookupFingerprint = Fingerprint(entry, input, BundleLoadDependencyMode.Default);
        var cachePath = EntryPath(lookupFingerprint);
        var textureHashes = result.TextureHashes?.Distinct(StringComparer.Ordinal).ToList()
            ?? new List<string>();
        var cached = new CompiledPartCacheEntry(
            Schema,
            Fingerprint(entry, input, dependencyMode),
            dependencyMode,
            EnsureObject(corePath),
            EnsureObject(runtimePath),
            textureHashes
        );
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var temporaryPath = cachePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cached));
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string Fingerprint(
        PartRegistryEntry entry,
        ResolvedBundleInput input,
        BundleLoadDependencyMode dependencyMode
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Schema);
        Append(hash, ResolvePartType(entry));
        Append(hash, Path.GetFileName(input.ResolvedBundlePath));
        Append(hash, dependencyMode.ToString());
        foreach (var path in BundleDependencyResolver.ResolveLoadBundlePaths(input, dependencyMode)
            .Append(entry.ColorVariationBundlePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, Path.GetFileName(path));
            hash.AppendData(FileHash(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private byte[] FileHash(string path)
    {
        if (bundleHashes.TryGet(assetRoot, path, out var indexedHash))
        {
            BundleHashIndexHits += 1;
            return indexedHash;
        }
        var info = new FileInfo(path);
        var cached = fileHashes.GetOrAdd(path, _ => new CachedFileHash(
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            ComputeAndCountFileHash(path)
        ));
        if (cached.Length == info.Length && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
        {
            return cached.Hash;
        }
        cached = new CachedFileHash(info.Length, info.LastWriteTimeUtc.Ticks, ComputeAndCountFileHash(path));
        fileHashes[path] = cached;
        return cached.Hash;
    }

    private static byte[] ComputeFileHash(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return SHA256.HashData(stream);
    }

    private byte[] ComputeAndCountFileHash(string path)
    {
        FileHashComputations += 1;
        return ComputeFileHash(path);
    }

    private string EnsureObject(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var target = ObjectPath(hash);
        if (!File.Exists(target))
        {
            ContentAddressedFile.Ensure(
                target,
                hash,
                temporaryPath => File.Copy(sourcePath, temporaryPath)
            );
        }
        return hash;
    }

    private string EntryPath(string fingerprint) =>
        Path.Combine(cacheRoot, "entries", fingerprint[..2], fingerprint + ".json");

    private string ObjectPath(string hash) =>
        Path.Combine(cacheRoot, "objects", hash[..2], hash + ".msgpack.br");

    private string SharedTexturePath(string hash) =>
        Path.Combine(sharedContentRoot, "textures", "sha256", hash[..2], hash + ".png");

    private static string CoreRelativePath(PartRegistryEntry entry)
    {
        var baseKey = entry.BaseSourceKey ?? entry.SourceKey ?? entry.PackagePath;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseKey))).ToLowerInvariant();
        return $"parts/_cores/{ResolvePartType(entry)}/{hash}/part-runtime-core.msgpack.br";
    }

    private static string ResolvePartType(PartRegistryEntry entry)
    {
        if (string.Equals(entry.HeadCostume3dAssetbundleType, "head_only", StringComparison.OrdinalIgnoreCase))
        {
            return "head_optional";
        }
        return string.Equals(entry.PartType, "accessory", StringComparison.OrdinalIgnoreCase)
            ? "head_optional"
            : entry.PartType.ToLowerInvariant();
    }

    private static PartRuntimeIdentity BuildIdentity(PartRegistryEntry entry) => new(
        entry.Costume3dId,
        ResolvePartType(entry),
        entry.CharacterId,
        entry.Unit,
        entry.Name,
        entry.ColorId,
        entry.ColorName,
        entry.Costume3dGroupId,
        entry.ModelAssetbundleName,
        entry.HeadCostume3dAssetbundleType
    );

    private static void PatchMount(JsonObject delta, PartRegistryEntry entry)
    {
        if (delta["mount"] is not JsonObject mount)
        {
            return;
        }
        mount["attachNode"] = entry.AttachNode;
        mount["expectedSkeletonId"] = entry.CharacterId.ToString("00");
    }

    private static void PatchManifest(
        JsonObject delta,
        PartRegistryEntry entry,
        ResolvedBundleInput input,
        IReadOnlyDictionary<string, float> characterHeightMetersById
    )
    {
        if (delta["manifest"] is not JsonObject manifest)
        {
            return;
        }
        var partType = ResolvePartType(entry);
        manifest["id"] = $"{partType}-{entry.CharacterId:00}-{entry.Costume3dId}-{entry.Unit ?? "default"}";
        manifest["displayName"] = entry.Name;
        manifest["characterId"] = entry.CharacterId.ToString("00");
        manifest["characterHeightMeters"] = CharacterHeightResolver.ResolveMeters(characterHeightMetersById, entry.CharacterId);
        manifest["attachNode"] = entry.AttachNode;
        if (manifest["source"] is JsonObject source)
        {
            source["bundleRoot"] = input.ResolvedBundlePath;
        }
    }

    private static string? TryRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? null
            : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData(new byte[] { 0 });
    }
}

public sealed record CompiledPartCacheEntry(
    string Schema,
    string InputFingerprint,
    BundleLoadDependencyMode DependencyMode,
    string CoreHash,
    string DeltaHash,
    IReadOnlyList<string> TextureHashes
);

internal sealed record CachedFileHash(long Length, long LastWriteUtcTicks, byte[] Hash);
