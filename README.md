# Haruki-3D-Exporter

Offline converter for Project SEKAI character bundles.

The converter reads Unity AssetBundles with AssetStudio and writes a browser-friendly runtime package for Haruki 3D Engine.

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

Most command defaults can also be stored in a local config file:

```bash
cp haruki-3d-exporter.config.example.json haruki-3d-exporter.config.json

./scripts/dotnet.sh run -- \
  --config haruki-3d-exporter.config.json \
  --character3d-id 5
```

`haruki-3d-exporter.config.json` is ignored by git. The tracked `.example.json` is the public template and should only contain placeholder paths. CLI flags override values loaded from config.

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

To generate only `face_motion.json` from a motion bundle or decoded
AnimationClip JSON output:

```bash
./scripts/dotnet.sh run -- \
  --export-face-motion \
  --motion <costume_setting.bundle-or-decoded-folder-or-json> \
  --out <face_motion.json-or-output-directory>
```

This path is implemented in C# through the same AssetStudio/AnimationClip
decoder used by the main exporter. It does not require a Python-side animation
helper on the local machine or remote server.

## Lean Output

By default the converter writes the runtime package and prunes intermediate/debug files:

```text
character/character.vrm
character/textures/**
pjsk-sekai-runtime.extension.msgpack.br
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

The final package contains `PJSK_sekai_runtime`, written into `character/character.vrm` and as `pjsk-sekai-runtime.extension.msgpack.br` by default. Use `--runtime-json-output gzip`, `json`, or `both` only for compatibility/debug output.

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

When building outside Docker against a local AssetStudio checkout, pass its path through MSBuild:

```bash
./scripts/dotnet.sh build -p:AssetStudioRoot=<AssetStudio-Haruki-directory>
```

Publish the Linux x64 runtime directory used by Haruki-Sekai-Asset-Updater external mounts:

```bash
scripts/publish-linux-x64.sh /data/xy/haruki-3d-exporter-runtime/linux-x64
```

The output directory contains a self-contained `Haruki-3D-Exporter` executable and its AssetStudio runtime dependencies.
Mount that directory into updater deployments that enable `regions.<region>.export.haruki_3d`.

If the host does not have a .NET SDK, build the Docker image and copy `/app/exporter` out of a created container.
That copied directory is the same external runtime mount payload.

## Docker

Build the Linux exporter image:

```bash
docker build -t haruki-3d-exporter .
```

The Docker build clones `Team-Haruki/AssetStudio` and builds the required
AssetStudio `net8.0` dependencies inside the image. Override the source when
needed:

```bash
docker build \
  --build-arg ASSETSTUDIO_REPOSITORY=https://github.com/Team-Haruki/AssetStudio.git \
  --build-arg ASSETSTUDIO_BRANCH=sekai-modified \
  -t haruki-3d-exporter .
```

Run the image by mounting masterdata, AssetBundles, and an output directory:

```bash
docker run --rm \
  -v <config-file>:/app/haruki-3d-exporter.config.json:ro \
  -v <master-data-dir>:/data/master:ro \
  -v <asset-bundle-root>:/data/assets:ro \
  -v <output-dir>:/data/out \
  haruki-3d-exporter \
  --config /app/haruki-3d-exporter.config.json \
  --character3d-id 5 \
  --master /data/master \
  --asset-root /data/assets \
  --out /data/out
```

GitHub Actions builds and publishes a self-contained Linux image to GHCR on `main` and version
tags. Pull requests only build the image.

## Masterdata Audit

The costume masterdata audit checks the relationships needed by preset/custom
viewer modes without opening Unity bundles:

```bash
node --test scripts/test-costume-masterdata-audit.mjs

node scripts/audit-costume-masterdata.mjs \
  --master <master-data-dir>
```

The audit treats broken hard references as errors and known masterdata quirks as
warnings. Pattern rows that point to missing costume ids are kept for diagnostics,
but those ids should not be exposed as selectable viewer parts.

If textures look wrong after converter changes, regenerate the output folder and re-import the whole folder in the viewer. Browser blob URLs can otherwise keep stale files alive.

## License

Haruki-3D-Exporter is released under the MIT License. See `LICENSE`.

## Costume Registries

Generate compact viewer/exporter registries from masterdata and the local bundle
mirror:

```bash
./scripts/dotnet.sh run -- \
  --emit-costume-registries \
  --master <master-data-dir> \
  --asset-root <asset-bundle-root> \
  --out <output-dir>
```

This writes:

- `character3d-index.json` for official preset packages keyed by `character3ds.id`
- `parts/part-registry.json` for body, hair, and head/head_optional rows
- `parts/part-registry-compact.msgpack.br` as the field-name-free global
  registry consumed by Cloud
- `parts/head-hair-compatibility.json` for custom-mode head/hair rules
- `parts/head-hair-compatibility-compact.msgpack.br` as the field-name-free
  compatibility registry consumed by Cloud
- `parts/compat/by-unit/*/head-hair-compatibility.json` as a runtime-sized
  per-unit deny list; the full registry above remains available for audits
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
  --part-costume3d-id 2 \
  --part-type body \
  --master <master-data-dir> \
  --asset-root <asset-bundle-root> \
  --out <output-dir>
```

This writes `parts/<partType>/<costume3dId>/<unit>/part-runtime.msgpack.br` plus
part-local textures. The package includes native meshes, material slots, texture
roles, prefab graph metadata, and part-scoped SpringBone records.

Pass `--shared-content-store <directory>` after `--compact-textures` to place
exact texture and `part-runtime.msgpack.br` bytes in a cross-region SHA-256 CAS.
Region paths stay unchanged and are hard-linked to immutable CAS objects, so
the output and shared store must be on the same filesystem. The first run is an
explicit full migration. Later runs use `content-addressed-store-state.json` to
skip files that are still protected CAS links; only newly exported or replaced
files are hashed and relinked.

In `msgpack-br` mode, large arrays on the explicit native-mesh and Unity-motion
schemas use runtime extension type `42`: float data is little-endian float32 and
mesh indexes are little-endian uint16/uint32. Unrelated arrays with the same
property names remain ordinary MessagePack arrays. Older ordinary arrays remain
valid. Deploy an Engine version with extension-42 support before exporting or
serving packages produced by this version.

Viewer custom mode must merge the active part SpringBone records, rebind current
body colliders, and reset simulation whenever body/head/hair/accessory selection
changes. Preset mode should continue to load full `character3ds.id` packages.
