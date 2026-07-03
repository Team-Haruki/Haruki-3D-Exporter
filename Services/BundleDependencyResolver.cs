using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public enum BundleLoadDependencyMode
{
    Default,
    FullDirectory,
}

public static class BundleDependencyResolver
{
    public static IReadOnlyList<string> ResolveLoadBundlePaths(
        ResolvedBundleInput input,
        BundleLoadDependencyMode mode = BundleLoadDependencyMode.Default
    )
    {
        var primaryPath = input.ResolvedBundlePath;
        var directory = Path.GetDirectoryName(primaryPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return new[] { primaryPath };
        }

        if (mode == BundleLoadDependencyMode.FullDirectory || input.PartKind == BundlePartKind.Body)
        {
            return BuildOrderedBundleList(primaryPath, Directory.EnumerateFiles(directory, "*.bundle", SearchOption.TopDirectoryOnly));
        }

        var familyStem = ResolveFamilyStem(Path.GetFileNameWithoutExtension(primaryPath));
        var familyBundles = Directory
            .EnumerateFiles(directory, "*.bundle", SearchOption.TopDirectoryOnly)
            .Where(path => IsSameHeadFamily(Path.GetFileNameWithoutExtension(path), familyStem));
        return BuildOrderedBundleList(primaryPath, familyBundles);
    }

    private static IReadOnlyList<string> BuildOrderedBundleList(string primaryPath, IEnumerable<string> bundlePaths)
    {
        var normalizedPrimary = Path.GetFullPath(primaryPath);
        return bundlePaths
            .Prepend(normalizedPrimary)
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => string.Equals(path, normalizedPrimary, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveFamilyStem(string bundleStem)
    {
        var digitCount = 0;
        while (digitCount < bundleStem.Length && char.IsDigit(bundleStem[digitCount]))
        {
            digitCount++;
        }

        if (digitCount > 0)
        {
            return bundleStem[..digitCount];
        }

        var underscore = bundleStem.IndexOf('_');
        return underscore > 0 ? bundleStem[..underscore] : bundleStem;
    }

    private static bool IsSameHeadFamily(string bundleStem, string familyStem)
    {
        if (string.Equals(bundleStem, familyStem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!bundleStem.StartsWith(familyStem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = bundleStem[familyStem.Length..];
        return suffix.StartsWith('_') || suffix.All(char.IsLetter);
    }
}
