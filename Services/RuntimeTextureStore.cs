using System.Security.Cryptography;

namespace PjskBundle2Parts.Services;

public sealed class RuntimeTextureStore
{
    private readonly string outputDirectory;

    public RuntimeTextureStore(string outputDirectory)
    {
        this.outputDirectory = Path.GetFullPath(outputDirectory);
    }

    public string StorePng(ReadOnlySpan<byte> png)
    {
        var hash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        var shard = hash[..2];
        var directory = Path.Combine(outputDirectory, "_texture_store", "sha256", shard);
        var path = Path.Combine(directory, hash + ".png");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{hash}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, png.ToArray());
                try
                {
                    File.Move(temporaryPath, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Another exporter process won the exact-content race.
                }
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        return $"/_texture_store/sha256/{shard}/{hash}.png";
    }
}
