import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

let masterDir;
let sampleLimit;
let costume3ds;
let costume3dModels;
let cards;
let cardCostume3ds;
let availablePatterns;
let notAvailablePatterns;
let defaultHairs;
let costumeById;
let cardById;
let modelsByCostumeId;
let cardCostumesByCardId;
let costumesByGroupId;
let errors;
let warnings;
let notes;
let samples;

export function auditCostumeMasterdata(options = {}) {
  masterDir = options.masterDir ?? "/mnt/d/github/testfile/master";
  sampleLimit = Number(options.sampleLimit ?? 12);

  costume3ds = readMaster("costume3ds.json");
  costume3dModels = readMaster("costume3dModels.json");
  cards = readMaster("cards.json");
  cardCostume3ds = readMaster("cardCostume3ds.json");
  availablePatterns = readMaster("costume3dModelAvailablePatterns.json");
  notAvailablePatterns = readMaster("costume3dModelNotAvailablePatterns.json");
  defaultHairs = readMaster("costume3dModelDefaultHairs.json");

  costumeById = new Map(costume3ds.map((entry) => [entry.id, entry]));
  cardById = new Map(cards.map((entry) => [entry.id, entry]));
  modelsByCostumeId = groupBy(costume3dModels, (entry) => entry.costume3dId);
  cardCostumesByCardId = groupBy(cardCostume3ds, (entry) => entry.cardId);
  costumesByGroupId = groupBy(costume3ds, (entry) => entry.costume3dGroupId);

  errors = [];
  warnings = [];
  notes = [];
  samples = {};

  auditCostumeModelReferences();
  auditCardCostumeReferences();
  auditCompatibilityPatterns();
  auditCostumeGroups();
  auditModelShape();

  return {
    input: {
      masterDir,
      costume3ds: costume3ds.length,
      costume3dModels: costume3dModels.length,
      cards: cards.length,
      cardCostume3ds: cardCostume3ds.length,
      availablePatterns: availablePatterns.length,
      notAvailablePatterns: notAvailablePatterns.length,
      defaultHairs: defaultHairs.length,
    },
    counts: {
      costume3dsByPartType: countBy(costume3ds, (entry) => entry.partType ?? "<missing>"),
      costume3dModelsByPartType: countBy(costume3dModels, (entry) => (
        costumeById.get(entry.costume3dId)?.partType ?? "<missing-costume>"
      )),
      headAssetbundleTypes: countBy(costume3dModels, (entry) => (
        entry.headCostume3dAssetbundleType ?? "<none>"
      )),
      cardUnlockPartsets: countCardUnlockPartsets(),
      costumeGroupPartsets: countCostumeGroupPartsets(),
      costumeGroupColorCounts: countBy(
        [...costumesByGroupId.values()],
        (entries) => String(new Set(entries.map((entry) => entry.colorId)).size)
      ),
    },
    errors,
    warnings,
    notes,
    samples,
  };
}

if (isMainModule()) {
  const args = parseArgs(process.argv.slice(2));
  const strict = args.strict === "true";
  const jsonOutput = args.json === "true";
  const summary = auditCostumeMasterdata({
    masterDir: args.master,
    sampleLimit: args.sampleLimit,
  });

  if (jsonOutput) {
    console.log(JSON.stringify(summary, null, 2));
  } else {
    printHumanSummary(summary);
  }

  if (summary.errors.length > 0 || strict && summary.warnings.length > 0) {
    process.exitCode = 1;
  }
}

function auditCostumeModelReferences() {
  const missingCostumes = [];
  for (const model of costume3dModels) {
    if (!costumeById.has(model.costume3dId)) {
      missingCostumes.push(model.costume3dId);
    }
  }

  if (missingCostumes.length > 0) {
    addError("costume3dModels_missing_costume3ds", {
      count: missingCostumes.length,
      uniqueCount: new Set(missingCostumes).size,
    });
    addSample("costume3dModels_missing_costume3ds", missingCostumes);
  }
}

