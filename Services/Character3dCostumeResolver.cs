using System.Text.Json;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class Character3dCostumeResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ResolvedCharacter3dCostume Resolve(
        int character3dId,
        string masterDirectory,
        string assetRoot
    )
    {
        var normalizedMasterDirectory = Path.GetFullPath(masterDirectory);
        var normalizedAssetRoot = Path.GetFullPath(assetRoot);
        var character3ds = ReadJson<IReadOnlyList<Character3dMaster>>(
            Path.Combine(normalizedMasterDirectory, "character3ds.json")
        );
        var costumeModels = ReadJson<IReadOnlyList<Costume3dModelMaster>>(
            Path.Combine(normalizedMasterDirectory, "costume3dModels.json")
        );
        var gameCharacters = ReadJson<IReadOnlyList<GameCharacterMaster>>(
            Path.Combine(normalizedMasterDirectory, "gameCharacters.json")
        );

        var character3d = character3ds.FirstOrDefault(entry => entry.Id == character3dId)
            ?? throw new InvalidOperationException($"character3d id {character3dId} was not found.");
        var character = gameCharacters.FirstOrDefault(entry => entry.Id == character3d.CharacterId)
            ?? throw new InvalidOperationException($"game character id {character3d.CharacterId} was not found.");
        var costumesById = costumeModels
            .GroupBy(entry => entry.Costume3dId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Costume3dModelMaster>)group.ToList());

        var body = ResolveCostume(costumesById, character3d.BodyCostume3dId, character3d.Unit, "body");
        var hair = ResolveCostume(costumesById, character3d.HairCostume3dId, character3d.Unit, "hair");
        var head = ResolveCostume(costumesById, character3d.HeadCostume3dId, character3d.Unit, "head");

        var bodyDefaultPath = ResolveBodyBundlePath(normalizedAssetRoot, body.AssetbundleName!, character);
        var bodyColorVariationPath = ResolveBodyColorVariationPath(
            normalizedAssetRoot,
            body.AssetbundleName!,
            ResolveBodyBundleFileName(character),
            body.ColorAssetbundleName
        );
        var bodyPath = bodyDefaultPath;
        var hairBundle = ResolveFaceBundle(normalizedAssetRoot, hair.AssetbundleName!, "hair", character);
        var hairPath = hairBundle.Path;
        var headType = head.HeadCostume3dAssetbundleType ?? "unknown";
        var hairType = hair.HeadCostume3dAssetbundleType ?? "head_and_hair";
        var hairBundleKind = ClassifyFaceBundleName(hair.AssetbundleName);
        var headBundleKind = ClassifyHeadBundleName(head);
        var hairVariantGroupKey = ResolveFaceVariantGroupKey(hair.AssetbundleName);
        var headVariantGroupKey = ResolveFaceVariantGroupKey(head.AssetbundleName);
        var completeHeadBundle = ResolveCompleteHeadBundle(normalizedAssetRoot, head, character);
        var completeHeadPath = completeHeadBundle?.Path;
        var hairDrivenSameGroupHead =
            hairBundleKind == "alternate_hair_no_accessory"
            && !string.IsNullOrWhiteSpace(hairVariantGroupKey)
            && string.Equals(hairVariantGroupKey, headVariantGroupKey, StringComparison.OrdinalIgnoreCase)
            && completeHeadPath is not null;
        var useHeadAsMain = completeHeadPath is not null;
        var mainHeadPath = useHeadAsMain ? completeHeadPath! : hairPath;
        var mainHeadAssetbundleName = useHeadAsMain ? completeHeadBundle!.AssetbundleName : hairBundle.AssetbundleName;
        var mainHeadModel = useHeadAsMain ? head : hair;
        var mainHeadColorVariationPath = ResolveFaceColorVariationPath(
            normalizedAssetRoot,
            mainHeadModel.AssetbundleName,
            mainHeadModel.ColorAssetbundleName
        );
        var mainHeadType = useHeadAsMain ? headType : hairType;
        var mainHeadMode = useHeadAsMain ? "complete_head" : "base_hair";
        // Same-group n/letter/unsuffixed face bundles are complete head alternatives, not attachable
        // accessories. Composing them creates duplicate head meshes.
        var accessory = hairDrivenSameGroupHead
            ? ResolvedAccessoryHead.None
            : ResolveAccessoryHead(normalizedAssetRoot, head);
        var hasAccessory = accessory.AssetbundleName is not null;
        var headTextureFallbackPath = hairDrivenSameGroupHead ? hairPath : null;
        var headTextureFallbackAssetbundleName = hairDrivenSameGroupHead ? hairBundle.AssetbundleName : null;
        var assetBundleNames = BuildAssetBundleNames(
            mainHeadAssetbundleName,
            accessory.AssetbundleName,
            head.ColorAssetbundleName,
            body.AssetbundleName!,
            body.ColorAssetbundleName
        );
        var assetBundlePaths = BuildAssetBundlePaths(
            mainHeadPath,
            accessory.Path,
            accessory.ColorVariationPath,
            bodyPath,
            bodyColorVariationPath
        );
        var headCompositionKind = hairDrivenSameGroupHead
            ? "complete_head_with_same_group_hair_texture_fallback"
            : ResolveHeadCompositionKind(
                hairBundleKind,
                headBundleKind,
                hairVariantGroupKey,
                headVariantGroupKey,
                useHeadAsMain,
                hasAccessory
            );

        return new ResolvedCharacter3dCostume(
            Character3dId: character3d.Id,
            CharacterId: character3d.CharacterId,
            CharacterName: character3d.Name,
            Unit: character3d.Unit,
            BodyPath: bodyPath,
            BodyColorVariationPath: bodyColorVariationPath,
            HairPath: hairPath,
            MainHeadPath: mainHeadPath,
            MainHeadAssetbundleName: mainHeadAssetbundleName,
            MainHeadColorVariationPath: mainHeadColorVariationPath,
            MainHeadMode: hasAccessory ? $"{mainHeadMode}_with_optional_accessory" : mainHeadMode,
            MainHeadCostumeType: mainHeadType,
            HeadTextureFallbackPath: headTextureFallbackPath,
            HeadTextureFallbackAssetbundleName: headTextureFallbackAssetbundleName,
            HairBundleKind: hairBundleKind,
            HairVariantGroupKey: hairVariantGroupKey,
            HeadBundleKind: headBundleKind,
            HeadVariantGroupKey: headVariantGroupKey,
            HeadCompositionKind: headCompositionKind,
            AccessoryHeadPath: accessory.Path,
            AccessoryHeadAssetbundleName: accessory.AssetbundleName,
            AccessoryHeadCostumeType: hasAccessory ? headType : null,
            AccessoryAttachNode: accessory.AttachNode,
            AccessoryColorAssetbundleName: head.ColorAssetbundleName,
            AccessoryColorVariationPath: accessory.ColorVariationPath,
            AssetBundleNames: assetBundleNames,
            AssetBundlePaths: assetBundlePaths,
            BodyCostume3dId: character3d.BodyCostume3dId,
            HairCostume3dId: character3d.HairCostume3dId,
            HeadCostume3dId: character3d.HeadCostume3dId,
            BodyAssetbundleName: body.AssetbundleName!,
            HairAssetbundleName: hair.AssetbundleName!,
            HeadAssetbundleName: head.AssetbundleName ?? string.Empty,
            BodyColorAssetbundleName: body.ColorAssetbundleName,
            HairColorAssetbundleName: hair.ColorAssetbundleName,
            HeadColorAssetbundleName: head.ColorAssetbundleName
        );
    }

    private static Costume3dModelMaster ResolveCostume(
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> costumesById,
        int costume3dId,
        string? unit,
        string label
    )
    {
        if (!costumesById.TryGetValue(costume3dId, out var costumes) || costumes.Count == 0)
        {
            throw new InvalidOperationException($"{label} costume3dId {costume3dId} was not found.");
        }
        return string.IsNullOrWhiteSpace(unit)
            ? costumes.First()
            : costumes.FirstOrDefault(entry =>
                string.Equals(entry.Unit, unit, StringComparison.OrdinalIgnoreCase)
            ) ?? costumes.First();
    }

    private static string ResolveBodyBundlePath(
        string assetRoot,
        string assetbundleName,
        GameCharacterMaster character
    )
    {
        var directory = ResolveAssetDirectory(assetRoot, "body", assetbundleName);
        var fileName = ResolveBodyBundleFileName(character);
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            return path;
        }

        if (Directory.Exists(directory))
        {
            var fallback = Directory
                .GetFiles(directory, "*.bundle", SearchOption.TopDirectoryOnly)
                .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fallback is not null)
            {
                return fallback;
            }
        }

        throw new FileNotFoundException($"Body bundle was not found: {path}");
    }

    private static ResolvedFaceBundle ResolveFaceBundle(
        string assetRoot,
        string assetbundleName,
        string label,
        GameCharacterMaster character
    )
    {
        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        var effectiveName = ResolveFaceModelTypeBundleName(assetRoot, normalizedName, character);
        var path = ResolveExistingFaceBundlePath(assetRoot, effectiveName);
        if (path is not null)
        {
            return new ResolvedFaceBundle(effectiveName, path);
        }

        if (!string.Equals(effectiveName, normalizedName, StringComparison.Ordinal))
        {
            path = ResolveExistingFaceBundlePath(assetRoot, normalizedName);
            if (path is not null)
            {
                return new ResolvedFaceBundle(normalizedName, path);
            }
        }

        var fallback = ResolveDefaultFaceBundleFallback(assetRoot, normalizedName);
        if (fallback is not null)
        {
            return fallback;
        }

        throw new FileNotFoundException($"{label} face bundle was not found: {Path.Combine(ResolveAssetBaseDirectory(assetRoot, "face"), $"{ToSystemPath(normalizedName)}.bundle")}");
    }

    private static string? ResolveExistingFaceBundlePath(string assetRoot, string assetbundleName)
    {
        var path = Path.Combine(
            ResolveAssetBaseDirectory(assetRoot, "face"),
            $"{ToSystemPath(assetbundleName)}.bundle"
        );
        if (File.Exists(path))
        {
            return path;
        }

        var gzipPath = path + ".gz";
        return File.Exists(gzipPath) ? gzipPath : null;
    }

    private static ResolvedFaceBundle? ResolveDefaultFaceBundleFallback(
        string assetRoot,
        string normalizedAssetbundleName
    )
    {
        var trimmedName = normalizedAssetbundleName.Trim('/');
        var leaf = Path.GetFileName(trimmedName);
        if (string.IsNullOrWhiteSpace(leaf) || leaf.Any(static character => character != '0'))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(trimmedName)?.Replace('\\', '/') ?? string.Empty;
        var fallbackLeaf = new string('0', Math.Max(leaf.Length - 1, 0)) + "1";
        var fallbackName = string.IsNullOrWhiteSpace(directory)
            ? fallbackLeaf
            : $"{directory}/{fallbackLeaf}";
        var fallbackPath = ResolveExistingFaceBundlePath(assetRoot, fallbackName);

        return fallbackPath is not null
            ? new ResolvedFaceBundle(fallbackName, fallbackPath)
            : null;
    }

    private static ResolvedFaceBundle? ResolveCompleteHeadBundle(
        string assetRoot,
        Costume3dModelMaster head,
        GameCharacterMaster character
    )
    {
        var headType = head.HeadCostume3dAssetbundleType ?? string.Empty;
        if (!IsCompleteHeadCostume(headType) || string.IsNullOrWhiteSpace(head.AssetbundleName))
        {
            return null;
        }

        return ResolveFaceBundle(assetRoot, head.AssetbundleName!, "head", character);
    }

    private static ResolvedAccessoryHead ResolveAccessoryHead(
        string assetRoot,
        Costume3dModelMaster head
    )
    {
        var headType = head.HeadCostume3dAssetbundleType ?? string.Empty;
        if (!IsAccessoryHeadCostume(headType) || string.IsNullOrWhiteSpace(head.AssetbundleName))
        {
            return ResolvedAccessoryHead.None;
        }

        var normalizedName = head.AssetbundleName!.Replace('\\', '/').Trim('/');
        var parts = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ResolvedAccessoryHead.None;
        }

        var accessoryId = parts[0];
        var attachNode = !string.IsNullOrWhiteSpace(head.Part)
            ? head.Part!
            : parts.Length > 1 ? parts[1] : string.Empty;
        if (string.IsNullOrWhiteSpace(attachNode))
        {
            return new ResolvedAccessoryHead(
                Path: null,
                AssetbundleName: normalizedName,
                AttachNode: null,
                ColorVariationPath: null
            );
        }

        var optionalPath = ResolveHeadOptionalBundlePath(assetRoot, accessoryId, attachNode);
        var colorPath = ResolveHeadOptionalColorVariationPath(
            assetRoot,
            accessoryId,
            attachNode,
            head.ColorAssetbundleName
        );
        return new ResolvedAccessoryHead(
            Path: optionalPath,
            AssetbundleName: normalizedName,
            AttachNode: attachNode,
            ColorVariationPath: colorPath
        );
    }

    private static string? ResolveHeadOptionalBundlePath(
        string assetRoot,
        string accessoryId,
        string attachNode
    )
    {
        foreach (var baseDirectory in ResolveAssetBaseDirectoryCandidates(assetRoot, "head_optional"))
        {
            var candidate = Path.Combine(baseDirectory, accessoryId, $"{attachNode}.bundle");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveHeadOptionalColorVariationPath(
        string assetRoot,
        string accessoryId,
        string attachNode,
        string? colorAssetbundleName
    )
    {
        if (string.IsNullOrWhiteSpace(colorAssetbundleName))
        {
            return null;
        }

        foreach (var baseDirectory in ResolveColorVariationBaseDirectoryCandidates(assetRoot, "head_optional"))
        {
            var candidate = Path.Combine(
                baseDirectory,
                accessoryId,
                attachNode,
                $"{colorAssetbundleName}.bundle"
            );
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveBodyColorVariationPath(
        string assetRoot,
        string assetbundleName,
        string bodyBundleFileName,
        string? colorAssetbundleName
    )
    {
        if (string.IsNullOrWhiteSpace(colorAssetbundleName))
        {
            return null;
        }

        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        var bodyType = Path.GetFileNameWithoutExtension(bodyBundleFileName);
        foreach (var baseDirectory in ResolveColorVariationBaseDirectoryCandidates(assetRoot, "body"))
        {
            var candidate = Path.Combine(
                baseDirectory,
                ToSystemPath(normalizedName),
                bodyType,
                $"{colorAssetbundleName}.bundle"
            );
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveFaceColorVariationPath(
        string assetRoot,
        string? assetbundleName,
        string? colorAssetbundleName
    )
    {
        if (string.IsNullOrWhiteSpace(assetbundleName) || string.IsNullOrWhiteSpace(colorAssetbundleName))
        {
            return null;
        }

        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        foreach (var baseDirectory in ResolveColorVariationBaseDirectoryCandidates(assetRoot, "face"))
        {
            var candidate = Path.Combine(
                baseDirectory,
                ToSystemPath(normalizedName),
                $"{colorAssetbundleName}.bundle"
            );
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ResolveAssetDirectory(
        string assetRoot,
        string part,
        string assetbundleName
    )
    {
        return Path.Combine(ResolveAssetBaseDirectory(assetRoot, part), ToSystemPath(assetbundleName));
    }

    private static string ResolveAssetBaseDirectory(string assetRoot, string part)
    {
        foreach (var candidate in ResolveAssetBaseDirectoryCandidates(assetRoot, part))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(assetRoot, "live_pv", "model", "characterv2", part);
    }

    private static IEnumerable<string> ResolveAssetBaseDirectoryCandidates(string assetRoot, string part)
    {
        yield return Path.Combine(assetRoot, "live_pv", "model", "characterv2", part);
        yield return Path.Combine(assetRoot, "live_pv", "model", "character", part);
        yield return Path.Combine(assetRoot, part);
    }

    private static IEnumerable<string> ResolveColorVariationBaseDirectoryCandidates(string assetRoot, string part)
    {
        yield return Path.Combine(
            assetRoot,
            "live_pv",
            "model",
            "characterv2",
            "color_variation",
            part
        );
        yield return Path.Combine(
            assetRoot,
            "live_pv",
            "model",
            "character",
            "color_variation",
            part
        );
        yield return Path.Combine(assetRoot, "color_variation", part);
    }

    private sealed record ResolvedAccessoryHead(
        string? Path,
        string? AssetbundleName,
        string? AttachNode,
        string? ColorVariationPath
    )
    {
        public static readonly ResolvedAccessoryHead None = new(
            Path: null,
            AssetbundleName: null,
            AttachNode: null,
            ColorVariationPath: null
        );
    }

    private static string ResolveBodyBundleFileName(GameCharacterMaster character)
    {
        if (string.Equals(character.Figure, "ladies", StringComparison.OrdinalIgnoreCase))
        {
            return $"ladies_{character.BreastSize.ToLowerInvariant()}.bundle";
        }

        return $"{character.Figure.ToLowerInvariant()}.bundle";
    }

    private static string ResolveFaceModelTypeBundleName(
        string assetRoot,
        string normalizedAssetbundleName,
        GameCharacterMaster character
    )
    {
        var suffix = ResolveFaceModelTypeSuffix(character);
        if (suffix is null)
        {
            return normalizedAssetbundleName;
        }

        var candidateName = $"{normalizedAssetbundleName}_{suffix}";
        return ResolveExistingFaceBundlePath(assetRoot, candidateName) is not null
            ? candidateName
            : normalizedAssetbundleName;
    }

    private static string? ResolveFaceModelTypeSuffix(GameCharacterMaster character)
    {
        if (character.FaceModelType is not { } value ||
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var raw = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();
        raw = raw?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return raw.ToLowerInvariant();
    }

    private static IReadOnlyList<string> BuildAssetBundleNames(
        string faceAssetbundleName,
        string? accessoryAssetbundleName,
        string? accessoryColorAssetbundleName,
        string bodyAssetbundleName,
        string? bodyColorAssetbundleName
    )
    {
        var names = new List<string> { faceAssetbundleName };
        if (!string.IsNullOrWhiteSpace(accessoryAssetbundleName))
        {
            names.Add(accessoryAssetbundleName!);
            if (!string.IsNullOrWhiteSpace(accessoryColorAssetbundleName))
            {
                names.Add($"{accessoryAssetbundleName}/{accessoryColorAssetbundleName}");
            }
        }
        names.Add(bodyAssetbundleName);
        if (!string.IsNullOrWhiteSpace(bodyColorAssetbundleName))
        {
            names.Add($"{bodyAssetbundleName}/{bodyColorAssetbundleName}");
        }
        return names;
    }

    private static IReadOnlyList<string> BuildAssetBundlePaths(params string?[] paths)
    {
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();
    }

    private static bool IsCompleteHeadCostume(string type)
    {
        return type is "head_and_hair";
    }

    private static bool IsAccessoryHeadCostume(string type)
    {
        return type is "head_only" or "head_all" or "head_front" or "head_back";
    }

    private static string ClassifyFaceBundleName(string? assetbundleName)
    {
        if (string.IsNullOrWhiteSpace(assetbundleName))
        {
            return "none";
        }

        var leaf = Path.GetFileName(assetbundleName.Replace('\\', '/'));
        if (leaf.All(static character => character == '0'))
        {
            return "default_hair_0000";
        }
        if (leaf.EndsWith('n') || leaf.EndsWith('N'))
        {
            return "alternate_hair_no_accessory";
        }
        if (leaf.Length > 0 && char.IsAsciiLetter(leaf[^1]))
        {
            return "alternate_hair_accessory_variant";
        }

        return "complete_hair_or_head";
    }

    private static string ClassifyHeadBundleName(Costume3dModelMaster head)
    {
        if (IsAccessoryHeadCostume(head.HeadCostume3dAssetbundleType ?? string.Empty))
        {
            return string.IsNullOrWhiteSpace(head.AssetbundleName)
                ? "empty_head_optional_slot"
                : "head_optional_accessory";
        }

        return ClassifyFaceBundleName(head.AssetbundleName);
    }

    private static string ResolveFaceVariantGroupKey(string? assetbundleName)
    {
        if (string.IsNullOrWhiteSpace(assetbundleName))
        {
            return string.Empty;
        }

        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        var directory = Path.GetDirectoryName(normalizedName)?.Replace('\\', '/') ?? string.Empty;
        var leaf = Path.GetFileName(normalizedName);
        var groupLeaf = leaf;
        if (leaf.Length > 0 && char.IsAsciiLetter(leaf[^1]))
        {
            groupLeaf = leaf[..^1];
        }

        return string.IsNullOrWhiteSpace(directory)
            ? groupLeaf
            : $"{directory}/{groupLeaf}";
    }

    private static string ResolveHeadCompositionKind(
        string hairBundleKind,
        string headBundleKind,
        string hairVariantGroupKey,
        string headVariantGroupKey,
        bool useHeadAsMain,
        bool hasAccessory
    )
    {
        if (hasAccessory)
        {
            return hairBundleKind == "alternate_hair_no_accessory"
                ? "alternate_hair_with_head_optional_accessory"
                : "base_hair_with_head_optional_accessory";
        }

        if (useHeadAsMain)
        {
            if (
                hairBundleKind == "alternate_hair_no_accessory"
                && !string.IsNullOrWhiteSpace(hairVariantGroupKey)
                && string.Equals(hairVariantGroupKey, headVariantGroupKey, StringComparison.OrdinalIgnoreCase)
            )
            {
                return headBundleKind == "alternate_hair_accessory_variant"
                    ? "alternate_hair_with_lettered_accessory"
                    : "alternate_hair_with_complete_head_accessory";
            }

            return "complete_head";
        }

        return hairBundleKind == "alternate_hair_no_accessory"
            ? "alternate_hair_no_accessory"
            : "base_hair";
    }

    private static string ToSystemPath(string assetbundleName)
    {
        return assetbundleName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private sealed record ResolvedFaceBundle(string AssetbundleName, string Path);

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Master file was not found: {path}");
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse master file: {path}");
    }
}
