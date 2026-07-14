using AssetStudio;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class UnityRuntimeTextureExporter
{
    public IReadOnlyDictionary<string, string> ExportPartTextures(
        string packageDirectory,
        string runtimeOutputDirectory,
        string partKind,
        BundleInventory inventory,
        IReadOnlyList<ImportedTexture>? overrideTextures = null,
        IReadOnlyDictionary<string, string>? baseTextures = null
    )
    {
        Directory.CreateDirectory(packageDirectory);
        var result = baseTextures is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(baseTextures, StringComparer.OrdinalIgnoreCase);
        var normalizedPartKind = partKind.Equals("head_optional", StringComparison.OrdinalIgnoreCase)
            ? "accessory"
            : partKind;
        var store = new RuntimeTextureStore(runtimeOutputDirectory);
        if (baseTextures is null)
        {
            ExportReferencedTextures(
                store,
                inventory.Materials.SelectMany(material => material.TextureSlots),
                normalizedPartKind,
                result
            );
        }
        var overrideAliases = StoreImportedTextures(
            store,
            overrideTextures,
            normalizedPartKind,
            result
        );
        ApplyOverrideAliases(inventory, overrideAliases, result);
        return result;
    }

    private static Dictionary<string, string> StoreImportedTextures(
        RuntimeTextureStore store,
        IReadOnlyList<ImportedTexture>? textures,
        string prefix,
        Dictionary<string, string> result
    )
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (textures is null)
        {
            return aliases;
        }
        foreach (var texture in textures)
        {
            var safeName = $"{prefix}_{texture.Name}";
            var runtimePath = store.StorePng(texture.Data);
            AddTextureKey(result, safeName, runtimePath);
            AddTextureKey(result, texture.Name, runtimePath);
            AddTextureKey(aliases, texture.Name, runtimePath);
        }
        return aliases;
    }

    public IReadOnlyDictionary<string, string> ExportPartTextures(
        string outputDirectory,
        string partKind,
        IImported imported,
        IReadOnlyList<ImportedTexture>? overrideTextures = null
    )
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPartKind = partKind.Equals("head_optional", StringComparison.OrdinalIgnoreCase)
            ? "accessory"
            : partKind;
        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", normalizedPartKind),
            imported.TextureList,
            normalizedPartKind,
            Path.Combine("textures", normalizedPartKind),
            result
        );
        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", normalizedPartKind),
            overrideTextures,
            normalizedPartKind,
            Path.Combine("textures", normalizedPartKind),
            result
        );
        return result;
    }

    public IReadOnlyDictionary<string, string> ExportCharacterTextures(
        string outputDirectory,
        IImported bodyImported,
        IImported headImported,
        IImported? accessoryImported = null,
        IReadOnlyList<ImportedTexture>? bodyOverrideTextures = null,
        IReadOnlyList<ImportedTexture>? headOverrideTextures = null,
        IReadOnlyList<ImportedTexture>? accessoryOverrideTextures = null
    )
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", "body"),
            bodyImported.TextureList,
            "body",
            Path.Combine("textures", "body"),
            result
        );
        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", "body"),
            bodyOverrideTextures,
            "body",
            Path.Combine("textures", "body"),
            result
        );
        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", "head"),
            headImported.TextureList,
            "head",
            Path.Combine("textures", "head"),
            result
        );
        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", "head"),
            headOverrideTextures,
            "head",
            Path.Combine("textures", "head"),
            result
        );
        if (accessoryImported is not null)
        {
            ExportImportedTextures(
                Path.Combine(outputDirectory, "textures", "accessory"),
                accessoryImported.TextureList,
                "accessory",
                Path.Combine("textures", "accessory"),
                result
            );
        }
        ExportImportedTextures(
            Path.Combine(outputDirectory, "textures", "accessory"),
            accessoryOverrideTextures,
            "accessory",
            Path.Combine("textures", "accessory"),
            result
        );

        return result;
    }

    private static Dictionary<string, string> ExportImportedTextures(
        string textureDirectory,
        IReadOnlyList<ImportedTexture>? textures,
        string prefix,
        string relativeTextureDirectory,
        Dictionary<string, string> result
    )
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (textures is null || textures.Count == 0)
        {
            return aliases;
        }

        Directory.CreateDirectory(textureDirectory);
        foreach (var texture in textures)
        {
            var safeName = $"{prefix}_{texture.Name}";
            var filePath = Path.Combine(textureDirectory, safeName);
            File.WriteAllBytes(filePath, texture.Data);
            var relativePath = Path.Combine(relativeTextureDirectory, safeName).Replace('\\', '/');
            AddTextureKey(result, safeName, relativePath);
            AddTextureKey(result, texture.Name, relativePath);
            AddTextureKey(aliases, texture.Name, relativePath);
        }
        return aliases;
    }

    private static void ExportReferencedTextures(
        RuntimeTextureStore store,
        IEnumerable<TextureSlotInventory> textureSlots,
        string prefix,
        Dictionary<string, string> result
    )
    {
        var exported = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in textureSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.TextureKey) ||
                slot.TexturePathId == 0 ||
                slot.TextureData is null ||
                !exported.Add(slot.TextureKey))
            {
                continue;
            }

            var safeName = UniqueTextureFileName(prefix, slot, usedNames);
            var runtimePath = store.StorePng(slot.TextureData);
            AddTextureKey(result, slot.TextureKey, runtimePath);
            AddTextureKey(result, safeName, runtimePath);
            if (!string.IsNullOrWhiteSpace(slot.TextureName))
            {
                AddTextureKey(result, slot.TextureName, runtimePath);
            }
        }
    }

    private static string UniqueTextureFileName(
        string prefix,
        TextureSlotInventory slot,
        HashSet<string> usedNames
    )
    {
        var sourceName = string.IsNullOrWhiteSpace(slot.TextureName)
            ? $"texture_{slot.TextureFileId}_{slot.TexturePathId}.png"
            : slot.TextureName;
        var nameWithExtension = sourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? sourceName
            : $"{sourceName}.png";
        var prefixedName = $"{prefix}_{nameWithExtension}";
        if (usedNames.Add(prefixedName))
        {
            return prefixedName;
        }

        var stem = Path.GetFileNameWithoutExtension(prefixedName);
        var extension = Path.GetExtension(prefixedName);
        var disambiguated = $"{stem}_{slot.TextureFileId}_{slot.TexturePathId}{extension}";
        usedNames.Add(disambiguated);
        return disambiguated;
    }

    private static void ApplyOverrideAliases(
        BundleInventory inventory,
        IReadOnlyDictionary<string, string> overrideAliases,
        Dictionary<string, string> result
    )
    {
        if (overrideAliases.Count == 0)
        {
            return;
        }

        foreach (var slot in inventory.Materials.SelectMany(material => material.TextureSlots))
        {
            if (string.IsNullOrWhiteSpace(slot.TextureKey) ||
                string.IsNullOrWhiteSpace(slot.TextureName))
            {
                continue;
            }

            if (overrideAliases.TryGetValue(slot.TextureName, out var overridePath) ||
                overrideAliases.TryGetValue(Path.GetFileNameWithoutExtension(slot.TextureName), out overridePath))
            {
                result[slot.TextureKey] = overridePath;
            }
        }
    }

    private static void AddTextureKey(
        Dictionary<string, string> result,
        string textureName,
        string relativePath
    )
    {
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return;
        }

        result[textureName] = relativePath;
        var stem = Path.GetFileNameWithoutExtension(textureName);
        if (!string.IsNullOrWhiteSpace(stem))
        {
            result[stem] = relativePath;
        }
    }
}
