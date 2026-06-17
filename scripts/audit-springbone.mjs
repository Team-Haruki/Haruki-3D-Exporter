#!/usr/bin/env node

import { readFileSync, statSync } from "node:fs";
import { join } from "node:path";

function usage() {
  console.error("usage: node scripts/audit-springbone.mjs <head.springbone.json|springbone.json|output-dir> [...]");
  process.exit(1);
}

const inputs = process.argv.slice(2);
if (inputs.length === 0) {
  usage();
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function resolveInput(input) {
  const stat = statSync(input);
  if (!stat.isDirectory()) {
    return input;
  }
  return join(input, "head.springbone.json");
}

function parentPath(path) {
  if (!path || !path.includes("/")) {
    return null;
  }
  return path.slice(0, path.lastIndexOf("/"));
}

function pathDepth(path) {
  return path ? path.split("/").length : 0;
}

function isSameOrDescendant(path, root) {
  return path === root || path?.startsWith(`${root}/`);
}

function readPart(payload) {
  if (Array.isArray(payload?.Bones)) {
    return payload;
  }
  if (payload?.Head && Array.isArray(payload.Head.Bones)) {
    return payload.Head;
  }
  if (payload?.head && Array.isArray(payload.head.Bones)) {
    return payload.head;
  }
  throw new Error("input does not look like head.springbone.json or combined springbone.json");
}

function objectName(ref) {
  return ref?.Name ?? ref?.name ?? "<none>";
}

function objectPath(ref) {
  return ref?.TransformPath ?? ref?.transformPath ?? null;
}

function rawNumber(raw, key, fallback = null) {
  const value = raw?.[key] ?? raw?.[key[0].toUpperCase() + key.slice(1)];
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function rawVec(raw, key) {
  const value = raw?.[key] ?? raw?.[key[0].toUpperCase() + key.slice(1)] ?? {};
  return {
    x: rawNumber(value, "x", rawNumber(value, "X", 0)),
    y: rawNumber(value, "y", rawNumber(value, "Y", 0)),
    z: rawNumber(value, "z", rawNumber(value, "Z", 0)),
  };
}

function angleLimit(raw, key) {
  const value = raw?.[key] ?? {};
  return {
    active: Boolean(value.active ?? value.Active),
    min: typeof value.min === "number" ? value.min : typeof value.Min === "number" ? value.Min : null,
    max: typeof value.max === "number" ? value.max : typeof value.Max === "number" ? value.Max : null,
  };
}

function classifyHairName(name) {
  const lower = String(name ?? "").toLowerCase();
  const segmentMatch = lower.match(/_hair_(\d+)/);
  const familyMatch = lower.match(/(?:ex_)?((?:left|right|back|center)(?:_[a-z]+)?_hair)_\d+/);
  return {
    side: lower.includes("left_")
      ? "left"
      : lower.includes("right_")
        ? "right"
        : lower.includes("back_")
          ? "back"
          : lower.includes("center_")
            ? "center"
            : "unknown",
    segment: segmentMatch ? Number(segmentMatch[1]) : null,
    mirrorKey: lower
      .replace(/^ex_/, "")
      .replace(/^left_/, "side_")
      .replace(/^right_/, "side_"),
    family: familyMatch?.[1] ?? lower,
  };
}

function fmtLimit(limit) {
  if (!limit.active) {
    return "off";
  }
  return `[${limit.min ?? "-inf"},${limit.max ?? "inf"}]`;
}

function fmtVec(vec) {
  return `${vec.x}/${vec.y}/${vec.z}`;
}

function buildChains(bones) {
  const springNodePaths = new Set();
  const managerOrder = new Map();
  bones.forEach((bone, index) => {
    managerOrder.set(bone.PathId ?? bone.pathId ?? index, index);
    const bonePath = objectPath(bone.GameObject ?? bone.gameObject);
    const pivotPath = objectPath(bone.PivotNode ?? bone.pivotNode);
    if (bonePath) {
      springNodePaths.add(bonePath);
    }
    if (pivotPath) {
      springNodePaths.add(pivotPath);
    }
  });

  const roots = [...springNodePaths]
    .filter((path) => {
      const parent = parentPath(path);
      return !parent || !springNodePaths.has(parent);
    })
    .sort((a, b) => pathDepth(a) - pathDepth(b) || a.localeCompare(b));

  const assigned = new Set();
  const chains = [];
  for (const root of roots) {
    const chainBones = bones
      .filter((bone) => !assigned.has(bone.PathId ?? bone.pathId))
      .filter((bone) => {
        const bonePath = objectPath(bone.GameObject ?? bone.gameObject);
        const pivotPath = objectPath(bone.PivotNode ?? bone.pivotNode);
        return isSameOrDescendant(bonePath, root) || isSameOrDescendant(pivotPath, root);
      })
      .sort((a, b) => {
        const aPath = objectPath(a.GameObject ?? a.gameObject);
        const bPath = objectPath(b.GameObject ?? b.gameObject);
        return (
          pathDepth(aPath) - pathDepth(bPath) ||
          (managerOrder.get(a.PathId ?? a.pathId) ?? 9999) - (managerOrder.get(b.PathId ?? b.pathId) ?? 9999)
        );
      });
    if (chainBones.length === 0) {
      continue;
    }
    for (const bone of chainBones) {
      assigned.add(bone.PathId ?? bone.pathId);
    }
    chains.push({ root, bones: chainBones });
  }
  return { chains, springNodePaths };
}

function summarize(path) {
  const payload = readJson(path);
  const part = readPart(payload);
  const bones = part.Bones ?? part.bones ?? [];
  const managerRefs = part.Managers?.[0]?.Raw?.springBones ?? part.managers?.[0]?.raw?.springBones ?? [];
  const { chains, springNodePaths } = buildChains(bones);
  const mirrorCandidates = new Map();

  console.log(`\n== ${path}`);
  console.log(`part=${part.PartKind ?? part.partKind ?? "unknown"} managers=${part.Managers?.length ?? 0} bones=${bones.length} managerRefs=${managerRefs.length} chains=${chains.length}`);
  console.log(`spring semantics: fixed=pivot->spring, angular=spring->next pivot, root pivots are passive anchors`);

  chains.forEach((chain, chainIndex) => {
    const rootName = chain.root.split("/").pop();
    console.log(`\n[${chainIndex}] root=${rootName} path=${chain.root} joints=${chain.bones.length}`);
    chain.bones.forEach((bone, jointIndex) => {
      const gameObject = bone.GameObject ?? bone.gameObject;
      const pivot = bone.PivotNode ?? bone.pivotNode;
      const raw = bone.Raw ?? bone.raw ?? {};
      const name = objectName(gameObject);
      const pivotName = objectName(pivot);
      const bonePath = objectPath(gameObject);
      const pivotPath = objectPath(pivot);
      const parentPivotInSpringSet = springNodePaths.has(parentPath(pivotPath));
      const parentBoneInSpringSet = springNodePaths.has(parentPath(bonePath));
      const y = angleLimit(raw, "yAngleLimits");
      const z = angleLimit(raw, "zAngleLimits");
      const info = classifyHairName(name);
      mirrorCandidates.set(info.mirrorKey, [
        ...(mirrorCandidates.get(info.mirrorKey) ?? []),
        { name, y, z, flag: rawNumber(raw, "colliderFlag", 0) },
      ]);
      console.log(
        [
          `  ${String(jointIndex).padStart(2, "0")}`,
          name,
          `pivot=${pivotName}`,
          `rootPivot=${!parentPivotInSpringSet}`,
          `springRoot=${!parentBoneInSpringSet}`,
          `side=${info.side}`,
          `seg=${info.segment ?? "-"}`,
          `flag=${rawNumber(raw, "colliderFlag", 0)}`,
          `stiff=${bone.StiffnessForce ?? bone.stiffnessForce}`,
          `drag=${bone.DragForce ?? bone.dragForce}`,
          `force=${fmtVec(rawVec(bone, "SpringForce"))}`,
          `y=${fmtLimit(y)}`,
          `z=${fmtLimit(z)}`,
        ].join(" | ")
      );
    });
  });

  const mirrorRows = [...mirrorCandidates.values()]
    .filter((items) => items.length === 2 && items.some((item) => item.name.includes("Left_")) && items.some((item) => item.name.includes("Right_")))
    .map((items) => {
      const left = items.find((item) => item.name.includes("Left_"));
      const right = items.find((item) => item.name.includes("Right_"));
      const yMatches = left.y.min === right.y.min && left.y.max === right.y.max;
      const zMirrors = left.z.min === -right.z.max && left.z.max === -right.z.min;
      return `${left.name} <-> ${right.name} | ySame=${yMatches} zMirror=${zMirrors} | Lz=${fmtLimit(left.z)} Rz=${fmtLimit(right.z)} | flags=${left.flag}/${right.flag}`;
    });
  if (mirrorRows.length) {
    console.log("\nmirror pairs:");
    mirrorRows.forEach((row) => console.log(`  ${row}`));
  }
}

for (const input of inputs) {
  summarize(resolveInput(input));
}
