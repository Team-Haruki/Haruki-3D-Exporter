using AssetStudio;
using PjskBundle2Parts.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioImportedModelFactory
{
    private const string SekaiUnityVersion = "2022.3.21f1";

    public IImported CreateImportedModel(ResolvedBundleInput input, string? preferredRootOverride = null)
    {
        using var readableBundle = new SekaiBundleDecryptor().PrepareReadableBundle(input.ResolvedBundlePath);
        var manager = new AssetsManager
        {
            MeshLazyLoad = false,
        };
        manager.Options.CustomUnityVersion = new UnityVersion(SekaiUnityVersion);
        manager.SetAssetFilter(
            ClassIDType.Animator,
            ClassIDType.Material,
            ClassIDType.Mesh,
            ClassIDType.Texture2D
        );
        manager.LoadFilesAndFolders(readableBundle.Path);

        var rootGameObjects = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .OfType<GameObject>()
            .Where(gameObject => gameObject.m_Transform != null && gameObject.m_Transform.m_Father.IsNull)
            .ToList();

        if (rootGameObjects.Count == 0)
        {
            throw new InvalidOperationException($"No root GameObjects found in {input.ResolvedBundlePath}");
        }

        var preferredRootName = !string.IsNullOrWhiteSpace(preferredRootOverride)
            ? preferredRootOverride
            : input.PartKind switch
            {
                BundlePartKind.Body => "body",
                BundlePartKind.Head => "face",
                _ => rootGameObjects[0].m_Name,
            };

        var preferredRoot = rootGameObjects
            .FirstOrDefault(gameObject =>
                string.Equals(gameObject.m_Name, preferredRootName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Root GameObject '{preferredRootName}' was not found in {input.ResolvedBundlePath}. Available roots: {string.Join(", ", rootGameObjects.Select(gameObject => gameObject.m_Name))}"
            );

        var imported = new ModelConverter(preferredRoot, ImageFormat.Png);
        // AssetStudio's ModelConverter flips exported texture bytes vertically.
        NormalizeTextureOrientation(imported.TextureList);
        return imported;
    }

    public IReadOnlyList<ImportedTexture> CreateImportedTextures(string bundlePath)
    {
        using var readableBundle = new SekaiBundleDecryptor().PrepareReadableBundle(bundlePath);
        var manager = new AssetsManager();
        manager.Options.CustomUnityVersion = new UnityVersion(SekaiUnityVersion);
        manager.SetAssetFilter(ClassIDType.Texture2D);
        manager.LoadFilesAndFolders(readableBundle.Path);

        var textures = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .OfType<Texture2D>()
            .Select(texture =>
            {
                using var stream = texture.ConvertToStream(ImageFormat.Png, true);
                return stream is null
                    ? null
                    : new ImportedTexture(stream, $"{texture.m_Name}.png");
            })
            .Where(texture => texture is not null)
            .Select(texture => texture!)
            .DistinctBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        NormalizeTextureOrientation(textures);
        return textures;
    }

    private static void NormalizeTextureOrientation(IReadOnlyList<ImportedTexture> textures)
    {
        foreach (var texture in textures)
        {
            using var image = Image.Load(texture.Data);
            image.Mutate(x => x.Flip(FlipMode.Vertical));
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            texture.Data = stream.ToArray();
        }
    }
}
