using AssetStudio;

namespace PjskBundle2Parts.Services;

public sealed class UnityRuntimeTextureExporter
{
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
        ExportPartTextures(
            Path.Combine(outputDirectory, "textures", normalizedPartKind),
            imported.TextureList,
            normalizedPartKind,
            Path.Combine("textures", normalizedPartKind),
            result
        );
        ExportPartTextures(
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

        ExportPartTextures(
            Path.Combine(outputDirectory, "textures", "body"),
            bodyImported.TextureList,
            "body",
            Path.Combine("textures", "body"),
            result
        );
        ExportPartTextures(
            Path.Combine(outputDirectory, "textures", "body"),
            bodyOverrideTextures,
            "body",
            Path.Combine("textures", "body"),
            result
        );
        ExportPartTextures(
            Path.Combine(outputDirectory, "textures", "head"),
            headImported.TextureList,
            "head",
            Path.Combine("textures", "head"),
            result
        );
        ExportPartTextures(
            Path.Combine(outputDirectory, "textures", "head"),
            headOverrideTextures,
            "head",
            Path.Combine("textures", "head"),
            result
        );
        if (accessoryImported is not null)
        {
            ExportPartTextures(
                Path.Combine(outputDirectory, "textures", "accessory"),
                accessoryImported.TextureList,
                "accessory",
                Path.Combine("textures", "accessory"),
                result
            );
        }
        ExportPartTextures(
            Path.Combine(outputDirectory, "textures", "accessory"),
            accessoryOverrideTextures,
            "accessory",
            Path.Combine("textures", "accessory"),
            result
        );

        return result;
    }

    private static void ExportPartTextures(
        string textureDirectory,
        IReadOnlyList<ImportedTexture>? textures,
        string prefix,
        string relativeTextureDirectory,
        Dictionary<string, string> result
    )
    {
        if (textures is null || textures.Count == 0)
        {
            return;
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