function auditCardCostumeReferences() {
  const missingCards = [];
  const missingCostumes = [];
  const characterMismatches = [];

  for (const link of cardCostume3ds) {
    const card = cardById.get(link.cardId);
    const costume = costumeById.get(link.costume3dId);
    if (!card) {
      missingCards.push(link);
      continue;
    }
    if (!costume) {
      missingCostumes.push(link);
      continue;
    }
    if (card.characterId !== costume.characterId) {
      characterMismatches.push({
        cardId: link.cardId,
        costume3dId: link.costume3dId,
        cardCharacterId: card.characterId,
        costumeCharacterId: costume.characterId,
      });
    }
  }

  if (missingCards.length > 0) {
    addError("cardCostume3ds_missing_cards", { count: missingCards.length });
    addSample("cardCostume3ds_missing_cards", missingCards);
  }
  if (missingCostumes.length > 0) {
    addError("cardCostume3ds_missing_costume3ds", { count: missingCostumes.length });
    addSample("cardCostume3ds_missing_costume3ds", missingCostumes);
  }
  if (characterMismatches.length > 0) {
    addError("cardCostume3ds_character_mismatch", { count: characterMismatches.length });
    addSample("cardCostume3ds_character_mismatch", characterMismatches);
  }
}

function auditCompatibilityPatterns() {
  const available = normalizePatterns(availablePatterns, "available");
  const notAvailable = normalizePatterns(notAvailablePatterns, "not_available");
  const defaults = normalizePatterns(defaultHairs, "default_hair");
  const conflicts = [];
  let defaultInAvailable = 0;
  let defaultInNotAvailable = 0;
  let defaultOnly = 0;

  for (const key of available.keys()) {
    if (notAvailable.has(key)) {
      conflicts.push(key);
    }
  }

  for (const key of defaults.keys()) {
    if (available.has(key)) {
      defaultInAvailable += 1;
    } else {
      defaultOnly += 1;
    }
    if (notAvailable.has(key)) {
      defaultInNotAvailable += 1;
    }
  }

  if (conflicts.length > 0) {
    addError("available_notAvailable_conflicts", { count: conflicts.length });
    addSample("available_notAvailable_conflicts", conflicts);
  }

  addNote("compatibility_pattern_stats", {
    availableRows: availablePatterns.length,
    availableKeys: available.size,
    availableDuplicateRows: available.duplicateRows,
    availableConflictingDefaultRows: available.conflictingIsDefaultRows,
    notAvailableRows: notAvailablePatterns.length,
    notAvailableKeys: notAvailable.size,
    defaultHairRows: defaultHairs.length,
    defaultHairKeys: defaults.size,
    defaultInAvailable,
    defaultInNotAvailable,
    defaultOnly,
  });

  for (const [name, normalized] of [
    ["available", available],
    ["not_available", notAvailable],
    ["default_hair", defaults],
  ]) {
    const missingHeadIds = [];
    const missingHairIds = [];
    const invalidHeadPartIds = [];
    const invalidHairPartIds = [];

    for (const entry of normalized.values()) {
      const head = costumeById.get(entry.headCostume3dId);
      const hair = costumeById.get(entry.hairCostume3dId);
      if (!head) {
        missingHeadIds.push(entry.headCostume3dId);
      } else if (head.partType !== "head") {
        invalidHeadPartIds.push(entry.headCostume3dId);
      }
      if (!hair) {
        missingHairIds.push(entry.hairCostume3dId);
      } else if (hair.partType !== "hair") {
        invalidHairPartIds.push(entry.hairCostume3dId);
      }
    }

    warnMissingPatternRefs(name, "head", missingHeadIds);
    warnMissingPatternRefs(name, "hair", missingHairIds);
    warnInvalidPatternRefs(name, "head", invalidHeadPartIds);
    warnInvalidPatternRefs(name, "hair", invalidHairPartIds);
  }
}

