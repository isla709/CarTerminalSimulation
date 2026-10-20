#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Debug}"
case "$configuration" in Debug|Release) ;; *) echo 'Usage: Build.sh [Debug|Release]' >&2; exit 2 ;; esac
project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
repository_root="$(cd -- "$project_root/.." && pwd)"
build_root="$repository_root/bin/$configuration/Flutter/linux"
source_root="$build_root/source"
credential_file="$repository_root/docs/KEY.txt"
embedded_credential="$source_root/assets/credentials/map-credential.bin"
rm -f -- "$embedded_credential"
trap 'rm -f -- "$embedded_credential"' EXIT
if [[ ! -f "$credential_file" ]]; then
  echo 'Map credential missing: provide your local untracked docs/KEY.txt.' >&2; exit 1
fi
if ! git -C "$repository_root" check-ignore --quiet -- docs/KEY.txt; then
  echo 'docs/KEY.txt must remain untracked and ignored by Git.' >&2; exit 1
fi
if [[ -d "$source_root" ]]; then
  while IFS= read -r -d '' map_directory; do
    case "$map_directory" in "$build_root"/source/*/assets/map|"$build_root"/source/assets/map|"$build_root"/source/*/assets/maps|"$build_root"/source/assets/maps) rm -rf -- "$map_directory";; *) echo 'Invalid map build-cache target' >&2; exit 1;; esac
  done < <(find "$source_root" -type d \( -path '*/assets/map' -o -path '*/assets/maps' \) -prune -print0)
fi
mkdir -p -- "$source_root/lib" "$source_root/linux" "$build_root/tool-config"
cp -R -- "$project_root/lib/." "$source_root/lib/"
cp -R -- "$project_root/linux/." "$source_root/linux/"
if [[ -d "$project_root/assets" ]]; then
  mkdir -p -- "$source_root/assets"
  cp -R -- "$project_root/assets/." "$source_root/assets/"
fi
cp -- "$project_root/pubspec.yaml" "$project_root/pubspec.lock" "$source_root/"
mkdir -p -- "$source_root/tool"
cp -- "$project_root/tool/embed_map_credentials.dart" "$source_root/tool/"
printf '%s\n' '{"build-dir":"build","enable-linux-desktop":true}' > "$build_root/tool-config/settings"
export XDG_CONFIG_HOME="$build_root/tool-config"
cd -- "$source_root"
if [[ -n "${FLUTTER_SDK:-}" ]]; then flutter_command="$FLUTTER_SDK/bin/flutter"; else flutter_command="$(command -v flutter)"; fi
"$flutter_command" pub get
flutter_command="$(readlink -f -- "$flutter_command")"
dart_command="$(dirname -- "$flutter_command")/cache/dart-sdk/bin/dart"
"$dart_command" --packages="$source_root/.dart_tool/package_config.json" "$source_root/tool/embed_map_credentials.dart" "$credential_file" "$embedded_credential"
"$flutter_command" build linux "--${configuration,,}"
printf 'Build outputs: %s/build\n' "$source_root"
