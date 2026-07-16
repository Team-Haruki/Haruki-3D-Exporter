using System.Security.Cryptography;
using System.Text;

namespace PjskBundle2Parts.Services;

public sealed record RuntimeMaterialIdentity(
    string MaterialKey,
    long MaterialFileId,
    long MaterialPathId
);

public static class RuntimeMaterialIdentityResolver
{
    public static RuntimeMaterialIdentity Resolve(
        string partKind,
        int slotIndex,
        long materialFileId,
        long materialPathId,
        string? materialName
    )
    {
        if (materialPathId != 0)
        {
            return new RuntimeMaterialIdentity(
                MaterialIdentityLookup.BuildMaterialKey(materialFileId, materialPathId),
                materialFileId,
                materialPathId
            );
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            throw new InvalidOperationException(
                $"Renderer material slot {slotIndex} has an empty material reference and no imported material name."
            );
        }

        var key = BuildSyntheticMaterialKey(partKind, materialName);
        return new RuntimeMaterialIdentity(
            key,
            StableNegativeId($"{key}:file"),
            StableNegativeId($"{key}:path")
        );
    }

    public static string BuildSyntheticMaterialKey(string partKind, string materialName)
    {
        var normalizedPartKind = Normalize(partKind);
        if (normalizedPartKind == "accessory")
        {
            normalizedPartKind = "head_optional";
        }
        return $"synthetic:{normalizedPartKind}:{Normalize(materialName)}";
    }

    private static string Normalize(string value)
    {
        return value.Trim().Replace('\\', '/').ToLowerInvariant();
    }

    private static long StableNegativeId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var raw = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return raw == 0 ? -1 : -raw;
    }
}
