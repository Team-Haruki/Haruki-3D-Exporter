using System.Text.Json;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public static class CharacterHeightResolver
{
    public static IReadOnlyDictionary<string, float> LoadMetersByCharacterId(string masterDirectory)
    {
        var gameCharactersPath = Path.Combine(
            Path.GetFullPath(masterDirectory),
            "gameCharacters.json"
        );
        if (!File.Exists(gameCharactersPath))
        {
            throw new FileNotFoundException("gameCharacters.json was not found.", gameCharactersPath);
        }

        using var stream = File.OpenRead(gameCharactersPath);
        var characters = JsonSerializer.Deserialize<IReadOnlyList<GameCharacterMaster>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) ?? Array.Empty<GameCharacterMaster>();

        return characters.ToDictionary(
            character => character.Id.ToString("00"),
            character => ToMeters(character.Height)
        );
    }

    public static float ResolveMeters(
        IReadOnlyDictionary<string, float>? characterHeightMetersById,
        int characterId,
        float fallbackMeters = 1.00f
    )
    {
        return characterHeightMetersById is not null &&
            characterHeightMetersById.TryGetValue(characterId.ToString("00"), out var height)
            ? height
            : fallbackMeters;
    }

    public static float ResolveRequiredMeters(
        IReadOnlyDictionary<string, float> characterHeightMetersById,
        int characterId
    )
    {
        var key = characterId.ToString("00");
        if (!characterHeightMetersById.TryGetValue(key, out var height) || height <= 0)
        {
            throw new InvalidDataException(
                $"gameCharacters.json is missing a valid height for character {characterId}."
            );
        }
        return height;
    }

    private static float ToMeters(float height)
    {
        return height > 10f ? height / 100f : height;
    }
}