function auditCostumeGroups() {
  const multiCharacterGroups = [];
  for (const [groupId, entries] of costumesByGroupId) {
    const characterIds = [...new Set(entries.map((entry) => entry.characterId))];
    if (characterIds.length > 1) {
      multiCharacterGroups.push({
        costume3dGroupId: groupId,
        characterIds,
        entries: entries.map((entry) => ({
          id: entry.id,
          characterId: entry.characterId,
          partType: entry.partType,
          colorId: entry.colorId,
          name: entry.name,
        })),
      });
    }
  }

  if (multiCharacterGroups.length > 0) {
    addWarning("costume3dGroupId_multi_character_groups", {
      count: multiCharacterGroups.length,
      note: "Allowed for default/virtual-singer style groups; do not assume groupId is globally single-character.",
    });
    addSample("costume3dGroupId_multi_character_groups", multiCharacterGroups);
  }
}

function auditModelShape() {
  const missingAssetbundleNames = [];
  const headOnlyWithoutAttachNode = [];
  const modelsWithColor = [];

  for (const model of costume3dModels) {
    const costume = costumeById.get(model.costume3dId);
    if (!model.assetbundleName) {
      missingAssetbundleNames.push({
        costume3dId: model.costume3dId,
        partType: costume?.partType ?? null,
        unit: model.unit ?? null,
      });
    }
    if (model.headCostume3dAssetbundleType === "head_only") {
      const normalizedName = normalizeBundleName(model.assetbundleName ?? "");
      const pieces = normalizedName.split("/").filter(Boolean);
      if (!model.part && pieces.length < 2) {
        headOnlyWithoutAttachNode.push({
          costume3dId: model.costume3dId,
          unit: model.unit ?? null,
          assetbundleName: model.assetbundleName ?? null,
        });
      }
    }
    if (model.colorAssetbundleName) {
      modelsWithColor.push(model);
    }
  }

  if (missingAssetbundleNames.length > 0) {
    addWarning("costume3dModels_missing_assetbundleName", {
      count: missingAssetbundleNames.length,
      note: "Skip these for exportable part registry unless another resolver path is added.",
    });
    addSample("costume3dModels_missing_assetbundleName", missingAssetbundleNames);
  }
  if (headOnlyWithoutAttachNode.length > 0) {
    addWarning("head_only_missing_attach_node", {
      count: headOnlyWithoutAttachNode.length,
      note: "head_only requires model.part or an assetbundleName shaped like accessoryId/attachNode.",
    });
    addSample("head_only_missing_attach_node", headOnlyWithoutAttachNode);
  }

  addNote("color_assetbundle_stats", {
    modelsWithColorAssetbundleName: modelsWithColor.length,
    note: "colorAssetbundleName is a color/material override, not a separate structural model.",
  });
}

function warnMissingPatternRefs(patternName, role, ids) {
  const uniqueIds = [...new Set(ids)];
  if (uniqueIds.length === 0) {
    return;
  }
  const idsWithoutModel = uniqueIds.filter((id) => !modelsByCostumeId.has(id));
  addWarning(`${patternName}_patterns_missing_${role}_costume3ds`, {
    uniqueCount: uniqueIds.length,
    withoutModelRows: idsWithoutModel.length,
    note: "Keep rules for diagnostics, but do not expose missing ids as selectable viewer parts.",
  });
  addSample(`${patternName}_patterns_missing_${role}_costume3ds`, uniqueIds);
}

function warnInvalidPatternRefs(patternName, role, ids) {
  const uniqueIds = [...new Set(ids)];
  if (uniqueIds.length === 0) {
    return;
  }
  addWarning(`${patternName}_patterns_invalid_${role}_partType`, {
    uniqueCount: uniqueIds.length,
    note: "Pattern role does not match costume3ds.partType.",
  });
  addSample(`${patternName}_patterns_invalid_${role}_partType`, uniqueIds);
}

