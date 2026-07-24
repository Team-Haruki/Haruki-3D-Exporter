using System.Text.Json;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public static class RuntimeRoleCatalogExporter
{
    private const int CatalogVersion = 4;

    private static readonly string[] MikuUnits =
    [
        "piapro",
        "idol",
        "light_sound",
        "street",
        "theme_park",
        "school_refusal",
    ];

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static RuntimeRoleCatalog WriteFromMaster(string masterDirectory, string outputDirectory)
    {
        var normalizedMasterDirectory = Path.GetFullPath(masterDirectory);
        var path = Path.Combine(normalizedMasterDirectory, "character3ds.json");
        var entries = ReadCharacter3ds(path);
        var characterUnits = ReadCharacterUnits(
            Path.Combine(normalizedMasterDirectory, "gameCharacterUnits.json")
        );
        var characterHeightMetersById =
            CharacterHeightResolver.LoadMetersByCharacterId(normalizedMasterDirectory);
        var catalog = Build(
            entries,
            characterUnits,
            characterHeightMetersById,
            ResolveMasterVersion(normalizedMasterDirectory)
        );
        Write(outputDirectory, catalog);
        return catalog;
    }

    private static IReadOnlyList<Character3dMaster> ReadCharacter3ds(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<IReadOnlyList<Character3dMaster>>(stream, ReadJsonOptions)
            ?? throw new InvalidDataException($"Failed to parse master file: {path}");
    }

    private static IReadOnlyList<GameCharacterUnitMaster> ReadCharacterUnits(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<IReadOnlyList<GameCharacterUnitMaster>>(stream, ReadJsonOptions)
            ?? throw new InvalidDataException($"Failed to parse master file: {path}");
    }

    public static void Write(string outputDirectory, RuntimeRoleCatalog catalog)
    {
        Directory.CreateDirectory(outputDirectory);
        DeleteLegacyPresetIndexes(outputDirectory);
        if (CatalogIsCurrent(outputDirectory, catalog))
        {
            return;
        }
        RuntimeJsonWriter.Write(Path.Combine(outputDirectory, "runtime-role-catalog.json"), catalog, WriteJsonOptions);
        foreach (var role in catalog.Roles)
        {
            RuntimeJsonWriter.Write(
                Path.Combine(
                    outputDirectory,
                    "parts",
                    "by-role",
                    role.CharacterId.ToString(),
                    role.Unit ?? "default",
                    "runtime-role-catalog.json"
                ),
                new RuntimeRoleCatalog(CatalogVersion, catalog.MasterVersion, [role]),
                WriteJsonOptions
            );
        }
    }

    private static bool CatalogIsCurrent(string outputDirectory, RuntimeRoleCatalog catalog)
    {
        var rootPath = Path.Combine(outputDirectory, "runtime-role-catalog.json");
        if (!CatalogMatches(rootPath, catalog.MasterVersion, catalog.Roles))
        {
            return false;
        }
        return catalog.Roles.All(role => CatalogMatches(
            Path.Combine(
                outputDirectory,
                "parts",
                "by-role",
                role.CharacterId.ToString(),
                role.Unit ?? "default",
                "runtime-role-catalog.json"
            ),
            catalog.MasterVersion,
            [role]
        ));
    }

    private static bool CatalogMatches(
        string path,
        string masterVersion,
        IReadOnlyList<RuntimeRoleCatalogEntry> roles
    )
    {
        if (!RuntimeJsonWriter.OutputsExist(path))
        {
            return false;
        }
        try
        {
            using var document = RuntimeJsonWriter.ReadJsonDocument(path);
            var existing = JsonSerializer.Deserialize<RuntimeRoleCatalog>(document.RootElement.GetRawText(), ReadJsonOptions);
            return existing is not null &&
                existing.Version == CatalogVersion &&
                existing.MasterVersion == masterVersion &&
                existing.Roles.SequenceEqual(roles);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static void DeleteLegacyPresetIndexes(string outputDirectory)
    {
        File.Delete(Path.Combine(outputDirectory, "character3d-index.json"));
        File.Delete(Path.Combine(outputDirectory, "character3d-index.msgpack.br"));

        var roleRoot = Path.Combine(outputDirectory, "parts", "by-role");
        if (!Directory.Exists(roleRoot))
        {
            return;
        }
        foreach (var path in Directory.EnumerateFiles(roleRoot, "character3d-index.*", SearchOption.AllDirectories))
        {
            File.Delete(path);
        }
    }

    private static RuntimeRoleCatalog Build(
        IReadOnlyList<Character3dMaster> entries,
        IReadOnlyList<GameCharacterUnitMaster> characterUnits,
        IReadOnlyDictionary<string, float> characterHeightMetersById,
        string masterVersion
    )
    {
        var roles = entries
            .Where(entry => entry.Id is >= 1 and <= 31)
            .OrderBy(entry => entry.Id)
            .Select(entry =>
            {
                var characterUnit = characterUnits.SingleOrDefault(candidate =>
                    candidate.GameCharacterId == entry.CharacterId &&
                    string.Equals(candidate.Unit, entry.Unit, StringComparison.Ordinal)
                ) ?? throw new InvalidDataException(
                    $"gameCharacterUnits is missing role {entry.CharacterId}/{entry.Unit ?? "default"}."
                );
                return new RuntimeRoleCatalogEntry(
                    RoleId: entry.Id,
                    CharacterId: entry.CharacterId,
                    CharacterHeightMeters: CharacterHeightResolver.ResolveRequiredMeters(
                        characterHeightMetersById,
                        entry.CharacterId
                    ),
                    Unit: entry.Unit,
                    BodyCostume3dId: entry.BodyCostume3dId,
                    HeadCostume3dId: entry.HeadCostume3dId,
                    HairCostume3dId: entry.HairCostume3dId,
                    SkinColors: new RuntimeSkinColors(
                        Default: characterUnit.SkinColorCode,
                        Shadow1: characterUnit.SkinShadowColorCode1,
                        Shadow2: characterUnit.SkinShadowColorCode2
                    ),
                    RoleRuntimePath: $"roles/{entry.CharacterId}/{entry.Unit ?? "default"}/role-runtime.msgpack.br"
                );
            })
            .ToList();
        return Build(roles, masterVersion);
    }

    private static RuntimeRoleCatalog Build(IReadOnlyList<RuntimeRoleCatalogEntry> roles, string masterVersion)
    {
        if (string.IsNullOrWhiteSpace(masterVersion))
        {
            throw new InvalidDataException("Runtime role catalog requires a master version.");
        }
        if (roles.Count != 31 || !roles.Select(entry => entry.RoleId).SequenceEqual(Enumerable.Range(1, 31)))
        {
            throw new InvalidDataException("Runtime role catalog requires exactly the public role IDs 1 through 31.");
        }
        foreach (var entry in roles)
        {
            var expected = ExpectedRole(entry.RoleId);
            if (entry.CharacterId != expected.CharacterId ||
                !string.Equals(entry.Unit, expected.Unit, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Runtime role {entry.RoleId} is {entry.CharacterId}/{entry.Unit ?? "default"}, " +
                    $"expected {expected.CharacterId}/{expected.Unit}."
                );
            }
            if (entry.BodyCostume3dId <= 0 || entry.HeadCostume3dId <= 0 ||
                entry.HairCostume3dId <= 0 || string.IsNullOrWhiteSpace(entry.RoleRuntimePath) ||
                entry.CharacterHeightMeters <= 0 ||
                !IsColor(entry.SkinColors.Default) ||
                !IsColor(entry.SkinColors.Shadow1) ||
                !IsColor(entry.SkinColors.Shadow2))
            {
                throw new InvalidDataException($"Runtime role {entry.RoleId} has incomplete defaults.");
            }
        }

        return new RuntimeRoleCatalog(CatalogVersion, masterVersion, roles);
    }

    private static bool IsColor(string value) =>
        value.Length == 7 &&
        value[0] == '#' &&
        value.Skip(1).All(Uri.IsHexDigit);

    public static string ResolveMasterVersion(string masterDirectory)
    {
        foreach (var path in new[]
        {
            Path.Combine(masterDirectory, "current_version.json"),
            Path.Combine(Directory.GetParent(masterDirectory)?.FullName ?? masterDirectory, "versions", "current_version.json"),
        })
        {
            if (!File.Exists(path))
            {
                continue;
            }
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.TryGetProperty("dataVersion", out var dataVersion) &&
                dataVersion.GetString() is { Length: > 0 } value)
            {
                return value;
            }
        }

        throw new FileNotFoundException(
            $"Master version file with a non-empty dataVersion was not found for {masterDirectory}."
        );
    }

    private static (int CharacterId, string Unit) ExpectedRole(int roleId)
    {
        if (roleId <= 20)
        {
            return (roleId, UnitForCharacter(roleId));
        }
        if (roleId <= 26)
        {
            return (21, MikuUnits[roleId - 21]);
        }
        return (roleId - 5, "piapro");
    }

    private static string UnitForCharacter(int characterId) => characterId switch
    {
        <= 4 => "light_sound",
        <= 8 => "idol",
        <= 12 => "street",
        <= 16 => "theme_park",
        <= 20 => "school_refusal",
        _ => throw new ArgumentOutOfRangeException(nameof(characterId)),
    };
}
