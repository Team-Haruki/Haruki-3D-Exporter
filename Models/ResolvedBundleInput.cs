namespace PjskBundle2Parts.Models;

public enum BundlePartKind
{
    Body,
    Head,
}

public sealed record ResolvedBundleInput(
    BundlePartKind PartKind,
    string OriginalInputPath,
    string ResolvedBundlePath,
    string CharacterId,
    string BundleStem
);
