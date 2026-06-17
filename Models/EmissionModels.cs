namespace PjskBundle2Parts.Models;

public sealed record TextureEmission(
    string TextureName,
    string RelativePath
);

public sealed record GlbEmissionResult(
    string RelativeGlbPath,
    IReadOnlyDictionary<string, string> TexturePathByName
);
