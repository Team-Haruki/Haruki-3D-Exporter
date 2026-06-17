#!/usr/bin/env bash
set -euo pipefail

PROJECT_DOTNET_ROOT="${PJSK_DOTNET_ROOT:-/home/storyxy3/.dotnet}"
export DOTNET_ROOT="$PROJECT_DOTNET_ROOT"
export HOME="${HARUKI_DOTNET_HOME:-/tmp/haruki-3d-exporter-home}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME}"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/haruki-3d-exporter-nuget/packages}"
DOTNET_BIN="${PROJECT_DOTNET_ROOT}/dotnet"
mkdir -p "$HOME" "$NUGET_PACKAGES"

if [[ ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet not found at ${DOTNET_BIN}" >&2
  exit 127
fi

cmd="${1:-}"
case "$cmd" in
  build|restore|publish|test)
    shift
    exec "$DOTNET_BIN" "$cmd" \
      -p:BaseIntermediateOutputPath="${PJSK_DOTNET_OBJ:-/tmp/haruki-3d-exporter-obj}/" \
      -p:BaseOutputPath="${PJSK_DOTNET_BIN:-/tmp/haruki-3d-exporter-bin}/" \
      -p:RestoreConfigFile="${HARUKI_NUGET_CONFIG:-${REPO_ROOT}/NuGet.Config}" \
      "$@"
    ;;
  run)
    shift
    app_args=()
    after_delimiter=0
    for arg in "$@"; do
      if [[ "$arg" == "--" && "$after_delimiter" -eq 0 ]]; then
        after_delimiter=1
        continue
      fi

      app_args+=("$arg")
    done

    "$DOTNET_BIN" build \
      -p:BaseIntermediateOutputPath="${PJSK_DOTNET_OBJ:-/tmp/haruki-3d-exporter-obj}/" \
      -p:BaseOutputPath="${PJSK_DOTNET_BIN:-/tmp/haruki-3d-exporter-bin}/" \
      -p:RestoreConfigFile="${HARUKI_NUGET_CONFIG:-${REPO_ROOT}/NuGet.Config}" \
      >/dev/null
    exec "$DOTNET_BIN" "${PJSK_DOTNET_BIN:-/tmp/haruki-3d-exporter-bin}/Debug/net8.0/Haruki-3D-Exporter.dll" \
      "${app_args[@]}"
    ;;
  *)
    exec "$DOTNET_BIN" "$@"
    ;;
esac
