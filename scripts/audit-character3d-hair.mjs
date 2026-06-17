import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";

const args = parseArgs(process.argv.slice(2));
const masterDir = args.master ?? "/mnt/d/github/haruki-sekai-master/master";
const assetRoot = args.assetRoot ?? "/mnt/z/pjskdata/AssetBundles";
const character3dId = args.character3dId ? Number(args.character3dId) : null;
const onlyNonDefault = args.onlyNonDefault !== "false";

const character3ds = readJson(path.join(masterDir, "character3ds.json"));
const costume3dModels = readJson(path.join(masterDir, "costume3dModels.json"));
const costumeById = new Map(costume3dModels.map((entry) => [entry.costume3dId, entry]));
const faceRoots = [
  path.join(assetRoot, "live_pv", "model", "characterv2", "face"),
  path.join(assetRoot, "live_pv", "model", "character", "face"),
  path.join(assetRoot, "face"),
];
const headOptionalRoots = [
  path.join(assetRoot, "live_pv", "model", "characterv2", "head_optional"),
  path.join(assetRoot, "live_pv", "model", "character", "head_optional"),
  path.join(assetRoot, "head_optional"),
];

const rows = [];
for (const character3d of character3ds) {
  if (character3dId !== null && character3d.id !== character3dId) {
    continue;
  }

  const hair = costumeById.get(character3d.hairCostume3dId);
  const head = costumeById.get(character3d.headCostume3dId);
  if (!hair) {
    rows.push({
      character3dId: character3d.id,
      characterId: character3d.characterId,
      name: character3d.name,
      error: `missing hair costume3dId ${character3d.hairCostume3dId}`,
    });
    continue;
  }

  const hairName = hair.assetbundleName ?? "";
  const hairKind = classifyFaceBundleName(hairName);
  if (onlyNonDefault && hairKind === "default_hair_0000") {
    continue;
  }

  const headName = head?.assetbundleName ?? "";
  const hairGroupKey = resolveVariantGroupKey(hairName);
  const headGroupKey = resolveVariantGroupKey(headName);
  const hasHeadOptional = (head?.headCostume3dAssetbundleType ?? "") === "head_only"
    && headName.length > 0;
  const usesCompleteHead = isCompleteHeadType(head?.headCostume3dAssetbundleType)
    && headName.length > 0;
  const headKind = classifyHeadBundle(head);
  const groupBundles = hairGroupKey
    ? listVariantGroupBundles(hairGroupKey)
    : [];

  rows.push({
    character3dId: character3d.id,
    characterId: character3d.characterId,
    name: character3d.name,
    bodyCostume3dId: character3d.bodyCostume3dId,
    hairCostume3dId: character3d.hairCostume3dId,
    hairAssetbundleName: hairName,
    hairBundleKind: hairKind,
    hairVariantGroupKey: hairGroupKey,
    hairBundleExists: faceBundleExists(hairName),
    headCostume3dId: character3d.headCostume3dId,
    headAssetbundleName: headName,
    headAssetbundleType: head?.headCostume3dAssetbundleType ?? null,
    headPart: head?.part ?? null,
    headBundleKind: headKind,
    headVariantGroupKey: headGroupKey,
    headBundleExists: headName.length > 0
      ? hasHeadOptional ? headOptionalBundleExists(headName, head?.part) : faceBundleExists(headName)
      : null,
    headCompositionKind: resolveCompositionKind(
      hairKind,
      headKind,
      hairGroupKey,
      headGroupKey,
      usesCompleteHead,
      hasHeadOptional
    ),
    sameVariantGroup: hairGroupKey.length > 0 && hairGroupKey === headGroupKey,
    availableFaceBundlesInHairGroup: groupBundles,
  });
}

const summary = rows.reduce((acc, row) => {
  const key = row.headCompositionKind ?? row.error ?? "unknown";
  acc[key] = (acc[key] ?? 0) + 1;
  return acc;
}, {});

if (args.json === "true") {
  console.log(JSON.stringify({ summary, rows }, null, 2));
} else {
  console.log(`rows=${rows.length}`);
  console.log(JSON.stringify(summary, null, 2));
  for (const row of rows) {
    if (row.error) {
      console.log(`${row.character3dId}\t${row.characterId}\tERROR\t${row.error}`);
      continue;
    }
    console.log([
      row.character3dId,
      String(row.characterId).padStart(2, "0"),
      row.hairAssetbundleName,
      row.hairBundleKind,
      `group=${row.hairVariantGroupKey}`,
      `head=${row.headAssetbundleName || "<none>"}`,
      row.headCompositionKind,
      `bundles=${row.availableFaceBundlesInHairGroup.join(",") || "<none>"}`,
      row.name,
    ].join("\t"));
  }
}