function normalizePatterns(patterns) {
  const map = new Map();
  let duplicateRows = 0;
  let conflictingIsDefaultRows = 0;
  for (const pattern of patterns) {
    const key = compatibilityKey(pattern);
    const normalized = {
      unit: pattern.unit ?? "",
      headCostume3dId: pattern.headCostume3dId,
      hairCostume3dId: pattern.hairCostume3dId,
      isDefault: Boolean(pattern.isDefault),
    };
    const existing = map.get(key);
    if (existing) {
      duplicateRows += 1;
      if (existing.isDefault !== normalized.isDefault) {
        conflictingIsDefaultRows += 1;
      }
      existing.isDefault = existing.isDefault || normalized.isDefault;
    } else {
      map.set(key, normalized);
    }
  }
  map.duplicateRows = duplicateRows;
  map.conflictingIsDefaultRows = conflictingIsDefaultRows;
  return map;
}

function compatibilityKey(pattern) {
  return `${pattern.unit ?? ""}|${pattern.headCostume3dId}|${pattern.hairCostume3dId}`;
}

function countCardUnlockPartsets() {
  const partsets = [];
  for (const rows of cardCostumesByCardId.values()) {
    partsets.push(rows
      .map((row) => costumeById.get(row.costume3dId)?.partType ?? "<missing>")
      .sort()
      .join("+"));
  }
  return countBy(partsets, (entry) => entry);
}

function countCostumeGroupPartsets() {
  const partsets = [];
  for (const entries of costumesByGroupId.values()) {
    partsets.push([...new Set(entries.map((entry) => entry.partType ?? "<missing>"))]
      .sort()
      .join("+"));
  }
  return countBy(partsets, (entry) => entry);
}

function countBy(entries, getKey) {
  const counts = new Map();
  for (const entry of entries) {
    const key = getKey(entry);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return Object.fromEntries([...counts].sort(([left], [right]) => (
    String(left).localeCompare(String(right))
  )));
}

function groupBy(entries, getKey) {
  const groups = new Map();
  for (const entry of entries) {
    const key = getKey(entry);
    const group = groups.get(key);
    if (group) {
      group.push(entry);
    } else {
      groups.set(key, [entry]);
    }
  }
  return groups;
}

function addError(code, detail) {
  errors.push({ code, ...detail });
}

function addWarning(code, detail) {
  warnings.push({ code, ...detail });
}

function addNote(code, detail) {
  notes.push({ code, ...detail });
}

function addSample(code, values) {
  samples[code] = values.slice(0, sampleLimit);
}

function readMaster(fileName) {
  return JSON.parse(readFileSync(path.join(masterDir, fileName), "utf8"));
}

function normalizeBundleName(assetbundleName) {
  return assetbundleName.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
}

function printHumanSummary(result) {
  console.log(`masterDir=${result.input.masterDir}`);
  console.log(`errors=${result.errors.length} warnings=${result.warnings.length} notes=${result.notes.length}`);
  console.log("inputCounts:");
  console.log(JSON.stringify(result.input, null, 2));
  console.log("coreCounts:");
  console.log(JSON.stringify(result.counts, null, 2));

  if (result.errors.length > 0) {
    console.log("errors:");
    for (const error of result.errors) {
      console.log(`- ${error.code}: ${JSON.stringify(error)}`);
    }
  }
  if (result.warnings.length > 0) {
    console.log("warnings:");
    for (const warning of result.warnings) {
      console.log(`- ${warning.code}: ${JSON.stringify(warning)}`);
    }
  }
  if (result.notes.length > 0) {
    console.log("notes:");
    for (const note of result.notes) {
      console.log(`- ${note.code}: ${JSON.stringify(note)}`);
    }
  }
  if (Object.keys(result.samples).length > 0) {
    console.log("samples:");
    console.log(JSON.stringify(result.samples, null, 2));
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

function isMainModule() {
  return process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
}
