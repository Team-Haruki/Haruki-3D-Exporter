using System.Security.Cryptography;
using System.Text;

namespace PjskBundle2Parts.Services;

public sealed class PartPackageWorkClaims
{
    private readonly string directory;

    public PartPackageWorkClaims(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }

    public bool TryClaim(string packagePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packagePath))).ToLowerInvariant();
        var claimPath = Path.Combine(directory, hash + ".claim");
        try
        {
            using var claim = new FileStream(
                claimPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read
            );
            using var writer = new StreamWriter(claim, leaveOpen: false);
            writer.Write(packagePath);
            return true;
        }
        catch (IOException) when (File.Exists(claimPath))
        {
            return false;
        }
    }
}
