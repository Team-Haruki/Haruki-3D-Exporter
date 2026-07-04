import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function source(relativePath) {
  return readFileSync(path.join(repoRoot, relativePath), "utf8");
}

test("costume resolver keeps default face and head optional bundle fallbacks", () => {
  const resolver = source("Services/Character3dCostumeResolver.cs");
  const registry = source("Services/CostumeRegistryExporter.cs");

  for (const text of [resolver, registry]) {
    assert.match(text, /ResolveDefaultFaceBundleFallbackPath/);
    assert.match(text, /leaf\.Any\(static character => character != '0'\)/);
    assert.match(text, /fallbackLeaf = new string\('0', Math\.Max\(leaf\.Length - 1, 0\)\) \+ "1"/);
    assert.match(text, /ResolveAssetBaseDirectoryCandidates\(assetRoot, "head_optional"\)/);
    assert.match(text, /ResolveColorVariationBaseDirectoryCandidates\(assetRoot, "head_optional"\)/);
  }
});

test("part exporter preserves official runtime resource names and FaceSDF metadata", () => {
  const exporter = source("Services/PartPackageExporter.cs");

  assert.match(exporter, /var normalizedType = ResolveRuntimePartType\(entry\)/);
  assert.match(exporter, /normalizedType == "head_optional" \? SelectAccessoryRootName\(inventory\)/);
  assert.match(exporter, /"_FaceShadowTex"/);
  assert.match(exporter, /FaceShadowTex: RewriteTexturePath\(faceShadowTex, textures\)/);
  assert.match(exporter, /"accessory" => "head_optional"/);
  assert.match(exporter, /"head_optional" => "head_optional"/);
});
