using System.Reflection;
using AssetStudio;
using Object = AssetStudio.Object;

namespace PjskBundle2Parts.Services;

public static class AssetStudioObjectFilter
{
    public static IReadOnlyList<Object> SelectPrimaryObjects(
        IReadOnlyList<Object> objects,
        string primaryFileName
    )
    {
        if (string.IsNullOrWhiteSpace(primaryFileName))
        {
            return objects;
        }

        var selected = objects
            .Where(obj => IsFromPrimaryFile(obj, primaryFileName))
            .ToList();
        return selected.Count > 0 ? selected : objects;
    }

    private static bool IsFromPrimaryFile(Object obj, string primaryFileName)
    {
        var assetsFile = ResolveMember(obj, "AssetsFile") ?? ResolveMember(obj, "assetsFile");
        if (assetsFile is null)
        {
            return false;
        }

        foreach (var memberName in new[] { "originalPath", "OriginalPath", "fileName", "FileName", "Path" })
        {
            if (ResolveMember(assetsFile, memberName) is string value &&
                string.Equals(Path.GetFileName(value), primaryFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static object? ResolveMember(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = source.GetType();
        return type.GetProperty(name, flags)?.GetValue(source)
            ?? type.GetField(name, flags)?.GetValue(source);
    }
}
