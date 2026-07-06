using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
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

    public CostumeRegistryExport Export(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        string runtimeJsonOutput = RuntimeJsonWriter.Gzip
    )
    {
        var export = ExportInMemory(masterDirectory, assetRoot);
        var normalizedOutputDirectory = Path.GetFullPath(outputDirectory);

        Directory.CreateDirectory(normalizedOutputDirectory);
        Directory.CreateDirectory(Path.Combine(normalizedOutputDirectory, "parts"));
        WriteJson(Path.Combine(normalizedOutputDirectory, "character3d-index.json"), export.Character3dIndex, runtimeJsonOutput);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "part-registry.json"), export.PartRegistry, runtimeJsonOutput);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "part-source-map.json"), export.PartSourceMap, runtimeJsonOutput);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "head-hair-compatibility.json"), export.HeadHairCompatibility, runtimeJsonOutput);
        WriteJson(Path.Combine(normalizedOutputDirectory, "parts", "card-costume-unlocks.json"), export.CardCostumeUnlocks, runtimeJsonOutput);
        WriteScopedPartRegistryIndexes(normalizedOutputDirectory, export, runtimeJsonOutput);
        WriteScopedHeadHairCompatibilityIndexes(normalizedOutputDirectory, export, runtimeJsonOutput);

        PrintSummary(export, normalizedOutputDirectory);
        return export;
    }

    private static void WriteScopedPartRegistryIndexes(
        string outputDirectory,
        CostumeRegistryExport export,
        string runtimeJsonOutput
    )
    {
        var roles = export.PartRegistry.Entries
            .Select(entry => (entry.CharacterId, entry.Unit))
            .Concat(export.Character3dIndex.Entries.Select(entry => (entry.CharacterId, entry.Unit)))
            .Distinct()
            .OrderBy(entry => entry.CharacterId)
            .ThenBy(entry => entry.Unit ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (var role in roles)
        {
            var roleDirectory = Path.Combine(
                outputDirectory,
                "parts",
                "by-role",
                role.CharacterId.ToString(),
                RuntimePathUnitSegment(role.Unit)
            );
            var partRegistry = new PartRegistry(
                Version: export.PartRegistry.Version,
                Source: export.PartRegistry.Source,
                Entries: export.PartRegistry.Entries
                    .Where(entry =>
                        entry.CharacterId == role.CharacterId &&
                        string.Equals(entry.Unit ?? string.Empty, role.Unit ?? string.Empty, StringComparison.Ordinal))
                    .ToList()
            );
            var characterIndex = new Character3dIndex(
                Version: export.Character3dIndex.Version,
                Source: export.Character3dIndex.Source,
                Entries: export.Character3dIndex.Entries
                    .Where(entry =>
                        entry.CharacterId == role.CharacterId &&
                        string.Equals(entry.Unit ?? string.Empty, role.Unit ?? string.Empty, StringComparison.Ordinal))
                    .ToList()
            );

            WriteJson(Path.Combine(roleDirectory, "part-registry.json"), partRegistry, runtimeJsonOutput);
            WriteJson(Path.Combine(roleDirectory, "character3d-index.json"), characterIndex, runtimeJsonOutput);
        }
    }

    private static void WriteScopedHeadHairCompatibilityIndexes(
        string outputDirectory,
        CostumeRegistryExport export,
        string runtimeJsonOutput
    )
    {
        var units = export.HeadHairCompatibility.Rules
            .Select(rule => rule.Unit)
            .Distinct()
            .OrderBy(unit => unit ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (var unit in units)
        {
            var compatibility = new HeadHairCompatibilityRegistry(
                Version: export.HeadHairCompatibility.Version,
                Source: export.HeadHairCompatibility.Source,
                Rules: export.HeadHairCompatibility.Rules
                    .Where(rule => string.Equals(rule.Unit ?? string.Empty, unit ?? string.Empty, StringComparison.Ordinal))
                    .ToList()
            );
            WriteJson(
                Path.Combine(outputDirectory, "parts", "compat", "by-unit", RuntimePathUnitSegment(unit), "head-hair-compatibility.json"),
                compatibility,
                runtimeJsonOutput
            );
        }
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

        var partRegistry = BuildPartRegistry(
            costume3ds,
            character3ds,
            modelsByCostumeId,
            characterById,
            normalizedAssetRoot,
            source
        );

        return new CostumeRegistryExport(
            Character3dIndex: BuildCharacter3dIndex(
                character3ds,
                costumeById,
                modelsByCostumeId,
                source
            ),
            PartRegistry: partRegistry,
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
            ),
            PartSourceMap: BuildPartSourceMap(partRegistry, normalizedAssetRoot, source)
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
                    RoleRuntimePath: BuildRoleRuntimePath(entry.CharacterId, entry.Unit),
                    Status: warnings.Count == 0 ? "available" : "partial",
                    Warnings: warnings
                );
            })
            .ToList();

        return new Character3dIndex(Version: 1, Source: source, Entries: entries);
    }

    private static PartRegistry BuildPartRegistry(
        IReadOnlyList<Costume3dMaster> costume3ds,
        IReadOnlyList<Character3dMaster> character3ds,
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
                entries.Add(BuildPartEntry(costume, null, null, null, null, null, null, "missing", new[] { "missing costume3dModels row" }));
                continue;
            }

            foreach (var model in models.OrderBy(entry => entry.Unit ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var warnings = new List<string>();
                var registryPartType = ResolveRegistryPartType(costume.PartType, model);
                var headOptional = registryPartType == "head_optional"
                    ? ResolveHeadOptionalDescriptor(costume, model)
                    : null;
                var bundlePath = ResolveBundlePath(costume, model, headOptional, characterById, assetRoot, warnings);
                var colorPath = ResolveColorVariationBundlePath(costume, model, headOptional, characterById, assetRoot, warnings);
                var sourceIdentity = BuildSourceIdentity(registryPartType, bundlePath, colorPath, assetRoot);
                var packagePath = sourceIdentity?.PackagePath ?? BuildPackagePath(registryPartType, costume.Id, model.Unit);
                var status = headOptional?.IsEmptySlot == true
                    ? "empty"
                    : bundlePath is null
                    ? "missing"
                    : "planned";
                entries.Add(BuildPartEntry(costume, model, bundlePath, colorPath, sourceIdentity, headOptional?.AttachNode ?? ResolveAttachNode(model), packagePath, status, warnings));
            }
        }

        AddOfficialPresetRoleAliases(entries, character3ds);
        return new PartRegistry(Version: 1, Source: source, Entries: entries);
    }

    private static void AddOfficialPresetRoleAliases(
        List<PartRegistryEntry> entries,
        IReadOnlyList<Character3dMaster> character3ds
    )
    {
        if (character3ds.Count == 0 || entries.Count == 0)
        {
            return;
        }

        var entriesByCostumeAndUnit = entries
            .GroupBy(entry => (entry.Costume3dId, UnitKey(entry.Unit)))
            .ToDictionary(group => group.Key, group => group.ToList());
        var existing = entries
            .Select(PartRegistryRoleKey)
            .ToHashSet(StringComparer.Ordinal);
        var aliases = new List<PartRegistryEntry>();

        foreach (var preset in character3ds.OrderBy(entry => entry.Id))
        {
            AddOfficialPresetPartAlias(entriesByCostumeAndUnit, existing, aliases, preset, preset.BodyCostume3dId);
            AddOfficialPresetPartAlias(entriesByCostumeAndUnit, existing, aliases, preset, preset.HeadCostume3dId);
            AddOfficialPresetPartAlias(entriesByCostumeAndUnit, existing, aliases, preset, preset.HairCostume3dId);
        }

        entries.AddRange(aliases);
    }

    private static void AddOfficialPresetPartAlias(
        IReadOnlyDictionary<(int Costume3dId, string Unit), List<PartRegistryEntry>> entriesByCostumeAndUnit,
        HashSet<string> existing,
        List<PartRegistryEntry> aliases,
        Character3dMaster preset,
        int costume3dId
    )
    {
        var candidates = ResolveOfficialPresetPartCandidates(entriesByCostumeAndUnit, costume3dId, preset.Unit);
        foreach (var entry in candidates)
        {
            if (entry.CharacterId == preset.CharacterId)
            {
                continue;
            }

            var alias = entry with
            {
                CharacterId = preset.CharacterId,
                Unit = string.IsNullOrWhiteSpace(entry.Unit) ? preset.Unit : entry.Unit,
            };
            if (existing.Add(PartRegistryRoleKey(alias)))
            {
                aliases.Add(alias);
            }
        }
    }

    private static IReadOnlyList<PartRegistryEntry> ResolveOfficialPresetPartCandidates(
        IReadOnlyDictionary<(int Costume3dId, string Unit), List<PartRegistryEntry>> entriesByCostumeAndUnit,
        int costume3dId,
        string? unit
    )
    {
        if (entriesByCostumeAndUnit.TryGetValue((costume3dId, UnitKey(unit)), out var exact))
        {
            return exact;
        }
        if (entriesByCostumeAndUnit.TryGetValue((costume3dId, string.Empty), out var defaultUnit))
        {
            return defaultUnit;
        }
        return Array.Empty<PartRegistryEntry>();
    }

    private static string PartRegistryRoleKey(PartRegistryEntry entry)
    {
        return $"{entry.CharacterId}\n{UnitKey(entry.Unit)}\n{entry.PartType}\n{entry.Costume3dId}";
    }

    private static string UnitKey(string? unit)
    {
        return unit ?? string.Empty;
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
            var composition = ResolveHeadHairComposition(chosen, modelsByCostumeId);

            rules.Add(new HeadHairCompatibilityRule(
                Unit: chosen.Unit,
                HeadCostume3dId: chosen.HeadCostume3dId,
                HairCostume3dId: chosen.HairCostume3dId,
                State: state,
                IsDefault: availablePattern?.IsDefault == true || defaultPattern is not null,
                HeadCompositionKind: composition.Kind,
                ActiveContributors: composition.ActiveContributors,
                Source: sources,
                Warnings: warnings
            ));
        }

        return new HeadHairCompatibilityRegistry(Version: 1, Source: source, Rules: rules);
    }

    private static (string Kind, IReadOnlyList<string> ActiveContributors) ResolveHeadHairComposition(
        Costume3dModelPatternMaster pattern,
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId
    )
    {
        var head = ResolvePatternModel(modelsByCostumeId, pattern.HeadCostume3dId, pattern.Unit);
        if (head is not null && IsCompleteHeadCostume(head.HeadCostume3dAssetbundleType))
        {
            return ("complete_head", new[] { "head" });
        }
        if (head is not null && IsAccessoryHeadCostume(head.HeadCostume3dAssetbundleType))
        {
            return ("base_hair_with_head_optional_accessory", new[] { "hair", "head_optional" });
        }

        return ("custom_head_hair", new[] { "head", "hair" });
    }

    private static Costume3dModelMaster? ResolvePatternModel(
        IReadOnlyDictionary<int, IReadOnlyList<Costume3dModelMaster>> modelsByCostumeId,
        int costume3dId,
        string? unit
    )
    {
        if (!modelsByCostumeId.TryGetValue(costume3dId, out var models) || models.Count == 0)
        {
            return null;
        }
        return models.FirstOrDefault(model => string.Equals(model.Unit, unit, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(model => string.IsNullOrWhiteSpace(model.Unit))
            ?? models[0];
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
        PartSourceIdentity? source,
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
            BaseSourceKey: source?.BaseSourceKey,
            SourceKey: source?.SourceKey,
            SourcePackagePath: source?.PackagePath,
            PackagePath: packagePath ?? BuildPackagePath(costume.PartType, costume.Id, model?.Unit),
            AttachNode: attachNode,
            Status: status,
            Warnings: warnings.Distinct().ToList()
        );
    }

    private static PartSourceMap BuildPartSourceMap(
        PartRegistry registry,
        string assetRoot,
        IReadOnlyDictionary<string, string> source
    )
    {
        var entries = registry.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SourceKey))
            .GroupBy(entry => entry.SourceKey!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(entry => entry.PartType, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Costume3dId)
                    .ThenBy(entry => entry.Unit ?? string.Empty, StringComparer.Ordinal)
                    .ToList();
                var representative = ordered[0];
                var aliases = ordered.Select(BuildPartSourceAlias).ToList();
                return new PartSourceMapEntry(
                    SourceKey: representative.SourceKey!,
                    BaseSourceKey: representative.BaseSourceKey!,
                    PartType: representative.PartType,
                    BundlePath: representative.BundlePath!,
                    ColorVariationBundlePath: representative.ColorVariationBundlePath,
                    AssetRootRelativeBundlePath: ToAssetRootRelativePath(assetRoot, representative.BundlePath),
                    AssetRootRelativeColorVariationBundlePath: ToAssetRootRelativePath(assetRoot, representative.ColorVariationBundlePath),
                    PackagePath: representative.PackagePath,
                    Representative: aliases[0],
                    Aliases: aliases
                );
            })
            .ToList();

        return new PartSourceMap(Version: 1, Source: source, Entries: entries);
    }

    private static PartSourceMapAlias BuildPartSourceAlias(PartRegistryEntry entry)
    {
        return new PartSourceMapAlias(
            Costume3dId: entry.Costume3dId,
            PartType: entry.PartType,
            CharacterId: entry.CharacterId,
            Unit: entry.Unit,
            Name: entry.Name,
            ColorId: entry.ColorId,
            ColorName: entry.ColorName,
            Costume3dGroupId: entry.Costume3dGroupId
        );
    }

    private static PartSourceIdentity? BuildSourceIdentity(
        string partType,
        string? bundlePath,
        string? colorVariationPath,
        string assetRoot
    )
    {
        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            return null;
        }

        var normalizedPartType = NormalizePackagePartType(partType);
        var baseRelativePath = ToAssetRootRelativePath(assetRoot, bundlePath) ?? NormalizeSourcePath(bundlePath);
        var colorRelativePath = ToAssetRootRelativePath(assetRoot, colorVariationPath);
        var baseKey = ComputeSourceKey(normalizedPartType, baseRelativePath, null);
        var sourceKey = ComputeSourceKey(normalizedPartType, baseRelativePath, colorRelativePath);
        return new PartSourceIdentity(
            BaseSourceKey: baseKey,
            SourceKey: sourceKey,
            PackagePath: $"parts/_sources/{normalizedPartType}/{sourceKey}/"
        );
    }

    private static string ComputeSourceKey(string partType, string bundlePath, string? colorVariationPath)
    {
        var input = $"{partType}\n{bundlePath}\n{colorVariationPath ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? ToAssetRootRelativePath(string assetRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var relative = Path.GetRelativePath(assetRoot, path);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return NormalizeSourcePath(path);
        }

        return NormalizeSourcePath(relative);
    }

    private static string NormalizeSourcePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string BuildPackagePath(string partType, int costume3dId, string? unit)
    {
        return $"parts/{NormalizePackagePartType(partType)}/{costume3dId}/{unit ?? "default"}/";
    }

    private static string BuildRoleRuntimePath(int characterId, string? unit)
    {
        return $"roles/{characterId}/{RuntimePathUnitSegment(unit)}/role-runtime.json";
    }

    private static string RuntimePathUnitSegment(string? unit)
    {
        return unit ?? "default";
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
        HeadOptionalDescriptor? headOptional,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot,
        List<string> warnings
    )
    {
        return costume.PartType switch
        {
            "body" => RequireModelAssetbundleName(model, warnings) is { } assetbundleName
                ? ResolveBodyBundlePath(assetbundleName, costume.CharacterId, characterById, assetRoot, warnings)
                : null,
            "hair" => RequireModelAssetbundleName(model, warnings) is { } assetbundleName
                ? ResolveFaceBundlePath(assetbundleName, assetRoot, warnings)
                : null,
            "head" => IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType)
                ? ResolveHeadOptionalBundlePath(headOptional ?? ResolveHeadOptionalDescriptor(costume, model), assetRoot, warnings)
                : RequireModelAssetbundleName(model, warnings) is { } assetbundleName
                    ? ResolveFaceBundlePath(assetbundleName, assetRoot, warnings)
                    : null,
            _ => null,
        };
    }

    private static string? ResolveColorVariationBundlePath(
        Costume3dMaster costume,
        Costume3dModelMaster model,
        HeadOptionalDescriptor? headOptional,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot,
        List<string> warnings
    )
    {
        if (string.IsNullOrWhiteSpace(model.ColorAssetbundleName))
        {
            return null;
        }

        var path = costume.PartType switch
        {
            "body" => ResolveBodyColorVariationPath(model, costume.CharacterId, characterById, assetRoot),
            "hair" => ResolveFaceColorVariationPath(model, assetRoot),
            "head" => IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType)
                ? ResolveHeadOptionalColorVariationPath(headOptional ?? ResolveHeadOptionalDescriptor(costume, model), model.ColorAssetbundleName, assetRoot, warnings)
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

        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        var relativePath = Path.Combine(ToSystemPath(normalizedName), ResolveBodyBundleFileName(character));
        var path = ResolveExistingBundlePath(
            ResolveAssetBaseDirectoryCandidates(assetRoot, "body"),
            relativePath
        );
        if (path is null)
        {
            warnings.Add($"body bundle not found: {normalizedName}/{ResolveBodyBundleFileName(character)}");
        }
        return path;
    }

    private static string? ResolveFaceBundlePath(
        string assetbundleName,
        string assetRoot,
        List<string> warnings
    )
    {
        var normalizedName = assetbundleName.Replace('\\', '/').Trim('/');
        var relativePath = $"{ToSystemPath(normalizedName)}.bundle";
        var path = ResolveExistingBundlePath(
            ResolveAssetBaseDirectoryCandidates(assetRoot, "face"),
            relativePath
        );
        if (path is not null)
        {
            return path;
        }

        path = ResolveDefaultFaceBundleFallbackPath(assetRoot, normalizedName);
        if (path is not null)
        {
            return path;
        }

        warnings.Add($"face bundle not found: {normalizedName}.bundle");
        return null;
    }

    private static string? ResolveDefaultFaceBundleFallbackPath(
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
        return ResolveExistingBundlePath(
            ResolveAssetBaseDirectoryCandidates(assetRoot, "face"),
            $"{ToSystemPath(fallbackName)}.bundle"
        );
    }

    private static string? ResolveHeadOptionalBundlePath(
        HeadOptionalDescriptor descriptor,
        string assetRoot,
        List<string> warnings
    )
    {
        if (descriptor.IsEmptySlot)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(descriptor.AccessoryId) || string.IsNullOrWhiteSpace(descriptor.AttachNode))
        {
            warnings.Add("head_only row has no accessory id or attach node");
            return null;
        }

        var path = ResolveExistingBundlePath(
            ResolveAssetBaseDirectoryCandidates(assetRoot, "head_optional"),
            Path.Combine(descriptor.AccessoryId, $"{descriptor.AttachNode}.bundle")
        );
        if (path is null)
        {
            warnings.Add($"head_optional bundle not found: {descriptor.AccessoryId}/{descriptor.AttachNode}.bundle");
        }
        return path;
    }

    private static string? ResolveBodyColorVariationPath(
        Costume3dModelMaster model,
        int characterId,
        IReadOnlyDictionary<int, GameCharacterMaster> characterById,
        string assetRoot
    )
    {
        if (string.IsNullOrWhiteSpace(model.AssetbundleName))
        {
            return null;
        }
        if (!characterById.TryGetValue(characterId, out var character))
        {
            return null;
        }

        var normalizedName = model.AssetbundleName!.Replace('\\', '/').Trim('/');
        var bodyType = Path.GetFileNameWithoutExtension(ResolveBodyBundleFileName(character));
        return ResolveExistingBundlePath(
            ResolveColorVariationBaseDirectoryCandidates(assetRoot, "body"),
            Path.Combine(
                ToSystemPath(normalizedName),
                bodyType,
                $"{model.ColorAssetbundleName}.bundle"
            )
        );
    }

    private static string? ResolveFaceColorVariationPath(Costume3dModelMaster model, string assetRoot)
    {
        if (string.IsNullOrWhiteSpace(model.AssetbundleName))
        {
            return null;
        }

        var normalizedName = model.AssetbundleName!.Replace('\\', '/').Trim('/');
        return ResolveExistingBundlePath(
            ResolveColorVariationBaseDirectoryCandidates(assetRoot, "face"),
            Path.Combine(ToSystemPath(normalizedName), $"{model.ColorAssetbundleName}.bundle")
        );
    }

    private static string? ResolveHeadOptionalColorVariationPath(
        HeadOptionalDescriptor descriptor,
        string? colorAssetbundleName,
        string assetRoot,
        List<string> warnings
    )
    {
        if (descriptor.IsEmptySlot)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(descriptor.AccessoryId) || string.IsNullOrWhiteSpace(descriptor.AttachNode))
        {
            return null;
        }

        var path = ResolveExistingBundlePath(
            ResolveColorVariationBaseDirectoryCandidates(assetRoot, "head_optional"),
            Path.Combine(descriptor.AccessoryId, descriptor.AttachNode, $"{colorAssetbundleName}.bundle")
        );
        if (path is null)
        {
            warnings.Add($"head_optional color variation bundle not found: {descriptor.AccessoryId}/{descriptor.AttachNode}/{colorAssetbundleName}.bundle");
        }
        return path;
    }

    private static HeadOptionalDescriptor ResolveHeadOptionalDescriptor(Costume3dMaster? costume, Costume3dModelMaster model)
    {
        var normalizedName = FirstNonEmpty(
            model.AssetbundleName,
            costume?.AssetbundleName,
            model.ThumbnailAssetbundleName
        )?.Replace('\\', '/').Trim('/') ?? string.Empty;
        var parts = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return new HeadOptionalDescriptor(null, null, null, false);
        }

        var attachNode = !string.IsNullOrWhiteSpace(model.Part)
            ? model.Part
            : parts.Length > 1 ? parts[1] : null;
        var isEmptySlot = string.IsNullOrWhiteSpace(attachNode) &&
            Path.GetFileName(normalizedName).StartsWith("head_default_", StringComparison.OrdinalIgnoreCase);
        return new HeadOptionalDescriptor(normalizedName, parts[0], attachNode, isEmptySlot);
    }

    private static string? ResolveAttachNode(Costume3dModelMaster? model)
    {
        if (model is null || !IsAccessoryHeadCostume(model.HeadCostume3dAssetbundleType))
        {
            return null;
        }
        return ResolveHeadOptionalDescriptor(null, model).AttachNode;
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
        yield return Path.Combine(assetRoot, "live_pv", "model", "characterv2", "color_variation", part);
        yield return Path.Combine(assetRoot, "live_pv", "model", "character", "color_variation", part);
        yield return Path.Combine(assetRoot, "color_variation", part);
    }

    private static string? ResolveExistingBundlePath(IEnumerable<string> baseDirectories, string relativePath)
    {
        foreach (var baseDirectory in baseDirectories)
        {
            var candidate = Path.Combine(baseDirectory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            var gzipCandidate = candidate + ".gz";
            if (File.Exists(gzipCandidate))
            {
                return gzipCandidate;
            }
        }

        return null;
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

    private static bool IsCompleteHeadCostume(string? type)
    {
        return string.Equals(type, "head_and_hair", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "head_all", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "head_front", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "head_back", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RequireModelAssetbundleName(Costume3dModelMaster model, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(model.AssetbundleName))
        {
            return model.AssetbundleName!;
        }

        warnings.Add("missing model assetbundleName");
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
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

    private static void WriteJson<T>(string path, T value, string runtimeJsonOutput)
    {
        RuntimeJsonWriter.Write(path, value, WriteJsonOptions, runtimeJsonOutput);
    }

    private static void PrintSummary(CostumeRegistryExport export, string outputDirectory)
    {
        var missingParts = export.PartRegistry.Entries.Count(entry => entry.Status == "missing");
        var emptyParts = export.PartRegistry.Entries.Count(entry => entry.Status == "empty");
        var patternWarnings = export.HeadHairCompatibility.Rules.Count(entry => entry.Warnings.Count > 0);
        Console.WriteLine($"Wrote costume registries to {outputDirectory}");
        Console.WriteLine($"  character3d presets: {export.Character3dIndex.Entries.Count}");
        Console.WriteLine($"  part entries: {export.PartRegistry.Entries.Count} ({missingParts} missing metadata, {emptyParts} empty slots)");
        Console.WriteLine($"  part source packages: {export.PartSourceMap.Entries.Count}");
        Console.WriteLine($"  head/hair rules: {export.HeadHairCompatibility.Rules.Count} ({patternWarnings} with warnings)");
        Console.WriteLine($"  card unlock entries: {export.CardCostumeUnlocks.Entries.Count}");
    }
}

internal sealed record PartSourceIdentity(
    string BaseSourceKey,
    string SourceKey,
    string PackagePath
);

internal sealed record HeadOptionalDescriptor(
    string? AssetbundleName,
    string? AccessoryId,
    string? AttachNode,
    bool IsEmptySlot
);
