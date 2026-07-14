using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PjskBundle2Parts.Services;

public sealed class ContentAddressedStore
{
    private const string StateFileName = "content-addressed-store-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ContentAddressedStoreReport Compact(string outputDirectory, string sharedStoreDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        sharedStoreDirectory = Path.GetFullPath(sharedStoreDirectory);
        Directory.CreateDirectory(sharedStoreDirectory);

        var textures = EnumerateTextures(outputDirectory).ToList();
        var partRuntimes = EnumeratePartRuntimes(outputDirectory).ToList();
        var previousState = LoadState(outputDirectory);
        var nextState = new Dictionary<string, ContentAddressedStoreStateEntry>(StringComparer.Ordinal);
        var newContentCount = 0;
        var reusedContentCount = 0;
        var unchangedFileCount = 0;
        long reusedBytes = 0;

        foreach (var file in textures)
        {
            CompactFile(file, Path.Combine(sharedStoreDirectory, "textures", "sha256"));
        }
        foreach (var file in partRuntimes)
        {
            CompactFile(file, Path.Combine(sharedStoreDirectory, "part-runtime", "sha256"));
        }

        var report = new ContentAddressedStoreReport(
            Version: 1,
            TextureFileCount: textures.Count,
            PartRuntimeFileCount: partRuntimes.Count,
            NewContentCount: newContentCount,
            ReusedContentCount: reusedContentCount,
            UnchangedFileCount: unchangedFileCount,
            ReusedBytes: reusedBytes
        );
        RuntimeJsonWriter.Write(
            Path.Combine(outputDirectory, StateFileName),
            nextState,
            JsonOptions,
            RuntimeJsonWriter.Json
        );
        RuntimeJsonWriter.Write(
            Path.Combine(outputDirectory, "content-addressed-store-report.json"),
            report,
            JsonOptions,
            RuntimeJsonWriter.Json
        );
        return report;

        void CompactFile(string sourcePath, string storeRoot)
        {
            var relativePath = Path.GetRelativePath(outputDirectory, sourcePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var sourceInfo = new FileInfo(sourcePath);
            var previousStorePath = previousState.TryGetValue(relativePath, out var previous)
                ? Path.Combine(storeRoot, previous.Hash[..2], previous.Hash + previous.Extension)
                : null;
            if (previous is not null &&
                previous.Length == sourceInfo.Length &&
                previous.LastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks &&
                IsReadOnly(sourcePath) &&
                previousStorePath is not null &&
                File.Exists(previousStorePath) &&
                new FileInfo(previousStorePath).Length == previous.Length &&
                IsReadOnly(previousStorePath))
            {
                nextState[relativePath] = previous;
                unchangedFileCount += 1;
                return;
            }

            var size = sourceInfo.Length;
            var hash = ComputeSha256Hex(sourcePath);
            var extension = sourcePath.EndsWith(".msgpack.br", StringComparison.OrdinalIgnoreCase)
                ? ".msgpack.br"
                : Path.GetExtension(sourcePath).ToLowerInvariant();
            var storePath = Path.Combine(storeRoot, hash[..2], hash + extension);
            var created = EnsureCanonical(sourcePath, storePath, hash);
            ProtectCanonical(storePath);
            ReplaceWithHardLink(sourcePath, storePath);
            var linkedInfo = new FileInfo(sourcePath);
            nextState[relativePath] = new ContentAddressedStoreStateEntry(
                Length: linkedInfo.Length,
                LastWriteUtcTicks: linkedInfo.LastWriteTimeUtc.Ticks,
                Hash: hash,
                Extension: extension
            );
            if (created)
            {
                newContentCount += 1;
            }
            else
            {
                reusedContentCount += 1;
                reusedBytes += size;
            }
        }
    }

    private static Dictionary<string, ContentAddressedStoreStateEntry> LoadState(string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, StateFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, ContentAddressedStoreStateEntry>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, ContentAddressedStoreStateEntry>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) is { } state
            ? new Dictionary<string, ContentAddressedStoreStateEntry>(state, StringComparer.Ordinal)
            : new Dictionary<string, ContentAddressedStoreStateEntry>(StringComparer.Ordinal);
    }

    private static bool IsReadOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }

        var mode = File.GetUnixFileMode(path);
        return (mode & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0;
    }

    private static void ProtectCanonical(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
        );
    }

    private static IEnumerable<string> EnumerateTextures(string outputDirectory)
    {
        var root = Path.Combine(outputDirectory, "_texture_store", "sha256");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            : Array.Empty<string>();
    }

    private static IEnumerable<string> EnumeratePartRuntimes(string outputDirectory)
    {
        var root = Path.Combine(outputDirectory, "parts", "_sources");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "part-runtime.msgpack.br", SearchOption.AllDirectories)
            : Array.Empty<string>();
    }

    private static bool EnsureCanonical(string sourcePath, string storePath, string expectedHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        if (File.Exists(storePath))
        {
            ValidateHash(storePath, expectedHash);
            return false;
        }

        var tempPath = $"{storePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: false);
            ValidateHash(tempPath, expectedHash);
            try
            {
                File.Move(tempPath, storePath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(storePath))
            {
                ValidateHash(storePath, expectedHash);
                return false;
            }
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void ReplaceWithHardLink(string path, string canonicalPath)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.cas";
        try
        {
            CreateHardLink(tempPath, canonicalPath);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void CreateHardLink(string path, string canonicalPath)
    {
        var result = OperatingSystem.IsWindows()
            ? CreateHardLinkWindows(path, canonicalPath, IntPtr.Zero) ? 0 : -1
            : CreateHardLinkUnix(canonicalPath, path);
        if (result != 0)
        {
            throw new IOException(
                $"Failed to hard-link shared content '{canonicalPath}' to '{path}'. " +
                "The output and shared content store must be on the same filesystem.",
                new Win32Exception(Marshal.GetLastWin32Error())
            );
        }
    }

    private static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ValidateHash(string path, string expectedHash)
    {
        var actualHash = ComputeSha256Hex(path);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Shared content hash mismatch for {path}: expected {expectedHash}, got {actualHash}.");
        }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(string canonicalPath, string newPath);

    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string newPath, string existingPath, IntPtr securityAttributes);
}

public sealed record ContentAddressedStoreReport(
    int Version,
    int TextureFileCount,
    int PartRuntimeFileCount,
    int NewContentCount,
    int ReusedContentCount,
    int UnchangedFileCount,
    long ReusedBytes
);

public sealed record ContentAddressedStoreStateEntry(
    long Length,
    long LastWriteUtcTicks,
    string Hash,
    string Extension
);
