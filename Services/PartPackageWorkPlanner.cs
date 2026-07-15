using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public static class PartPackageWorkPlanner
{
    public static PartPackageWorkList Load(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<PartPackageWorkList>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"Part package work list is empty: {path}");

    public static string SummaryPath(string workListPath) => workListPath + ".summary.json";

    public static IReadOnlyList<IReadOnlyList<PartRegistryEntry>> Plan(
        IReadOnlyList<PartRegistryEntry> entries,
        int workerCount
    )
    {
        if (workerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        }

        var representatives = entries
            .Where(entry => entry.BundlePath is not null && entry.Status != "missing")
            .Where(HasRequiredBundleFiles)
            .GroupBy(entry => entry.PackagePath, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(entry => entry.Costume3dId)
                .ThenBy(entry => entry.Unit ?? string.Empty, StringComparer.Ordinal)
                .First())
            .ToList();
        var groups = representatives
            .GroupBy(SourceKey, StringComparer.Ordinal)
            .Select(group => new WorkGroup(
                group.Key,
                group.OrderBy(SortKey, StringComparer.Ordinal).ToList(),
                EstimateBytes(group)
            ))
            .OrderByDescending(group => group.EstimatedBytes)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();
        var workers = Enumerable.Range(0, Math.Min(workerCount, Math.Max(1, groups.Count)))
            .Select(_ => new Worker())
            .ToList();

        foreach (var group in groups)
        {
            var worker = workers
                .Select((value, index) => (value, index))
                .OrderBy(pair => pair.value.EstimatedBytes)
                .ThenBy(pair => pair.index)
                .First().value;
            worker.Entries.AddRange(group.Entries);
            worker.EstimatedBytes += group.EstimatedBytes;
        }

        return workers
            .Select(worker => (IReadOnlyList<PartRegistryEntry>)worker.Entries
                .OrderBy(SortKey, StringComparer.Ordinal)
                .ToList())
            .ToList();
    }

    private static bool HasRequiredBundleFiles(PartRegistryEntry entry) =>
        entry.BundlePath is not null &&
        File.Exists(entry.BundlePath) &&
        (entry.ColorVariationBundlePath is null || File.Exists(entry.ColorVariationBundlePath));

    private static string SourceKey(PartRegistryEntry entry) =>
        entry.BaseSourceKey ?? entry.SourceKey ?? entry.PackagePath;

    private static string SortKey(PartRegistryEntry entry)
    {
        var bundlePath = entry.BundlePath ?? entry.PackagePath;
        var directory = Path.GetDirectoryName(bundlePath) ?? string.Empty;
        var family = string.Equals(entry.PartType, "body", StringComparison.OrdinalIgnoreCase)
            ? directory
            : Path.Combine(directory, BundleDependencyResolver.ResolveFamilyStem(Path.GetFileNameWithoutExtension(bundlePath)));
        return $"{entry.PartType}\0{family}\0{SourceKey(entry)}\0{entry.PackagePath}";
    }

    private static long EstimateBytes(IEnumerable<PartRegistryEntry> entries) => entries
        .SelectMany(entry => new[] { entry.BundlePath, entry.ColorVariationBundlePath })
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Cast<string>()
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.Ordinal)
        .Sum(path => new FileInfo(path).Length);

    private sealed record WorkGroup(
        string Key,
        IReadOnlyList<PartRegistryEntry> Entries,
        long EstimatedBytes
    );

    private sealed class Worker
    {
        public List<PartRegistryEntry> Entries { get; } = new();
        public long EstimatedBytes { get; set; }
    }
}

public sealed record PartPackageWorkList(
    IReadOnlyDictionary<string, float> CharacterHeightMetersById,
    IReadOnlyList<PartRegistryEntry> Entries
);
