using System.Text.Json;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class CostumeRegistryExporter
{
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
    };

    public CostumeRegistryExport Export(string masterDirectory, string assetRoot, string outputDirectory)
    {
        var export = ExportInMemory(masterDirectory, assetRoot);
        var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);

        Directory.CreateDirectory(normalizedOutputDirectory);
        Directory.CreateDirectory(Path.Combine(normalizedOutputDirectory, "parts"));
        WriteJson(Path.Combine(normalizedOutputDirectory, "character3d-index.json"), export.Character3dIndex);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "part-registry.json"), export.PartRegistry);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "head-hair-compatibility.json"), export.HeadHairCompatibility);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "card-costume-unlocks.json"), export.CardCostumeUnlocks);

        PrintSummary(export, normalizedOutputDirectory);
        return export;
    }

    public CostumeRegistryExport ExportInMemory(string masterDirectory, string assetRoot)
    {
        var normalizedMasterDirectory = Path.GetFullPath(masterDirectory);
        var normalizedAssetRoot = Path.GetFullPath(assetRoot);

        var character3ds = ReadMaster<IReadOnlyList<Character3dMaster>>(normalizedMasterDirectory, "character3ds.json");
        var costume3ds = ReadMaster<IReadOnlyList<Costume3dMaster>>(normalizedMasterDirectory, "costume3ds.json");
        var costumeModels = ReadMaster<IReadOnlyList<Costume3dModelMaster>>(normalizedMasterDirectory, "costume3dModels.json");
        var gameCharacters = ReadMaster<IReadOnlyList<GameCharacterMaster>>(normalizedMasterDirectory, "gameCharacters.json");
        var cards = ReadMaster<IReadOnlyList<CardMaster>>(normalizedMasterDirectory, "cards.json");
        var cardCostumes = ReadMaster<IReadOnlyList<CardCostume3dMaster>>(normalizedMasterDirectory, "cardCostume3ds.json");
        var availablePatterns = ReadMaster<IReadOnlyList<Costume3dModelPatternMaster>>(
            normalizedMasterDirectory,
            "costume3dModelAvailablePatterns.json"
        );
        var notAvailablePatterns = ReadMaster<IReadOnlyList<Costume3dModelPatternMaster>>(
            normalizedMasterDirectory,
            "costume3dModelNotAvailablePatterns.json"
        );
        var defaultHairs = ReadMaster<IReadOnlyList<Costume3dModelPatternMaster>>(
            normalizedMasterDirectory,
            "costume3dModelDefaultHairs.json"
        );

        var source = new Dictionary<string, string>
        {
            ["masterDirectory"] = normalizedMasterDirectory,
            ["assetRoot"] = normalizedAssetRoot,
        };
        var costumeById = costume3ds.ToDictionary(entry => entry.Id);
        var characterById = gameCharacters.ToDictionary(entry => entry.Id);
        var modelsByCostumeId = costumeModels
            .GroupBy(entry => entry.Costume3dId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Costume3dModelMaster>)group.ToList());
        var cardsById = cards.ToDictionary(entry => entry.Id);

        return new CostumeRegistryExport(
            Character3dIndex: BuildCharacter3dIndex(
                character3ds,
                costumeById,
                modelsByCostumeId,
                source
            ),
            PartRegistry: BuildPartRegistry(
                costume3ds,
                modelsByCostumeId,
                characterById,
                normalizedAssetRoot,
                source
            ),
            HeadHairCompatibility: BuildHeadHairCompatibility(
                availablePatterns,
                notAvailablePatterns,
                defaultHairs,
                costumeById,
                modelsByCostumeId,
                source
            ),
            CardCostumeUnlocks: BuildCardCostumeUnlocks(
                cardCostumes,
                cardsById,
                costumeById,
                source
            )
        );
    }

    private static Character3dIndex BuildCharacter3dIndex(
        IReadOnlyList<Character3dMaster> character3ds,
        IReadOnlyDictionary<int, Costume3dMaster> costumeById,
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId,
        IReadOnlyDictionary<string, string> source
    )
    {
        var entries = character3ds
            .OrderBy(entry => entry.Id)
            .Select(entry =>
            {
                var warnings = new List<string>();
                AddPresetWarnings(warnings, "body", entry.BodyCostume3dId, "body", costumeById, modelsByCostumeId);
                AddPresetWarnings(warnings, "head", entry.HeadCostume3dId, "head", costumeById, modelsByCostumeId);
                AddPresetWarnings(warnings, "hair", entry.HairCostume3dId, "hair", costumeById, modelsByCostumeId);

                return new Character3dIndexEntry(
                    Character3dId: entry.Id,
                    CharacterId: entry.CharacterId,
                    Unit: entry.Unit,
                    Name: entry.Name,
                    BodyCostume3dId: entry.BodyCostume3dId,
                    HeadCostume3dId: entry.HeadCostume3dId,
                    HairCostume3dId: entry.HairCostume3dId,
                    OutputPath: $"presets/{entry.Id}/",
                    Status: warnings.Count == 0 ? "available" : "partial",
                    Warnings: warnings
                );
            })
            .ToList();

        return new Character3dIndex(Version: 1, Source: source, Entries: entries);
    }

    private static PartRegistry BuildPartRegistry(
        IReadOnlyList<Costume3dMaster> costume3ds,
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot,
        IReadOnlyDictionary<string, string> source
    )
    {
        var entries = new List<PartRegistryEntry>();
        foreach (var costume in costume3ds.OrderBy(entry => entry.Id))
        {
            if (!modelsByCostumeId.TryGetValue(costume.Id, out var models) || models.Count == 0)
            {
                entries.Add(BuildPartEntry(costume, null, null, null, null, null, "missing", new[] { "missing costume3dModels row" }));
                continue;
            }

            foreach (var model in models.OrderBy(entry => entry.Unit ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var warnings = new List<string>();
                var bundlePath = ResolveBundlePath(costume, model, characterById, assetRoot, warnings);
                var colorPath = ResolveColorVariationBundlePath(costume, model, characterById, assetRoot, warnings);
                var registryPartType = ResolveRegistryPartType(costume.PartType, model);
                var packagePath = BuildPackagePath(registryPartType, costume.Id, model.Unit);
                var status = bundlePath is null
                    ? "missing"
                    : "planned";
                entries.Add(BuildPartEntry(costume, model, bundlePath, colorPath, ResolveAttachNode(model), packagePath, status, warnings));
            }
        }

        return new PartRegistry(Version: 1, Source: source, Entries: entries);
    }

    private static HeadHairCompatibilityRegistry BuildHeadHairCompatibility(
        IReadOnlyList<Costume3dModelPatternMaster> availablePatterns,
        IReadOnlyList<Costume3dModelPatternMaster> notAvailablePatterns,
        IReadOnlyList<Costume3dModelPatternMaster> defaultHairs,
        IReadOnlyDictionary<int, Costume3dMaster> costumeById,
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId,
        IReadOnlyDictionary<string, string> source
    )
    {
        var available = NormalizePatterns(availablePatterns);
        var notAvailable = NormalizePatterns(notAvailablePatterns);
        var defaults = NormalizePatterns(defaultHairs);
        var keys = available.Keys.Concat(notAvailable.Keys).Concat(defaults.Keys)
            .Distinct()
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var rules = new List<HeadHairCompatibilityRule>();

        foreach (var key in keys)
        {
            available.TryGetValue(key, out var availablePattern);
            notAvailable.TryGetValue(key, out var notAvailablePattern);
            defaults.TryGetValue(key, out var defaultPattern);
            var chosen = notAvailablePattern ?? availablePattern ?? defaultPattern!;
            var state = notAvailablePattern is not null
                ? "not_available"
                : availablePattern is not null ? "available" : "default_hint";
            var sources = new List<string>();
            if (availablePattern is not null)
            {
                sources.Add("costume3dModelAvailablePatterns");
            }
            if (notAvailablePattern is not null)
            {
                sources.Add("costume3dModelNotAvailablePatterns");
            }
            if (defaultPattern is not null)
            {
                sources.Add("costume3dModelDefaultHairs");
            }

            var warnings = new List<string>();
            AddPatternReferenceWarnings(warnings, "head", chosen.HeadCostume3dId, "head", costumeById, modelsByCostumeId);
            AddPatternReferenceWarnings(warnings, "hair", chosen.HairCostume3dId, "hair", costumeById, modelsByCostumeId);

            rules.Add(new HeadHairCompatibilityRule(
                Unit: chosen.Unit,
                HeadCostume3dId: chosen.HeadCostume3dId,
                HairCostume3dId: chosen.HairCostume3dId,
                State: state,
                IsDefault: availablePattern?.IsDefault == true || defaultPattern is not null,
                Source: sources,
                Warnings: warnings
            ));
        }

        return new HeadHairCompatibilityRegistry(Version: 1, Source: source, Rules: rules);
    }

    private static CardCostumeUnlockRegistry BuildCardCostumeUnlocks(
        IReadOnlyList<CardCostume3dMaster> cardCostumes,
        IReadOnlyDictionary<int, CardMaster> cardsById,
        IReadOnlyDictionary<int, Costume3dMaster> costumeById,
        IReadOnlyDictionary<string, string> source
    )
    {
        var entries = cardCostumes
            .GroupBy(entry => entry.CardId)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                cardsById.TryGetValue(group.Key, out var card);
                var warnings = new List<string>();
                if (card is null)
                {
                    warnings.Add("missing cards row");
                }

                var costumes = group
                    .OrderBy(entry => entry.Costume3dId)
                    .Select(entry =>
                    {
                        costumeById.TryGetValue(entry.Costume3dId, out var costume);
                        if (costume is null)
                        {
                            warnings.Add($"missing costume3ds row for costume3dId {entry.Costume3dId}");
                        }

                        return new CardCostumeUnlockCostume(
                            Costume3dId: entry.Costume3dId,
                            PartType: costume?.PartType,
                            Costume3dGroupId: costume?.Costume3dGroupId,
                            ColorId: costume?.ColorId,
                            Name: costume?.Name,
                            IsInitialObtainHair: entry.IsInitialObtainHair
                        );
                    })
                    .ToList();

                return new CardCostumeUnlockEntry(
                    CardId: group.Key,
                    CharacterId: card?.CharacterId ?? 0,
                    CardRarityType: card?.CardRarityType,
                    Prefix: card?.Prefix,
                    AssetbundleName: card?.AssetbundleName,
                    ReleaseAt: card?.ReleaseAt,
                    Costumes: costumes,
                    Warnings: warnings.Distinct().ToList()
                );
            })
            .ToList();

        return new CardCostumeUnlockRegistry(Version: 1, Source: source, Entries: entries);
    }

    private static PartRegistryEntry BuildPartEntry(
        Costume3dMaster costume,
        Costume3dModelMaster? model,
        string? bundlePath,
        string? colorVariationPath,
        string? attachNode,
        string? packagePath,
        string status,
        IReadOnlyList<string> warnings
    )
    {
        return new PartRegistryEntry(
            Costume3dId: costume.Id,
            PartType: ResolveRegistryPartType(costume.PartType, model),
            CharacterId: costume.CharacterId,
            Unit: model?.Unit,
            Name: costume.Name,
            ColorId: costume.ColorId,
            ColorName: costume.ColorName,
            Costume3dGroupId: costume.Costume3dGroupId,
            CostumeAssetbundleName: costume.AssetbundleName,
            ModelAssetbundleName: model?.AssetbundleName,
            ColorAssetbundleName: model?.ColorAssetbundleName,
            HeadCostume3dAssetbundleType: model?.HeadCostume3dAssetbundleType,
            Part: model?.Part,
            BundlePath: bundlePath,
            ColorVariationBundlePath: colorVariationPath,
            PackagePath: packagePath ?? BuildPackagePath(costume.PartType, costume.Id, model?.Unit),
            AttachNode: attachNode,
            Status: status,
            Warnings: warnings.Distinct().ToList()
        );
    }

    private static string BuildPackagePath(string partType, int costume3dId, string? unit)
    {
        return $"parts/{NormalizePackagePartType(partType)}/{costume3dId}/{unit ?? "default"}/";
    }

    private static string NormalizePackagePartType(string partType)
    {
        return partType.Equals("accessory", StringComparison.OrdinalIgnoreCase)
            ? "head_optional"
            : partType;
    }

    private static string ResolveRegistryPartType(string partType, Costume3dModelMaster? model)
    {
        if (model is not null && IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType))
        {
            return "head_optional";
        }

        return NormalizePackagePartType(partType);
    }

    private static string? ResolveBundlePath(
        Costume3dMaster costume,
        Costume3dModelMaster model,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot,
        List<string> warnings
    )
    {
        if (string.IsNullOrWhiteSpace(model.AssetbundleName))
        {
            warnings.Add("missing model assetbundleName");
            return null;
        }

        return costume.PartType switch
        {
            "body" => ResolveBodyBundlePath(model.AssetbundleName!, costume.CharacterId, characterById, assetRoot, warnings),
            "hair" => ResolveFaceBundlePath(model.AssetbundleName!, assetRoot),
            "head" => IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType)
                ? ResolveHeadOptionalBundlePath(model, assetRoot, warnings)
                : ResolveFaceBundlePath(model.AssetbundleName!, assetRoot),
            _ => null,
        };
    }

    private static string? ResolveColorVariationBundlePath(
        Costume3dMaster costume,
        Costume3dModelMaster model,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot,
        List<string> warnings
    )
    {
        if (string.IsNullOrWhiteSpace(model.ColorAssetbundleName))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(model.AssetbundleName))
        {
            warnings.Add("colorAssetbundleName exists but model assetbundleName is missing");
            return null;
        }

        var path = costume.PartType switch
        {
            "body" => ResolveBodyColorVariationPath(model, costume.CharacterId, characterById, assetRoot),
            "hair" => ResolveFaceColorVariationPath(model, assetRoot),
            "head" => IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType)
                ? ResolveHeadOptionalColorVariationPath(model, assetRoot)
                : ResolveFaceColorVariationPath(model, assetRoot),
            _ => null,
        };

        return path;
    }

    private static string? ResolveBodyBundlePath(
        string assetbundleName,
        int characterId,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot,
        List<string> warnings
    )
    {
        if (!characterById.TryGetValue(characterId, out var character))
        {
            warnings.Add($"missing gameCharacters row for characterId {characterId}");
            return null;
        }

        var directory = ResolveAssetDirectory(assetRoot, "body", assetbundleName);
        return Path.Combine(directory, ResolveBodyBundleFileName(character));
    }

    private static string? ResolveFaceBundlePath(string assetbundleName, string assetRoot)
    {
        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        return Path.Combine(ResolveAssetBaseDirectory(assetRoot, "face"), $"{ToSystemPath(normalizedName)}.bundle");
    }

    private static string? ResolveHeadOptionalBundlePath(
        Costume3dModelMaster model,
        string assetRoot,
        List<string> warnings
    )
    {
        var (accessoryId, attachNode) = ResolveAccessoryIdAndAttachNode(model);
        if (string.IsNullOrWhiteSpace(accessoryId) || string.IsNullOrWhiteSpace(attachNode))
        {
            warnings.Add("head_only row has no accessory id or attach node");
            return null;
        }

        return Path.Combine(ResolveAssetBaseDirectory(assetRoot, "head_optional"), accessoryId, $"{attachNode}.bundle");
    }

    private static string? ResolveBodyColorVariationPath(
        Costume3dModelMaster model,
        int characterId,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot
    )
    {
        if (!characterById.TryGetValue(characterId, out var character))
        {
            return null;
        }

        var normalizedName = model.AssetbundleName!.Replace('\\', '/').Trim('/');
        var bodyType = Path.GetFileNameWithoutExtension(ResolveBodyBundleFileName(character));
        return Path.Combine(
            ResolveColorVariationBaseDirectory(assetRoot, "body"),
            ToSystemPath(normalizedName),
            bodyType,
            $"{model.ColorAssetbundleName}.bundle"
        );
    }

    private static string? ResolveFaceColorVariationPath(Costume3dModelMaster model, string assetRoot)
    {
        var normalizedName = model.AssetbundleName!.Replace('\\', '/').Trim('/');
        return Path.Combine(
            ResolveColorVariationBaseDirectory(assetRoot, "face"),
            ToSystemPath(normalizedName),
            $"{model.ColorAssetbundleName}.bundle"
        );
    }

    private static string? ResolveHeadOptionalColorVariationPath(Costume3dModelMaster model, string assetRoot)
    {
        var (accessoryId, attachNode) = ResolveAccessoryIdAndAttachNode(model);
        if (string.IsNullOrWhiteSpace(accessoryId) || string.IsNullOrWhiteSpace(attachNode))
        {
            return null;
        }

        return Path.Combine(
            ResolveColorVariationBaseDirectory(assetRoot, "head_optional"),
            accessoryId,
            attachNode,
            $"{model.ColorAssetbundleName}.bundle"
        );
    }

    private static (string? AccessoryId, string? AttachNode) ResolveAccessoryIdAndAttachNode(Costume3dModelMaster model)
    {
        var normalizedName = model.AssetbundleName?.Replace('\\', '/').Trim('/') ?? string.Empty;
        var parts = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return (null, null);
        }

        var attachNode = !string.IsNullOrWhiteSpace(model.Part)
            ? model.Part
            : parts.Length > 1 ? parts[1] : null;
        return (parts[0], attachNode);
    }

    private static string? ResolveAttachNode(Costume3dModelMaster? model)
    {
        if (model is null || !IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType))
        {
            return null;
        }

        return ResolveAccessoryIdAndAttachNode(model).AttachNode;
    }

    private static Dictionary<string, Costume3dModelPatternMaster> NormalizePatterns(
        IReadOnlyList<Costume3dModelPatternMaster> patterns
    )
    {
        var result = new Dictionary<string, Costume3dModelPatternMaster>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            var key = PatternKey(pattern);
            if (result.TryGetValue(key, out var existing))
            {
                result[key] = existing with { IsDefault = existing.IsDefault == true || pattern.IsDefault == true };
                continue;
            }

            result[key] = pattern;
        }

        return result;
    }

    private static void AddPresetWarnings(
        List<string> warnings,
        string label,
        int costume3dId,
        string expectedPartType,
        IReadOnlyDictionary<int, Costume3dMaster> costumeById,
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId
    )
    {
        if (!costumeById.TryGetValue(costume3dId, out var costume))
        {
            warnings.Add($"missing {label} costume3ds row {costume3dId}");
            return;
        }
        if (!string.Equals(costume.PartType, expectedPartType, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{label} costume3dId {costume3dId} has partType {costume.PartType}");
        }
        if (!modelsByCostumeId.ContainsKey(costume3dId))
        {
            warnings.Add($"missing {label} costume3dModels row {costume3dId}");
        }
    }

    private static void AddPatternReferenceWarnings(
        List<string> warnings,
        string label,
        int costume3dId,
        string expectedPartType,
        IReadOnlyDictionary<int, Costume3dMaster> costumeById,
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId
    )
    {
        if (!costumeById.TryGetValue(costume3dId, out var costume))
        {
            warnings.Add($"missing {label} costume3ds row {costume3dId}");
            return;
        }
        if (!string.Equals(costume.PartType, expectedPartType, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{label} costume3dId {costume3dId} has partType {costume.PartType}");
        }
        if (!modelsByCostumeId.ContainsKey(costume3dId))
        {
            warnings.Add($"missing {label} costume3dModels row {costume3dId}");
        }
    }

    private static string PatternKey(Costume3dModelPatternMaster pattern)
    {
        return $"{pattern.Unit ?? string.Empty}|{pattern.HeadCostume3dId}|{pattern.HairCostume3dId}";
    }

    private static string ResolveAssetDirectory(string assetRoot, string part, string assetbundleName)
    {
        return Path.Combine(ResolveAssetBaseDirectory(assetRoot, part), ToSystemPath(assetbundleName));
    }

    private static string ResolveAssetBaseDirectory(string assetRoot, string part)
    {
        return Path.Combine(assetRoot, "live_pv", "model", "characterv2", part);
    }

    private static string ResolveColorVariationBaseDirectory(string assetRoot, string part)
    {
        return Path.Combine(assetRoot, "live_pv", "model", "characterv2", "color_variation", part);
    }

    private static string ResolveBodyBundleFileName(GameCharacterMaster character)
    {
        if (string.Equals(character.Figure, "ladies", StringComparison.OrdinalIgnoreCase))
        {
            return $"ladies_{character.BreastSize.ToLowerInvariant()}.bundle";
        }

        return $"{character.Figure.ToLowerInvariant()}.bundle";
    }

    private static bool IsAccessoryHeadCostume(string? type)
    {
        return string.Equals(type, "head_only", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSystemPath(string assetbundleName)
    {
        return assetbundleName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static T ReadMaster<T>(string masterDirectory, string fileName)
    {
        var path = Path.Combine(masterDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Master file was not found: {path}");
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, ReadJsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse master file: {path}");
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, WriteJsonOptions));
    }

    private static void PrintSummary(CostumeRegistryExport export, string outputDirectory)
    {
        var missingParts = export.PartRegistry.Entries.Count(entry => entry.Status == "missing");
        var patternWarnings = export.HeadHairCompatibility.Rules.Count(entry => entry.Warnings.Count > 0);
        Console.WriteLine($"Wrote costume registries to {outputDirectory}");
        Console.WriteLine($"  character3d presets: {export.Character3dIndex.Entries.Count}");
        Console.WriteLine($"  part entries: {export.PartRegistry.Entries.Count} ({missingParts} missing metadata)");
        Console.WriteLine($"  head/hair rules: {export.HeadHairCompatibility.Rules.Count} ({patternWarnings} with warnings)");
        Console.WriteLine($"  card unlock entries: {export.CardCostumeUnlocks.Entries.Count}");
    }
}
