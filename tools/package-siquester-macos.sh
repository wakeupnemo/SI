#!/usr/bin/env bash

set -euo pipefail

usage() {
  echo "Usage: $0 --rid <osx-x64|osx-arm64> --version <version> --output <directory> [--no-restore]" >&2
}

rid=""
version=""
output_directory=""
no_restore=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --rid)
      rid="${2:-}"
      shift 2
      ;;
    --version)
      version="${2:-}"
      shift 2
      ;;
    --output)
      output_directory="${2:-}"
      shift 2
      ;;
    --no-restore)
      no_restore=true
      shift
      ;;
    *)
      usage
      exit 2
      ;;
  esac
done

if [ "$rid" != "osx-x64" ] && [ "$rid" != "osx-arm64" ]; then
  usage
  exit 2
fi

if [[ ! "$version" =~ ^[0-9]+([.][0-9]+){1,2}$ ]] || [ -z "$output_directory" ]; then
  usage
  exit 2
fi

for required_command in dotnet gzip shasum tar; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $required_command" >&2
    exit 1
  fi
done

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/.." && pwd)"
output_directory="$(mkdir -p "$output_directory" && cd "$output_directory" && pwd)"
work_directory="$(mktemp -d "${TMPDIR:-/tmp}/siquester-macos-package.XXXXXX")"

publish_directory="$work_directory/publish"
application_bundle="$work_directory/SIQuester.app"
macos_directory="$application_bundle/Contents/MacOS"
resources_directory="$application_bundle/Contents/Resources"
archive_path="$output_directory/SIQuester-$version-$rid.app.tar.gz"
checksum_path="$output_directory/SIQuester-$version-$rid.SHA256SUMS"
archive_candidate="$output_directory/.SIQuester-$version-$rid.app.tar.gz.tmp.$$"
checksum_candidate="$output_directory/.SIQuester-$version-$rid.SHA256SUMS.tmp.$$"

cleanup() {
  rm -f -- "$archive_candidate" "$checksum_candidate"
  rm -rf -- "$work_directory"
}

trap cleanup EXIT

publish_arguments=(
  publish "$repository_root/src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj"
  --configuration Release
  --runtime "$rid"
  --self-contained true
  --output "$publish_directory"
  -p:Version="$version"
  -p:PublishSingleFile=false
  -p:PublishTrimmed=false
  -p:DebugSymbols=false
  -p:DebugType=None
  -p:UseAppHost=true
)

if [ "$no_restore" = true ]; then
  publish_arguments+=(--no-restore)
fi

dotnet "${publish_arguments[@]}"

if [ ! -f "$publish_directory/SIQuester.Desktop" ]; then
  echo "Publish did not produce a SIQuester.Desktop host for $rid." >&2
  exit 1
fi

install -d "$macos_directory" "$resources_directory"
cp -a "$publish_directory/." "$macos_directory/"
chmod 0755 "$macos_directory/SIQuester.Desktop"
install -m 0644 "$repository_root/LICENSE" "$resources_directory/LICENSE"
install -m 0644 "$repository_root/THIRD_PARTY_NOTICES.md" "$resources_directory/THIRD_PARTY_NOTICES.md"

icon_file="SIQuester.png"
install -m 0644 "$repository_root/deploy/SIQuester.Bootstrapper/Resources/logo.png" \
  "$resources_directory/$icon_file"

if command -v iconutil >/dev/null 2>&1 && command -v sips >/dev/null 2>&1; then
  iconset_directory="$work_directory/SIQuester.iconset"
  install -d "$iconset_directory"
  for icon_size in 16 32 128 256 512; do
    doubled_size=$((icon_size * 2))
    sips -z "$icon_size" "$icon_size" \
      "$repository_root/deploy/SIQuester.Bootstrapper/Resources/logo.png" \
      --out "$iconset_directory/icon_${icon_size}x${icon_size}.png" >/dev/null
    sips -z "$doubled_size" "$doubled_size" \
      "$repository_root/deploy/SIQuester.Bootstrapper/Resources/logo.png" \
      --out "$iconset_directory/icon_${icon_size}x${icon_size}@2x.png" >/dev/null
  done
  iconutil --convert icns "$iconset_directory" --output "$resources_directory/SIQuester.icns"
  rm "$resources_directory/$icon_file"
  icon_file="SIQuester.icns"
fi

sed \
  -e "s/@VERSION@/$version/g" \
  -e "s/@ICON_FILE@/$icon_file/g" \
  "$repository_root/deploy/siquester-desktop/macos/Info.plist.template" \
  > "$application_bundle/Contents/Info.plist"

if [ "$(uname -s)" = "Darwin" ] && command -v plutil >/dev/null 2>&1; then
  plutil -lint "$application_bundle/Contents/Info.plist"
fi

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$application_bundle"
  codesign --verify --deep --strict "$application_bundle"
fi

source_date_epoch="${SOURCE_DATE_EPOCH:-0}"
if [[ ! "$source_date_epoch" =~ ^[0-9]+$ ]]; then
  echo "SOURCE_DATE_EPOCH must contain a non-negative integer." >&2
  exit 2
fi

if date --version >/dev/null 2>&1; then
  find "$application_bundle" -exec touch -h -d "@$source_date_epoch" {} +
else
  touch_timestamp="$(date -u -r "$source_date_epoch" +%Y%m%d%H%M.%S)"
  find "$application_bundle" -exec touch -h -t "$touch_timestamp" {} +
fi

if command -v codesign >/dev/null 2>&1; then
  codesign --verify --deep --strict "$application_bundle"
fi

tar_version="$(tar --version)"
if [[ "$tar_version" == *GNU* ]]; then
  tar --sort=name --mtime="@$source_date_epoch" --owner=0 --group=0 --numeric-owner \
    --directory "$work_directory" --create --file=- SIQuester.app | gzip -n > "$archive_candidate"
else
  (
    cd "$work_directory"
    find SIQuester.app -print | LC_ALL=C sort > archive-files.txt
    tar --uid 0 --gid 0 --uname root --gname wheel --no-xattrs \
      --no-recursion --create --file=- --files-from=archive-files.txt
  ) | gzip -n > "$archive_candidate"
fi

mv -f -- "$archive_candidate" "$archive_path"

(
  cd "$output_directory"
  shasum -a 256 "$(basename "$archive_path")" > "$(basename "$checksum_candidate")"
)
mv -f -- "$checksum_candidate" "$checksum_path"

echo "Created $archive_path"
echo "Created $checksum_path"
