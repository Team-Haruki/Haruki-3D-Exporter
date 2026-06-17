#!/usr/bin/env bash
set -euo pipefail

ASSETSTUDIO_REPOSITORY="${ASSETSTUDIO_REPOSITORY:-https://github.com/Team-Haruki/AssetStudio.git}"
ASSETSTUDIO_BRANCH="${ASSETSTUDIO_BRANCH:-sekai-modified}"
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
