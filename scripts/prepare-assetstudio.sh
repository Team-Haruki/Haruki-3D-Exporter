#!/usr/bin/env bash
set -euo pipefail

ASSETSTUDIO_REPOSITORY="${ASSETSTUDIO_REPOSITORY:-https://github.com/Team-Haruki/AssetStudio.git}"
ASSETSTUDIO_BRANCH="${ASSETSTUDIO_BRANCH:-sekai-modified}"
ASSETSTUDIO_REVISION="${ASSETSTUDIO_REVISION:-90763ac6fbb6839d67524e8e6bf1d3b84426b03d}"
ASSETSTUDIO_ROOT="${ASSETSTUDIO_ROOT:-/tmp/haruki-assetstudio}"
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/haruki-3d-exporter-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/haruki-3d-exporter-nuget/packages}"
mkdir -p "${DOTNET_CLI_HOME}" "${NUGET_PACKAGES}"

if [[ ! -d "${ASSETSTUDIO_ROOT}/.git" ]]; then
  rm -rf "${ASSETSTUDIO_ROOT}"
  git clone --depth 1 --single-branch --branch "${ASSETSTUDIO_BRANCH}" \
    "${ASSETSTUDIO_REPOSITORY}" "${ASSETSTUDIO_ROOT}"
fi

actual_revision="$(git -C "${ASSETSTUDIO_ROOT}" rev-parse HEAD)"
if [[ "${actual_revision}" != "${ASSETSTUDIO_REVISION}" ]]; then
  git -C "${ASSETSTUDIO_ROOT}" fetch --depth 1 origin "${ASSETSTUDIO_REVISION}"
  git -C "${ASSETSTUDIO_ROOT}" checkout --detach "${ASSETSTUDIO_REVISION}"
fi

(
  cd "${ASSETSTUDIO_ROOT}/AssetStudioCLI"
  "${DOTNET_BIN}" build AssetStudioCLI.csproj \
  -c Release \
  -f net8.0 \
  -r linux-x64 \
  --self-contained false \
  -p:TargetFrameworks=net8.0 \
  -p:SolutionDir="${ASSETSTUDIO_ROOT}/"
)

mkdir -p "${ASSETSTUDIO_ROOT}/AssetStudioCLI/bin/Release/net8.0"
cp -a "${ASSETSTUDIO_ROOT}/AssetStudioCLI/bin/Release/net8.0/linux-x64/." \
  "${ASSETSTUDIO_ROOT}/AssetStudioCLI/bin/Release/net8.0/"
