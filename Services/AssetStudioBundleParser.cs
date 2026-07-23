using AssetStudio;
using PjskBundle2Parts.Models;
using Object = AssetStudio.Object;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioBundleParser
{
    private const string SekaiUnityVersion = "2022.3.21f1";
    private readonly SekaiBundleDecryptor decryptor = new();

    public BundleInventory Parse(ResolvedBundleInput input)
    {
        using var readableBundle = decryptor.PrepareReadableWorkspace(
            input.ResolvedBundlePath,
            BundleDependencyResolver.ResolveLoadBundlePaths(input)
        );
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
        manager.LoadFilesAndFolders(readableBundle.DirectoryPath);

        var objects = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .ToList();
        var primaryObjects = AssetStudioObjectFilter.SelectPrimaryObjects(objects, readableBundle.PrimaryFileName);
        return Parse(input, primaryObjects, objects, manager.AssetsFileList.Count);
    }

    public BundleInventory Parse(ResolvedBundleInput input, IReadOnlyList<Object> objects, int assetsFileCount)
    {
        return Parse(input, objects, objects, assetsFileCount);
    }

    public BundleInventory Parse(
        ResolvedBundleInput input,
        IReadOnlyList<Object> primaryObjects,
        IReadOnlyList<Object> allObjects,
        int assetsFileCount
    )
    {
        var objectTypeCounts = allObjects
            .GroupBy(obj => obj.type.ToString())
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var gameObjects = primaryObjects
            .OfType<GameObject>()
            .Where(gameObject => gameObject.m_Transform != null)
            .ToList();
        var roots = gameObjects
            .Where(gameObject => gameObject.m_Transform.m_Father.IsNull)
            .Select(gameObject => new RootNodeInventory(
                gameObject.m_Name,
                BuildTransformPath(gameObject.m_Transform)
            ))
            .OrderBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var boneNames = gameObjects
            .Select(gameObject => gameObject.m_Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skinnedMeshes = gameObjects
            .Where(gameObject => gameObject.m_SkinnedMeshRenderer != null)
            .Select(BuildSkinnedMeshInventory)
            .OrderBy(mesh => mesh.NodePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var staticMeshes = gameObjects
            .Where(gameObject => gameObject.m_MeshRenderer != null && gameObject.m_MeshFilter != null)
            .Select(BuildStaticMeshInventory)
            .Where(mesh => mesh is not null)
            .Cast<RenderMeshInventory>()
            .OrderBy(mesh => mesh.NodePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var materialInventory = skinnedMeshes
            .Concat(staticMeshes)
            .SelectMany(mesh => mesh.MaterialSlots)
            .Select(slot => slot.ResolvedMaterial)
            .Where(material => material is not null)
            .Select(material => material!)
            .DistinctBy(material => material.MaterialKey, StringComparer.Ordinal)
            .OrderBy(material => material.MaterialFileId)
            .ThenBy(material => material.MaterialPathId)
            .ToList();

        return new BundleInventory(
            BundlePath: input.ResolvedBundlePath,
            PartKind: input.PartKind.ToString(),
            AssetsFileCount: assetsFileCount,
            ObjectCount: allObjects.Count,
            ObjectTypeCounts: objectTypeCounts,
            Roots: roots,
            BoneNames: boneNames,
            AttachNodeCandidates: InferAttachCandidates(boneNames),
            OriginNodeCandidates: InferOriginCandidates(boneNames),
            SkinnedMeshes: skinnedMeshes,
            StaticMeshes: staticMeshes,
            Materials: materialInventory
        );
    }

    private static MaterialInventory BuildMaterialInventory(
        Material material,
        long materialFileId,
        string materialKey
    )
    {
        var shaderName = material.m_Shader.TryGet(out Shader shader) ? shader.m_Name : null;
        var slots = material.m_SavedProperties?.m_TexEnvs?
            .Select(entry =>
            {
                var texturePointer = entry.Value.m_Texture;
                var textureName = texturePointer.TryGet<Texture>(out var texture)
                    ? texture.m_Name
                    : null;
                var texturePathId = texturePointer.m_PathID;
                var textureFileId = texturePointer.m_FileID;
                return new TextureSlotInventory(
                    SlotName: entry.Key,
                    TextureName: textureName,
                    TextureFileId: textureFileId,
                    TexturePathId: texturePathId,
                    TextureKey: texturePathId == 0 ? null : BuildTextureKey(textureFileId, texturePathId),
                    TextureData: texture is Texture2D texture2D ? ConvertTextureToPng(texture2D) : null,
                    ScaleX: entry.Value.m_Scale.X,
                    ScaleY: entry.Value.m_Scale.Y,
                    OffsetX: entry.Value.m_Offset.X,
                    OffsetY: entry.Value.m_Offset.Y,
                    ColorSpace: texture is Texture2D sourceTexture ? sourceTexture.m_ColorSpace : 0,
                    SourceWidth: texture is Texture2D sourceWidth ? sourceWidth.m_Width : 0,
                    SourceHeight: texture is Texture2D sourceHeight ? sourceHeight.m_Height : 0,
                    SourceMipCount: texture is Texture2D sourceMipCount ? sourceMipCount.m_MipCount : 0,
                    SourceFormat: texture is Texture2D sourceFormat ? (int)sourceFormat.m_TextureFormat : 0,
                    FilterMode: texture is Texture2D sourceFilter ? sourceFilter.m_TextureSettings?.m_FilterMode ?? 0 : 0,
                    AnisoLevel: texture is Texture2D sourceAniso ? sourceAniso.m_TextureSettings?.m_Aniso ?? 0 : 0,
                    MipBias: texture is Texture2D sourceMipBias ? sourceMipBias.m_TextureSettings?.m_MipBias ?? 0f : 0f,
                    WrapU: texture is Texture2D sourceWrapU ? sourceWrapU.m_TextureSettings?.m_WrapU ?? 0 : 0,
                    WrapV: texture is Texture2D sourceWrapV ? sourceWrapV.m_TextureSettings?.m_WrapV ?? 0 : 0,
                    WrapW: texture is Texture2D sourceWrapW ? sourceWrapW.m_TextureSettings?.m_WrapW ?? 0 : 0
                );
            })
            .OrderBy(slot => slot.SlotName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<TextureSlotInventory>();
        var colorProperties = material.m_SavedProperties?.m_Colors?
            .Select(entry => new ColorPropertyInventory(
                entry.Key,
                entry.Value.R,
                entry.Value.G,
                entry.Value.B,
                entry.Value.A
            ))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<ColorPropertyInventory>();
        var floatProperties = material.m_SavedProperties?.m_Floats?
            .Select(entry => new FloatPropertyInventory(entry.Key, entry.Value))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<FloatPropertyInventory>();
        var intProperties = material.m_SavedProperties?.m_Ints?
            .Select(entry => new IntPropertyInventory(entry.Key, entry.Value))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<IntPropertyInventory>();

        return new MaterialInventory(
            MaterialFileId: materialFileId,
            MaterialPathId: material.m_PathID,
            MaterialKey: materialKey,
            Name: material.m_Name,
            ShaderName: shaderName,
            TextureSlots: slots,
            ColorProperties: colorProperties,
            FloatProperties: floatProperties,
            ValidKeywords: material.m_ValidKeywords,
            InvalidKeywords: material.m_InvalidKeywords,
            IntProperties: intProperties,
            LightmapFlags: material.m_LightmapFlags,
            EnableInstancingVariants: material.m_EnableInstancingVariants,
            DoubleSidedGi: material.m_DoubleSidedGI,
            CustomRenderQueue: material.m_CustomRenderQueue,
            StringTags: material.m_StringTagMap,
            DisabledShaderPasses: material.m_DisabledShaderPasses,
            ShaderFileId: material.m_Shader.m_FileID,
            ShaderPathId: material.m_Shader.m_PathID,
            ShaderKey: material.m_Shader.m_PathID == 0
                ? null
                : $"ref:{material.m_Shader.m_FileID}:{material.m_Shader.m_PathID}"
        );
    }

    private static string BuildTextureKey(long fileId, long pathId)
    {
        return $"ref:{fileId}:{pathId}";
    }

    private static byte[]? ConvertTextureToPng(Texture2D texture)
    {
        using var stream = texture.ConvertToStream(ImageFormat.Png, false);
        if (stream is null)
        {
            return null;
        }

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static RenderMeshInventory BuildSkinnedMeshInventory(GameObject gameObject)
    {
        var renderer = gameObject.m_SkinnedMeshRenderer!;
        Mesh? mesh = null;
        if (renderer.m_Mesh.TryGet(out Mesh resolvedMesh))
        {
            resolvedMesh.ProcessData();
            mesh = resolvedMesh;
        }

        var materialSlots = BuildMaterialSlots(renderer.m_Materials);
        var boneNames = renderer.m_Bones
            .Select(ptr =>
            {
                if (!ptr.TryGet(out Transform boneTransform))
                {
                    return $"missing:{ptr.m_PathID}";
                }
                return boneTransform.m_GameObject.TryGet(out GameObject boneGameObject)
                    ? boneGameObject.m_Name
                    : $"bone:{ptr.m_PathID}";
            })
            .ToList();

        return new RenderMeshInventory(
            NodeName: gameObject.m_Name,
            NodePath: BuildTransformPath(gameObject.m_Transform),
            MeshName: mesh?.m_Name ?? "<missing-mesh>",
            VertexCount: mesh?.m_VertexCount ?? 0,
            SubMeshCount: mesh?.m_SubMeshes?.Count ?? 0,
            MaterialSlots: materialSlots,
            BoneNames: boneNames
        );
    }

    private static RenderMeshInventory? BuildStaticMeshInventory(GameObject gameObject)
    {
        var renderer = gameObject.m_MeshRenderer!;
        if (!gameObject.m_MeshFilter!.m_Mesh.TryGet(out Mesh mesh))
        {
            return null;
        }

        mesh.ProcessData();
        var materialSlots = BuildMaterialSlots(renderer.m_Materials);

        return new RenderMeshInventory(
            NodeName: gameObject.m_Name,
            NodePath: BuildTransformPath(gameObject.m_Transform),
            MeshName: mesh.m_Name,
            VertexCount: mesh.m_VertexCount,
            SubMeshCount: mesh.m_SubMeshes?.Count ?? 0,
            MaterialSlots: materialSlots,
            BoneNames: Array.Empty<string>()
        );
    }

    private static IReadOnlyList<RenderMaterialSlotInventory> BuildMaterialSlots(IReadOnlyList<PPtr<Material>> materials)
    {
        return materials
            .Select((ptr, index) =>
            {
                var hasMaterial = ptr.TryGet(out Material material);
                var name = hasMaterial ? material.m_Name : null;
                var materialKey = MaterialIdentityLookup.BuildMaterialKey(ptr.m_FileID, ptr.m_PathID);
                return new RenderMaterialSlotInventory(
                    SlotIndex: index,
                    MaterialFileId: ptr.m_FileID,
                    MaterialPathId: ptr.m_PathID,
                    MaterialKey: materialKey,
                    MaterialName: name,
                    ResolvedMaterial: hasMaterial && ptr.m_PathID != 0
                        ? BuildMaterialInventory(material, ptr.m_FileID, materialKey)
                        : null
                );
            })
            .ToList();
    }

    private static string BuildTransformPath(Transform transform)
    {
        if (!transform.m_GameObject.TryGet(out GameObject gameObject))
        {
            return $"transform:{transform.m_PathID}";
        }

        if (!transform.m_Father.TryGet(out Transform father))
        {
            return gameObject.m_Name;
        }

        return $"{BuildTransformPath(father)}/{gameObject.m_Name}";
    }

    private static IReadOnlyList<string> InferAttachCandidates(IReadOnlyList<string> boneNames)
    {
        return boneNames
            .Where(name =>
                name.Contains("head", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("neck", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => ScoreAttachCandidate(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> InferOriginCandidates(IReadOnlyList<string> boneNames)
    {
        return boneNames
            .Where(name =>
                name.Contains("socket", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("neck", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("head", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => ScoreOriginCandidate(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreAttachCandidate(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "head" => 0,
            "j_head" => 1,
            "neck" => 2,
            _ when name.Contains("head", StringComparison.OrdinalIgnoreCase) => 10,
            _ when name.Contains("neck", StringComparison.OrdinalIgnoreCase) => 20,
            _ => 100,
        };
    }

    private static int ScoreOriginCandidate(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "necksocket" => 0,
            "head" => 1,
            "neck" => 2,
            _ when name.Contains("socket", StringComparison.OrdinalIgnoreCase) => 10,
            _ when name.Contains("neck", StringComparison.OrdinalIgnoreCase) => 20,
            _ when name.Contains("head", StringComparison.OrdinalIgnoreCase) => 30,
            _ => 100,
        };
    }
}
