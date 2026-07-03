using System.Text.Json;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class RoleRuntimeExporter
{
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly BundleInputResolver resolver = new();
    private readonly Character3dCostumeResolver character3dCostumeResolver = new();
    private readonly AssetStudioBundleParser parser = new();
    private readonly ConversionPlanner planner = new();
    private readonly SpringBoneExporter springBoneExporter = new();
    private readonly VrmSpringBoneCandidateBuilder vrmSpringBoneCandidateBuilder = new();
    private readonly AssetStudioImportedModelFactory modelFactory = new();
    private readonly MotionPackageExporter motionPackageExporter = new();
    private readonly PjskSekaiRuntimeExtensionBuilder runtimeExtensionBuilder = new();

    public IReadOnlyList<RoleRuntimeExportResult> ExportMany(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        IReadOnlyList<int> character3dIds,
        string? motionPath = null,
        string runtimeJsonOutput = RuntimeJsonWriter.Gzip
    )
    {
        var ids = character3dIds.Count == 0
            ? LoadAllCharacter3dIds(masterDirectory)
            : character3dIds;
        return ids
            .Distinct()
            .OrderBy(id => id)
            .Select(id => ExportOne(masterDirectory, assetRoot, outputDirectory, id, motionPath, runtimeJsonOutput))
            .ToList();
    }

    public RoleRuntimeExportResult ExportOne(
        string masterDirectory,
        string assetRoot,
        string outputDirectory,
        int character3dId,
        string? motionPath = null,
        string runtimeJsonOutput = RuntimeJsonWriter.Gzip
    )
    {
        var resolvedCostume = character3dCostumeResolver.Resolve(
            character3dId,
            masterDirectory,
            assetRoot
        );
        var roleDirectory = BuildRoleRuntimeDirectory(outputDirectory, resolvedCostume.CharacterId, resolvedCostume.Unit);
        var motionDirectory = Path.Combine(roleDirectory, "motion");
        Directory.CreateDirectory(roleDirectory);

        var bodyInput = resolver.ResolveBody(resolvedCostume.BodyPath)
            with { CharacterId = resolvedCostume.CharacterId.ToString("00") };
        var headInput = resolver.ResolveHead(resolvedCostume.MainHeadPath)
            with { CharacterId = resolvedCostume.CharacterId.ToString("00") };
        var bodyInventory = parser.Parse(bodyInput);
        var headInventory = parser.Parse(headInput);
        var plan = planner.CreatePlan(
            bodyInput,
            headInput,
            outputDirectory,
            bodyInventory,
            headInventory,
            headRootOverride: null
        );
        var bodySpringBone = springBoneExporter.Export(bodyInput);
        var headSpringBone = springBoneExporter.Export(headInput);
        var combinedSpringBone = new CombinedSpringBoneExport(
            Version: 1,
            Body: bodySpringBone,
            Head: headSpringBone
        );
        var vrmSpringBoneCandidate = vrmSpringBoneCandidateBuilder.Build(combinedSpringBone);
        var importedBody = modelFactory.CreateImportedModel(bodyInput);
        var resolvedMotionPath = motionPath ?? ResolveDefaultCostumeSettingMotionPath(assetRoot, resolvedCostume);
        var motionExport = motionPackageExporter.Export(
            resolvedMotionPath,
            motionDirectory,
            importedBody
        );
        var runtimeBuild = runtimeExtensionBuilder.Build(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            combinedSpringBone,
            vrmSpringBoneCandidate,
            motionExport,
            resolvedCostume
        );
        var warnings = new List<string>();
        if (resolvedMotionPath is null)
        {
            warnings.Add($"No costume_setting motion bundle was found for character3dId {character3dId}.");
        }
        warnings.AddRange(runtimeBuild.Report.Warnings);
        warnings.AddRange(vrmSpringBoneCandidate.Warnings);

        var runtime = new RoleRuntimePackage(
            Version: "0414-role-1",
            Role: new RoleRuntimeIdentity(
                CharacterId: resolvedCostume.CharacterId,
                Unit: resolvedCostume.Unit
            ),
            SourceCharacter3dId: resolvedCostume.Character3dId,
            SourceCharacterName: resolvedCostume.CharacterName,
            SourceCostumeSettingBundlePath: resolvedMotionPath,
            MotionPackage: RewriteMotionPackageForRoleDirectory(
                runtimeBuild.Extension.MotionPackage,
                roleDirectory,
                motionExport.UnityMotionJsonPath
            ),
            Warnings: warnings.Distinct().ToList()
        );
        var runtimePath = Path.Combine(roleDirectory, "role-runtime.json");
        RuntimeJsonWriter.Write(runtimePath, runtime, WriteJsonOptions, runtimeJsonOutput);
        return new RoleRuntimeExportResult(
            Character3dId: resolvedCostume.Character3dId,
            CharacterId: resolvedCostume.CharacterId,
            Unit: resolvedCostume.Unit,
            RuntimePath: RuntimeJsonWriter.PrimaryPath(runtimePath, runtimeJsonOutput),
            Warnings: runtime.Warnings
        );
    }

    private static PjskSekaiRuntimeMotionPackage? RewriteMotionPackageForRoleDirectory(
        PjskSekaiRuntimeMotionPackage? source,
        string roleDirectory,
        string? unityMotionJsonPath
    )
    {
        if (source is null)
        {
            return null;
        }

        return source with
        {
            UnityMotionJson = unityMotionJsonPath is null
                ? null
                : Path.GetRelativePath(
                    roleDirectory,
                    unityMotionJsonPath
                ).Replace('\\', '/'),
        };
    }

    private static string BuildRoleRuntimeDirectory(string outputDirectory, int characterId, string? unit)
    {
        return Path.Combine(outputDirectory, "roles", characterId.ToString(), unit ?? "default");
    }

    private static string? ResolveDefaultCostumeSettingMotionPath(
        string assetRoot,
        ResolvedCharacter3dCostume costume
    )
    {
        var root = Path.GetFullPath(assetRoot);
        var fileName = $"{ResolveCostumeSettingMotionCharacterId(costume):00}_00.bundle";
        var candidates = new[]
        {
            Path.Combine(root, "character", "motion", "costume_setting", fileName),
            Path.Combine(root, "motion", "costume_setting", fileName),
            Path.Combine(root, "costume_setting", fileName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int ResolveCostumeSettingMotionCharacterId(ResolvedCharacter3dCostume costume)
    {
        if (costume.CharacterId != 21)
        {
            return costume.CharacterId;
        }

        return (costume.Unit ?? string.Empty).ToLowerInvariant() switch
        {
            "light_sound" => 27,
            "idol" => 28,
            "street" => 29,
            "theme_park" => 30,
            "school_refusal" => 31,
            _ => 21,
        };
    }

    private static IReadOnlyList<int> LoadAllCharacter3dIds(string masterDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(masterDirectory), "character3ds.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Master file was not found: {path}");
        }

        using var stream = File.OpenRead(path);
        var entries = JsonSerializer.Deserialize<IReadOnlyList<Character3dMaster>>(stream, ReadJsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse master file: {path}");
        return entries.Select(entry => entry.Id).ToList();
    }
}
