using System.Security.Cryptography;

namespace PjskBundle2Parts.Services;

internal static class ContentAddressedFile
{
    public static bool Ensure(string path, string expectedHash, Action<string> writeTemporaryFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            ValidateHash(path, expectedHash);
            return false;
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            writeTemporaryFile(temporaryPath);
            ValidateHash(temporaryPath, expectedHash);
            try
            {
                File.Move(temporaryPath, path, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                ValidateHash(path, expectedHash);
                return false;
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void ValidateHash(string path, string expectedHash)
    {
        using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Shared content hash mismatch for {path}: expected {expectedHash}, got {actualHash}."
            );
        }
    }
}
