using AssetStudio;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class AssetStudioBundleParser
{
    private const string SekaiUnityVersion = "2022.3.21f1";
    private readonly SekaiBundleDecryptor decryptor = new();

    public BundleInventory Parse(ResolvedBundleInput input)
    {
        using var readableBundle = decryptor.PrepareReadableBundle(input.ResolvedBundlePath);
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

        var objects = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .ToList();
        var objectTypeCounts = objects
            .GroupBy(obj => obj.type.ToString())
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var gameObjects = objects
            .OfType<GameObject>()
            .Where(gameObject => gameObject.m_Transform != null)
            .ToList();
        var materialInventory = objects
            .OfType<Material>()
            .Select(BuildMaterialInventory)
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
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

        return new BundleInventory(
            BundlePath: input.ResolvedBundlePath,
            PartKind: input.PartKind.ToString(),
            AssetsFileCount: manager.AssetsFileList.Count,
            ObjectCount: objects.Count,
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

    private static MaterialInventory BuildMaterialInventory(Material material)
    {
        var shaderName = material.m_Shader.TryGet(out Shader shader) ? shader.m_Name : null;
        var slots = material.m_SavedProperties?.m_TexEnvs?
            .Select(entry =>
            {
                var textureName = entry.Value.m_Texture.TryGet<Texture>(out var texture)
                    ? texture.m_Name
                    : null;
                return new TextureSlotInventory(entry.Key, textureName);
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

        return new MaterialInventory(material.m_Name, shaderName, slots, colorProperties, floatProperties);
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

        var materialNames = renderer.m_Materials
            .Select(ptr => ptr.TryGet(out Material material) ? material.m_Name : $"missing:{ptr.m_PathID}")
            .ToList();
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
            MaterialNames: materialNames,
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
        var materialNames = renderer.m_Materials
            .Select(ptr => ptr.TryGet(out Material material) ? material.m_Name : $"missing:{ptr.m_PathID}")
            .ToList();

        return new RenderMeshInventory(
            NodeName: gameObject.m_Name,
            NodePath: BuildTransformPath(gameObject.m_Transform),
            MeshName: mesh.m_Name,
            VertexCount: mesh.m_VertexCount,
            SubMeshCount: mesh.m_SubMeshes?.Count ?? 0,
            MaterialNames: materialNames,
            BoneNames: Array.Empty<string>()
        );
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
