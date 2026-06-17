namespace PjskBundle2Parts.Services;

public sealed class SekaiBundleDecryptor
{
    private static readonly byte[] SekaiMagic = { 0x10, 0x00, 0x00, 0x00 };

    public DecryptedBundleHandle PrepareReadableBundle(string bundlePath)
    {
        using var source = File.Open(bundlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[4];
        var read = source.Read(header);
        source.Position = 0;

        if (read == 4 && header.SequenceEqual(SekaiMagic))
        {
            var tempPath = CreateTempSiblingPath(bundlePath);
            using var target = File.Create(tempPath);
            DecryptTo(source, target);
            return new DecryptedBundleHandle(tempPath, deleteOnDispose: true);
        }

        return new DecryptedBundleHandle(bundlePath, deleteOnDispose: false);
    }

    private static void DecryptTo(Stream source, Stream target)
    {
        Span<byte> magic = stackalloc byte[4];
        if (source.Read(magic) != 4 || !magic.SequenceEqual(SekaiMagic))
        {
            source.Position = 0;
            source.CopyTo(target);
            target.Position = 0;
            return;
        }

        var encryptedHeader = new byte[128];
        var actualHeaderBytes = source.Read(encryptedHeader, 0, encryptedHeader.Length);
        if (actualHeaderBytes != encryptedHeader.Length)
        {
            throw new InvalidDataException("Encrypted bundle header is shorter than 128 bytes.");
        }

        for (var i = 0; i < encryptedHeader.Length; i += 8)
        {
            for (var j = 0; j < 5; j++)
            {
                encryptedHeader[i + j] = (byte)~encryptedHeader[i + j];
            }
        }

        target.Write(encryptedHeader, 0, encryptedHeader.Length);
        source.CopyTo(target);
        target.Position = 0;
    }

    private static string CreateTempSiblingPath(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath)
            ?? throw new InvalidOperationException($"Cannot determine bundle directory for {originalPath}");
        var fileName = Path.GetFileName(originalPath);
        var tempName = $".pjskbundle2parts.{Guid.NewGuid():N}.{fileName}";
        return Path.Combine(directory, tempName);
    }
}

public sealed class DecryptedBundleHandle : IDisposable
{
    public string Path { get; }

    private readonly bool deleteOnDispose;

    public DecryptedBundleHandle(string path, bool deleteOnDispose)
    {
        Path = path;
        this.deleteOnDispose = deleteOnDispose;
    }

    public void Dispose()
    {
        if (!deleteOnDispose)
        {
            return;
        }

        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch
        {
            // Keep best-effort cleanup silent for converter probing.
        }
    }
}
