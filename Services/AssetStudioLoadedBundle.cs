using AssetStudio;
using PjskBundle2Parts.Models;
using Object = AssetStudio.Object;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioLoadedBundle : IDisposable
{
    private const string SekaiUnityVersion = "2022.3.21f1";

    private readonly DecryptedBundleWorkspace readableBundles;
    private readonly AssetsManager manager;

    public ResolvedBundleInput Input { get; }
    public BundleLoadDependencyMode DependencyMode { get; }
    public IReadOnlyList<Object> Objects { get; }
    public IReadOnlyList<Object> PrimaryObjects { get; }
    public int AssetsFileCount => manager.AssetsFileList.Count;

    private AssetStudioLoadedBundle(
        ResolvedBundleInput input,
        BundleLoadDependencyMode dependencyMode,
        DecryptedBundleWorkspace readableBundles,
        AssetsManager manager
    )
    {
        Input = input;
        DependencyMode = dependencyMode;
        this.readableBundles = readableBundles;
        this.manager = manager;
        Objects = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .ToList();
        PrimaryObjects = AssetStudioObjectFilter.SelectPrimaryObjects(Objects, readableBundles.PrimaryFileName);
    }

    public static AssetStudioLoadedBundle Load(
        ResolvedBundleInput input,
        BundleLoadDependencyMode dependencyMode = BundleLoadDependencyMode.Default
    )
    {
        var readableBundles = new SekaiBundleDecryptor().PrepareReadableWorkspace(
            input.ResolvedBundlePath,
            BundleDependencyResolver.ResolveLoadBundlePaths(input, dependencyMode)
        );
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
        manager.LoadFilesAndFolders(readableBundles.DirectoryPath);
        return new AssetStudioLoadedBundle(input, dependencyMode, readableBundles, manager);
    }

    public void Dispose()
    {
        readableBundles.Dispose();
    }
}
