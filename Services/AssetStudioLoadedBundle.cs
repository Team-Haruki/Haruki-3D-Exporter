using AssetStudio;
using PjskBundle2Parts.Models;
using Object = AssetStudio.Object;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioLoadedBundle : IDisposable
{
    private const string SekaiUnityVersion = "2022.3.21f1";

    private readonly DecryptedBundleHandle readableBundle;
    private readonly AssetsManager manager;

    public ResolvedBundleInput Input { get; }
    public IReadOnlyList<Object> Objects { get; }
    public int AssetsFileCount => manager.AssetsFileList.Count;

    private AssetStudioLoadedBundle(
        ResolvedBundleInput input,
        DecryptedBundleHandle readableBundle,
        AssetsManager manager
    )
    {
        Input = input;
        this.readableBundle = readableBundle;
        this.manager = manager;
        Objects = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .ToList();
    }

    public static AssetStudioLoadedBundle Load(ResolvedBundleInput input)
    {
        var readableBundle = new SekaiBundleDecryptor().PrepareReadableBundle(input.ResolvedBundlePath);
        var manager = new AssetsManager
        {
            MeshLazyLoad = false,
        };
        manager.Options.CustomUnityVersion = new UnityVersion(SekaiUnityVersion);
        manager.SetAssetFilter(
            ClassIDType.GameObject,
            ClassIDType.Transform,
            ClassIDType.Animator,
            ClassIDType.Material,
            ClassIDType.Mesh,
            ClassIDType.Texture2D,
            ClassIDType.MonoBehaviour,
            ClassIDType.MeshRenderer,
            ClassIDType.SkinnedMeshRenderer
        );
        manager.LoadFilesAndFolders(readableBundle.Path);
        return new AssetStudioLoadedBundle(input, readableBundle, manager);
    }

    public void Dispose()
    {
        readableBundle.Dispose();
    }
}
