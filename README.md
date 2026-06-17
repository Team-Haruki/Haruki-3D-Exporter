# Haruki-3D-Exporter

Offline converter for Project SEKAI character bundles.

The converter reads Unity AssetBundles with AssetStudio and writes a browser-friendly runtime package for `pjsk-webgl-viewer`.

## Quick Start

Use the repository wrapper, not system `dotnet`:

```bash
./scripts/dotnet.sh run -- \
  --character3d-id 5 \
  --master <master-data-directory> \
  --asset-root <assetbundle-root> \
  --out <output-directory>
```

The wrapper uses the SDK pinned by `global.json` and redirects build intermediates away from the checkout.

Direct bundle mode is also available:

```bash
./scripts/dotnet.sh run -- \
  --body /path/to/body.bundle-or-directory \
  --head /path/to/head.bundle \
  --out /path/to/output-directory
```

## Inputs

Preferred input:

- `--character3d-id <id>`
- `--master <master-directory>`
- `--asset-root <AssetBundles-root>`
- `--out <directory>`

The character3d resolver uses master data to pick body, head, hair/head composition, and accessory head data when needed.

Motion input:

- Explicit: `--motion <costume_setting.bundle-or-export-folder>`
- Automatic for character3d mode:
  - `character/motion/costume_setting/<characterId>_00.bundle`
  - `motion/costume_setting/<characterId>_00.bundle`
  - `costume_setting/<characterId>_00.bundle`

An exported motion folder may contain:

- `motion.glb`
- `motion_loop.glb`
- `face_motion.json`
- `light_motion.json`

## Lean Output

By default the converter writes the runtime package and prunes intermediate/debug files:

```text
character/character.vrm
character/textures/**
pjsk-sekai-runtime.extension.json
motion/body_motion.glb                # when motion is resolved
body.springbone.json
head.springbone.json
springbone.json
vrm-springbone.candidate.json
vrmc-springbone.extension.json
vrmc-springbone.resolve-report.json
```

`character/character.vrm` is a VRM-style GLB container with extra PJSK runtime semantics. Generic VRM viewers may show an approximate model, but exact rendering requires `PJSK_sekai_runtime` and the WebGL viewer.

## Debug Output

Use `--keep-intermediate` when debugging converter internals:

```bash
./scripts/dotnet.sh run -- \
  --character3d-id 5 \
  --master <master-data-directory> \
  --asset-root <assetbundle-root> \
  --out <debug-output-directory> \
  --keep-intermediate
```

This keeps older full export artifacts such as:

- split `body/body.glb` and `head/head.glb`
- intermediate character GLBs
- VRM/VRMC extension JSONs
- manifest templates
- bundle inventories
- conversion plan JSON
- resolve reports

## Runtime Extension

The final package contains `PJSK_sekai_runtime`, written both into `character/character.vrm` and as `pjsk-sekai-runtime.extension.json`.

It preserves PJSK-specific data that standard VRM cannot represent cleanly:

- C/S/H texture roles
- face SDF texture role
- material kinds and render order
- body/head assembly metadata
- body/head manifests after texture path rewrite
- character texture map relative to output root
- morph hash/channel bindings
- embedded face and light motion
- raw SpringBone metadata
- VRM SpringBone candidate data

## SpringBone State

The converter exports SpringBone metadata, but the current viewer disables UTJ runtime simulation by default.

Important SpringBone facts:

- `SpringManager.springBones` references are authoritative.
- PJSK SpringBone components may be named `SekaiSpringBone`.
- `SekaiSpringBone.colliderFlag` is required to reproduce runtime body-collider binding.
- `ModelUtility.SpringBoneSetup` appends body colliders by `CL_*` name prefixes at runtime.
- Raw, candidate, and VRMC springbone files are retained for reverse-engineering and future runtime work.

## Build

```bash
./scripts/dotnet.sh build
```

## Masterdata Audit

The costume masterdata audit checks the relationships needed by preset/custom
viewer modes without opening Unity bundles:

```bash
node --test scripts/test-costume-masterdata-audit.mjs

node scripts/audit-costume-masterdata.mjs \
  --master /mnt/d/github/testfile/master
```

The audit treats broken hard references as errors and known masterdata quirks as
warnings. Pattern rows that point to missing costume ids are kept for diagnostics,
but those ids should not be exposed as selectable viewer parts.

If textures look wrong after converter changes, regenerate the output folder and re-import the whole folder in the viewer. Browser blob URLs can otherwise keep stale files alive.

## Costume Registries

Generate compact viewer/exporter registries from masterdata and the local bundle
mirror:

```bash
./scripts/dotnet.sh run -- \
  --emit-costume-registries \
  --master /mnt/d/github/testfile/master \
  --asset-root /mnt/z/pjskdata/AssetBundles \
  --out /tmp/haruki-3d-registries
```

This writes:

- `character3d-index.json` for official preset packages keyed by `character3ds.id`
- `parts/part-registry.json` for body, hair, and head/head_optional rows
- `parts/head-hair-compatibility.json` for custom-mode head/hair rules
- `parts/card-costume-unlocks.json` for card unlock/source metadata

Registry generation does not scan the bundle mirror for every row. Part entries
therefore use `status: "planned"` when masterdata can produce a deterministic
bundle path, and `status: "missing"` only when required masterdata is absent.
Single preset/part export remains responsible for validating that the planned
bundle exists and can be opened.

Official presets are not rejected by custom head/hair pattern tables. For custom
mode, `costume3dModelNotAvailablePatterns.json` wins over available patterns,
and default hairs are emitted as hints when they are not explicit allow/block
rules.

## Runtime Part Packages

Custom runtime assembly uses the registries above plus incremental part packages.
Build one runtime-loadable package with:

```bash
./scripts/dotnet.sh run -- \
  --emit-part-packages \
  --part-costume3d-id 1 \
  --part-type body \
  --master /mnt/d/github/testfile/master \
  --asset-root /mnt/z/pjskdata/AssetBundles \
  --out /tmp/haruki-3d-registries
```

This writes `parts/<partType>/<costume3dId>/<unit>/part-runtime.json` plus
part-local textures. The package includes native meshes, material slots, texture
roles, prefab graph metadata, and part-scoped SpringBone records.

Viewer custom mode must merge the active part SpringBone records, rebind current
body colliders, and reset simulation whenever body/head/hair/accessory selection
changes. Preset mode should continue to load full `character3ds.id` packages.
