# Haruki-3D-Exporter

Offline converter for Project SEKAI character bundles.

The converter reads Unity AssetBundles with AssetStudio and writes a browser-friendly runtime package for Haruki 3D Engine.

## Final Pipeline

The exporter has one production package format: role-scoped registries, core+delta part packages, role runtimes, and Brotli-compressed MessagePack metadata.

Build the package in three stages:

```bash
dotnet run -- \\
  --emit-costume-registries \\
  --master /path/to/master \\
  --asset-root /path/to/AssetBundles \\
  --out /path/to/output

dotnet run -- \\
  --emit-part-packages \\
  --master /path/to/master \\
  --asset-root /path/to/AssetBundles \\
  --out /path/to/output

dotnet run -- \\
  --emit-role-runtimes \\
  --master /path/to/master \\
  --asset-root /path/to/AssetBundles \\
  --out /path/to/output
```

The asset root must contain `live_pv/model/characterv2`. Runtime metadata is always emitted as `.msgpack.br`; JSON, gzip, self-contained part runtimes, legacy `character` roots, and direct VRM/GLB full exports are not supported.

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
  --emit-role-runtimes \
  --role-character3d-id 5 \
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

- `character3d-index.msgpack.br` for official preset packages keyed by `character3ds.id`
- `parts/part-registry.msgpack.br` for body, hair, and head/head_optional rows
- `parts/part-registry-compact.msgpack.br` as the field-name-free global
  registry consumed by Cloud
- `parts/head-hair-compatibility.msgpack.br` for custom-mode head/hair rules
- `parts/head-hair-compatibility-compact.msgpack.br` as the field-name-free
  compatibility registry consumed by Cloud
- `parts/compat/by-unit/*/head-hair-compatibility.msgpack.br` as a runtime-sized
  per-unit deny list; the full registry above remains available for audits
- `parts/card-costume-unlocks.msgpack.br` for card unlock/source metadata

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

This writes a light
`parts/<partType>/<costume3dId>/<unit>/part-runtime.msgpack.br` delta. Heavy
native meshes, SpringBone data, and morph bindings shared by color variants are
written once under `parts/_cores/<partType>/<hash>/part-runtime-core.msgpack.br`.
Textures are written directly to the output's exact SHA-256 store under
`_texture_store/sha256`; packages refer to those immutable files instead of
building and later deleting duplicate part-local texture trees.

Pass `--shared-content-store <directory>` to place exact texture and
`part-runtime*.msgpack.br` bytes in a cross-region SHA-256 CAS.
Region paths stay unchanged and are hard-linked to immutable CAS objects, so
the output and shared store must be on the same filesystem. The first run is an
explicit full migration. Later runs use `content-addressed-store-state.json` to
skip files that are still protected CAS links; only newly exported or replaced
files are hashed and relinked.

Pass `--compiled-content-store <directory>` together with the shared content
store to reuse already compiled core/delta objects across sequential region
exports when their resolved input bundles are byte-identical. Restored deltas
are patched with the current region's identity and manifest fields. The shared
content store is the authoritative source for the cached texture hashes.

Texture lossless optimization is deliberately separate from package export.
After publishing an output, run the exporter with only `--out`,
`--optimize-texture-store`, and the desired `--png-optimize`/worker options.
The optimizer works on temporary files and keeps a result only when it is
smaller. It stores the optimized bytes under their new exact hash, rewrites
part-runtime references, and only then removes the old object, so exports do not
wait for oxipng and CAS paths remain truthful.

Runtime metadata uses direct object-to-MessagePack serialization and Brotli quality
6. It avoids the former JSON UTF-8 and DOM intermediate while retaining a good
size/speed balance.

Large arrays on the explicit native-mesh and Unity-motion
schemas use runtime extension type `42`: float data is little-endian float32 and
mesh indexes are little-endian uint16/uint32. Unrelated arrays with the same
property names remain ordinary MessagePack arrays.

Viewer custom mode must merge the active part SpringBone records, rebind current
body colliders, and reset simulation whenever body/head/hair/accessory selection
changes. Preset mode should continue to load full `character3ds.id` packages.
