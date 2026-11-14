#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
package_dir="$(cd -- "${script_dir}/.." && pwd)"
unity_data_dir="${1:-${UNITY_EDITOR_DIR:-}}"

# Keep SDK first-run and package-cache writes local so the build works in
# read-only-home CI environments as well as on developer machines.
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${package_dir}/.dotnet}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${package_dir}/.nuget/packages}"
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if [[ -z "${unity_data_dir}" ]]; then
  echo "Usage: $0 /path/to/Unity/Editor/Data" >&2
  echo "Alternatively set UNITY_EDITOR_DIR to the Editor/Data directory." >&2
  exit 2
fi

if [[ ! -f "${unity_data_dir}/Managed/UnityEditor.dll" ]]; then
  echo "UnityEditor.dll was not found below: ${unity_data_dir}" >&2
  exit 2
fi

projects=(
  "Nvh.UvTileScanner.Core.Editor"
  "Nvh.UvTileScanner.Poiyomi.Editor"
  "Nvh.UvTileScanner.Editor"
)

for project in "${projects[@]}"; do
  dotnet build "${package_dir}/Source~/${project}.csproj" \
    --configuration Release \
    -p:UnityEditorDir="${unity_data_dir}"
  cp "${package_dir}/Source~/bin/Release/netstandard2.1/${project}.dll" \
    "${package_dir}/Editor/${project}.dll"
  echo "Built ${package_dir}/Editor/${project}.dll"
done
