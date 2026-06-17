using System.Text.RegularExpressions;
using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class BundleInputResolver
{
    private static readonly Regex CharacterIdRegex =
        new(@"(?<=/)(\d{2})(?=/)", RegexOptions.Compiled);

    public ResolvedBundleInput ResolveBody(string inputPath)
    {
        var normalized = Normalize(inputPath);
        if (File.Exists(normalized))
        {
            return BuildResolved(BundlePartKind.Body, inputPath, normalized);
        }

        if (!Directory.Exists(normalized))
        {
            throw new FileNotFoundException($"Body input not found: {inputPath}");
        }

        var candidates = Directory
            .GetFiles(normalized, "*.bundle", SearchOption.TopDirectoryOnly)
            .OrderBy(path => ScoreBodyCandidate(Path.GetFileName(path)))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"No .bundle files found in body directory: {inputPath}"
            );
        }

        return BuildResolved(BundlePartKind.Body, inputPath, candidates[0]);
    }

    public ResolvedBundleInput ResolveHead(string inputPath)
    {
        var normalized = Normalize(inputPath);
        if (File.Exists(normalized))
        {
            return BuildResolved(BundlePartKind.Head, inputPath, normalized);
        }

        if (!Directory.Exists(normalized))
        {
            throw new FileNotFoundException($"Head input not found: {inputPath}");
        }

        var candidates = Directory
            .GetFiles(normalized, "*.bundle", SearchOption.TopDirectoryOnly)
            .OrderBy(path => ScoreHeadCandidate(Path.GetFileName(path)))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"No .bundle files found in head directory: {inputPath}"
            );
        }

        return BuildResolved(BundlePartKind.Head, inputPath, candidates[0]);
    }

    private static ResolvedBundleInput BuildResolved(
        BundlePartKind partKind,
        string originalInputPath,
        string resolvedBundlePath
    )
    {
        var normalizedResolved = Normalize(resolvedBundlePath);
        var characterId = InferCharacterId(normalizedResolved);
        var bundleStem = Path.GetFileNameWithoutExtension(normalizedResolved);
        return new ResolvedBundleInput(
            partKind,
            originalInputPath,
            normalizedResolved,
            characterId,
            bundleStem
        );
    }

    private static string InferCharacterId(string path)
    {
        var unixPath = path.Replace('\\', '/');
        var match = CharacterIdRegex.Match(unixPath);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    private static int ScoreBodyCandidate(string fileName)
    {
        return fileName.ToLowerInvariant() switch
        {
            "ladies_m.bundle" => 0,
            "ladies_s.bundle" => 1,
            _ => 100,
        };
    }

    private static int ScoreHeadCandidate(string fileName)
    {
        return fileName.ToLowerInvariant() switch
        {
            "0001.bundle" => 0,
            _ => 100,
        };
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
