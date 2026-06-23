using System.Text.Json;
using PjskBundle2Parts.Services;

var tempDir = Path.Combine(Path.GetTempPath(), $"haruki-exporter-config-test-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);
var configPath = Path.Combine(tempDir, "exporter.config.json");
File.WriteAllText(configPath, JsonSerializer.Serialize(new
{
    master = "/data/master",
    assetRoot = "/data/assets",
    output = "/data/out-from-config",
    character3dId = 5,
    emitCostumeRegistries = true,
    emitPartPackages = true,
    partCostume3dId = 2,
    partType = "Body",
    partUnit = "light_sound",
    manifest = "/data/manifest-from-config.json",
    keepIntermediate = true
}));

var parsed = ConversionOptionsParser.Parse(new[]
{
    "--config", configPath,
    "--out", "/data/out-from-cli",
    "--part-type", "head_optional",
    "--manifest", "/data/manifest-from-cli.json"
});

if (!parsed.IsSuccess || parsed.Options is null)
{
    throw new Exception(parsed.ErrorMessage);
}

var options = parsed.Options;
Expect(options.MasterDirectory == "/data/master", "master comes from config");
Expect(options.AssetRoot == "/data/assets", "asset root comes from config");
Expect(options.OutputDirectory == "/data/out-from-cli", "CLI output overrides config");
Expect(options.Character3dId == 5, "character3d id comes from config");
Expect(options.EmitCostumeRegistries, "emit registries comes from config");
Expect(options.EmitPartPackages, "emit part packages comes from config");
Expect(options.PartCostume3dId == 2, "part costume id comes from config");
Expect(options.PartType == "head_optional", "CLI part type overrides and normalizes config");
Expect(options.PartUnit == "light_sound", "part unit comes from config");
Expect(options.ManifestPath == "/data/manifest-from-cli.json", "CLI manifest overrides config");
Expect(options.KeepIntermediate, "keep intermediate comes from config");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}
