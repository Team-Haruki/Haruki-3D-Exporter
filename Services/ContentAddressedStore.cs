using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PjskBundle2Parts.Services;

public sealed class ContentAddressedStore
{
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
        var newContentCount = 0;
        var reusedContentCount = 0;
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
            ReusedBytes: reusedBytes
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
            var size = new FileInfo(sourcePath).Length;
            var hash = ComputeSha256Hex(sourcePath);
            var extension = sourcePath.EndsWith(".msgpack.br", StringComparison.OrdinalIgnoreCase)
                ? ".msgpack.br"
                : Path.GetExtension(sourcePath).ToLowerInvariant();
            var storePath = Path.Combine(storeRoot, hash[..2], hash + extension);
            var created = EnsureCanonical(sourcePath, storePath, hash);
            ReplaceWithHardLink(sourcePath, storePath);
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
    long ReusedBytes
);
