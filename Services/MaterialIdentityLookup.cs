using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class MaterialIdentityLookup
{
    private readonly IReadOnlyDictionary<string, MaterialInventory> materialByKey;

    private MaterialIdentityLookup(IReadOnlyDictionary<string, MaterialInventory> materialByKey)
    {
        this.materialByKey = materialByKey;
    }

    public static string BuildMaterialKey(long fileId, long pathId)
    {
        return $"{fileId}:{pathId}";
    }

    public static MaterialIdentityLookup FromInventory(IReadOnlyList<MaterialInventory> materials)
    {
        var result = new Dictionary<string, MaterialInventory>(StringComparer.Ordinal);
        foreach (var material in materials)
        {
            if (string.IsNullOrWhiteSpace(material.MaterialKey))
            {
                throw new InvalidOperationException($"Material '{material.Name}' is missing materialKey.");
            }

            if (!result.TryAdd(material.MaterialKey, material))
            {
                throw new InvalidOperationException($"Duplicate material identity '{material.MaterialKey}'.");
            }
        }

        return new MaterialIdentityLookup(result);
    }

    public MaterialInventory Require(RenderMaterialSlotInventory slot)
    {
        if (string.IsNullOrWhiteSpace(slot.MaterialKey))
        {
            throw new InvalidOperationException(
                $"Renderer material slot {slot.SlotIndex} for material '{slot.MaterialName ?? "<unnamed>"}' is missing materialKey."
            );
        }

        if (materialByKey.TryGetValue(slot.MaterialKey, out var material))
        {
            return material;
        }

        throw new MissingMaterialReferenceException(slot);
    }
}

public sealed class MissingMaterialReferenceException : InvalidOperationException
{
    public MissingMaterialReferenceException(RenderMaterialSlotInventory slot)
        : base($"Renderer material slot {slot.SlotIndex} references missing material {slot.MaterialKey} ({slot.MaterialName ?? "<unnamed>"}).")
    {
        SlotIndex = slot.SlotIndex;
        MaterialFileId = slot.MaterialFileId;
        MaterialPathId = slot.MaterialPathId;
        MaterialKey = slot.MaterialKey;
        MaterialName = slot.MaterialName;
    }

    public int SlotIndex { get; }
    public long MaterialFileId { get; }
    public long MaterialPathId { get; }
    public string MaterialKey { get; }
    public string? MaterialName { get; }
}