function parseArgs(rawArgs) {
  const result = {};
  for (let index = 0; index < rawArgs.length; index += 1) {
    const current = rawArgs[index];
    if (!current.startsWith("--")) {
      continue;
    }
    const [key, inlineValue] = current.slice(2).split("=", 2);
    if (inlineValue !== undefined) {
      result[toCamelCase(key)] = inlineValue;
      continue;
    }
    const next = rawArgs[index + 1];
    if (next && !next.startsWith("--")) {
      result[toCamelCase(key)] = next;
      index += 1;
    } else {
      result[toCamelCase(key)] = "true";
    }
  }
  return result;
}

function toCamelCase(value) {
  return value.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
}

function readJson(file) {
  return JSON.parse(readFileSync(file, "utf8"));
}

function classifyFaceBundleName(assetbundleName) {
  if (!assetbundleName) {
    return "none";
  }
  const leaf = path.posix.basename(assetbundleName.replaceAll("\\", "/"));
  if (/^0+$/.test(leaf)) {
    return "default_hair_0000";
  }
  if (/n$/i.test(leaf)) {
    return "alternate_hair_no_accessory";
  }
  if (/[a-z]$/i.test(leaf)) {
    return "alternate_hair_accessory_variant";
  }
  return "complete_hair_or_head";
}

function classifyHeadBundle(head) {
  if (!head) {
    return "none";
  }
  if ((head.headCostume3dAssetbundleType ?? "") === "head_only") {
    return head.assetbundleName ? "head_optional_accessory" : "empty_head_optional_slot";
  }
  return classifyFaceBundleName(head.assetbundleName ?? "");
}

function resolveVariantGroupKey(assetbundleName) {
  if (!assetbundleName) {
    return "";
  }
  const normalizedName = assetbundleName.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
  const directory = path.posix.dirname(normalizedName);
  const leaf = path.posix.basename(normalizedName);
  const groupLeaf = /[a-z]$/i.test(leaf) ? leaf.slice(0, -1) : leaf;
  return directory === "." ? groupLeaf : `${directory}/${groupLeaf}`;
}

function isCompleteHeadType(type) {
  return ["head_and_hair", "head_all", "head_front", "head_back"].includes(type ?? "");
}

function resolveCompositionKind(
  hairKind,
  headKind,
  hairGroupKey,
  headGroupKey,
  usesCompleteHead,
  hasHeadOptional
) {
  if (hasHeadOptional) {
    return hairKind === "alternate_hair_no_accessory"
      ? "alternate_hair_with_head_optional_accessory"
      : "base_hair_with_head_optional_accessory";
  }
  if (usesCompleteHead) {
    if (
      hairKind === "alternate_hair_no_accessory"
      && hairGroupKey
      && hairGroupKey === headGroupKey
    ) {
      return headKind === "complete_hair_or_head"
        ? "alternate_hair_with_complete_head_overlay"
        : headKind === "alternate_hair_accessory_variant"
        ? "alternate_hair_with_lettered_accessory"
        : "alternate_hair_with_complete_head_accessory";
    }
    return "complete_head";
  }
  return hairKind === "alternate_hair_no_accessory"
    ? "alternate_hair_no_accessory"
    : "base_hair";
}

function faceBundleExists(assetbundleName) {
  if (!assetbundleName) {
    return false;
  }
  return faceRoots.some((root) => existsSync(path.join(root, `${assetbundleName}.bundle`)));
}

function headOptionalBundleExists(assetbundleName, part) {
  if (!assetbundleName) {
    return false;
  }
  const normalizedName = assetbundleName.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
  const pieces = normalizedName.split("/");
  const accessoryId = pieces[0];
  const attachNode = part || pieces[1];
  if (!accessoryId || !attachNode) {
    return false;
  }
  return headOptionalRoots.some((root) => (
    existsSync(path.join(root, accessoryId, `${attachNode}.bundle`))
  ));
}

function listVariantGroupBundles(groupKey) {
  const directoryName = path.posix.dirname(groupKey);
  const baseLeaf = path.posix.basename(groupKey);
  const result = [];
  for (const root of faceRoots) {
    const directory = path.join(root, directoryName);
    if (!existsSync(directory)) {
      continue;
    }
    for (const file of readdirSync(directory)) {
      if (!file.endsWith(".bundle")) {
        continue;
      }
      const stem = file.slice(0, -".bundle".length);
      if (stem === baseLeaf || stem.startsWith(baseLeaf) && /^[a-z]$/i.test(stem.slice(baseLeaf.length))) {
        result.push(`${directoryName}/${stem}`);
      }
    }
  }
  return [...new Set(result)].sort((a, b) => a.localeCompare(b));
}
