using AssetStudio;

namespace PjskBundle2Parts.Models;

public sealed record ParsedBundleData(
    ResolvedBundleInput Input,
    BundleInventory Inventory,
    IImported ImportedModel
);
