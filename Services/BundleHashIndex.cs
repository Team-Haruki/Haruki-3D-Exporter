using System.Text.Json;

namespace PjskBundle2Parts.Services;

public sealed class BundleHashIndex
{
    private readonly IReadOnlyDictionary<string, byte[]> hashes;

    public BundleHashIndex(string? path)
    {
        hashes = Load(path);
    }

    public bool TryGet(string assetRoot, string bundlePath, out byte[] hash)
    {
        var key = Path.GetRelativePath(assetRoot, bundlePath).Replace('\\', '/');
        return hashes.TryGetValue(key, out hash!);
    }

    private static IReadOnlyDictionary<string, byte[]> Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? new Dictionary<string, string>();
            return values
                .Where(pair => pair.Value.Length == 64)
                .Select(pair => (pair.Key.Replace('\\', '/'), Hash: TryDecode(pair.Value)))
                .Where(pair => pair.Hash is not null)
                .ToDictionary(pair => pair.Item1, pair => pair.Hash!, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"Bundle hash index ignored ({path}): {ex.Message}");
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);
        }
    }

    private static byte[]? TryDecode(string value)
    {
        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
