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
            var bytes = png.ToArray();
            ContentAddressedFile.Ensure(
                path,
                hash,
                temporaryPath => File.WriteAllBytes(temporaryPath, bytes)
            );
        }
        return $"/_texture_store/sha256/{shard}/{hash}.png";
    }
}
