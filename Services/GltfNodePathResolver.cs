using SharpGLTF.Schema2;

namespace PjskBundle2Parts.Services;

public sealed class GltfNodePathResolver
{
    private readonly Dictionary<string, ResolvedGltfNode> nodesByPath;
    private readonly Dictionary<string, IReadOnlyList<ResolvedGltfNode>> nodesByName;
    private readonly IReadOnlyList<ResolvedGltfNode> nodes;

    private GltfNodePathResolver(
        IReadOnlyList<ResolvedGltfNode> nodes
    )
    {
        this.nodes = nodes;
        nodesByPath = nodes
            .GroupBy(node => node.NodePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase
            );
        nodesByName = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeName))
            .GroupBy(node => node.NodeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ResolvedGltfNode>)group.ToList(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    public int NodeCount => nodes.Count;

    public static GltfNodePathResolver FromGlb(string glbPath)
    {
        return FromModel(ModelRoot.Load(glbPath));
    }

    public static GltfNodePathResolver FromModel(ModelRoot model)
    {
        var scene = model.DefaultScene ?? model.LogicalScenes.FirstOrDefault();
        if (scene is null)
        {
            return new GltfNodePathResolver(Array.Empty<ResolvedGltfNode>());
        }

        var nodes = new List<ResolvedGltfNode>();
        foreach (var rootNode in scene.VisualChildren)
        {
            Collect(rootNode, null, nodes);
        }
        return new GltfNodePathResolver(nodes);
    }

    public bool TryResolvePath(
        string sourcePath,
        out ResolvedGltfNode resolved,
        out string matchMode
    )
    {
        foreach (var candidate in EnumeratePathCandidates(sourcePath))
        {
            if (TryResolvePathCore(candidate, out resolved, out matchMode))
            {
                if (!string.Equals(candidate, sourcePath, StringComparison.Ordinal))
                {
                    matchMode = $"alias:{matchMode}";
                }
                return true;
            }
        }

        resolved = default;
        matchMode = "not_found";
        return false;
    }

    private bool TryResolvePathCore(
        string sourcePath,
        out ResolvedGltfNode resolved,
        out string matchMode
    )
    {
        if (nodesByPath.TryGetValue(sourcePath, out resolved))
        {
            matchMode = "exact";
            return true;
        }

        var suffix = "/" + sourcePath;
        var matches = nodes
            .Where(node => node.NodePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
        {
            resolved = matches[0];
            matchMode = "suffix";
            return true;
        }

        resolved = default;
        matchMode = matches.Count > 1 ? "ambiguous_suffix" : "not_found";
        return false;
    }

    private static IEnumerable<string> EnumeratePathCandidates(string sourcePath)
    {
        yield return sourcePath;

        if (sourcePath.StartsWith("sit_body/", StringComparison.OrdinalIgnoreCase))
        {
            yield return "body/" + sourcePath["sit_body/".Length..];
        }

        const string faceHumanoidPrefix = "face/Position/Hip/Waist/Spine/Chest/Neck/Head";
        const string bodyHumanoidPrefix = "body/Position/PositionOffset/Hip/Waist/Spine/Chest/Neck/Head";
        if (sourcePath.StartsWith(faceHumanoidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return bodyHumanoidPrefix + sourcePath[faceHumanoidPrefix.Length..];
        }

        const string facePositionPrefix = "face/Position/Hip";
        const string bodyPositionPrefix = "body/Position/PositionOffset/Hip";
        if (sourcePath.StartsWith(facePositionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return bodyPositionPrefix + sourcePath[facePositionPrefix.Length..];
        }
    }

    public bool TryResolveName(
        string nodeName,
        out ResolvedGltfNode resolved,
        out string matchMode
    )
    {
        if (nodesByName.TryGetValue(nodeName, out var matches))
        {
            if (matches.Count == 1)
            {
                resolved = matches[0];
                matchMode = "unique_name";
                return true;
            }

            var preferredBodyMatches = matches
                .Where(node => IsPreferredBodyHumanoidPath(node.NodePath))
                .ToList();
            if (preferredBodyMatches.Count == 1)
            {
                resolved = preferredBodyMatches[0];
                matchMode = "preferred_body_humanoid_path";
                return true;
            }

            resolved = default;
            matchMode = "ambiguous_name";
            return false;
        }

        resolved = default;
        matchMode = "not_found";
        return false;
    }

    private static bool IsPreferredBodyHumanoidPath(string path)
    {
        return path.StartsWith("body/Position/PositionOffset/Hip", StringComparison.OrdinalIgnoreCase) &&
            path.IndexOf("/face/", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static void Collect(
        Node node,
        string? parentPath,
        List<ResolvedGltfNode> nodes
    )
    {
        var currentPath = string.IsNullOrWhiteSpace(parentPath)
            ? node.Name ?? string.Empty
            : $"{parentPath}/{node.Name}";
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            nodes.Add(new ResolvedGltfNode(
                NodeIndex: node.LogicalIndex,
                NodeName: node.Name ?? string.Empty,
                NodePath: currentPath
            ));
        }

        foreach (var child in node.VisualChildren)
        {
            Collect(child, currentPath, nodes);
        }
    }
}

public readonly record struct ResolvedGltfNode(
    int NodeIndex,
    string NodeName,
    string NodePath
);
