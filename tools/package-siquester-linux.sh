#!/usr/bin/env bash

set -euo pipefail

usage() {
  echo "Usage: $0 --rid <linux-x64|linux-arm64> --version <version> --output <directory> [--no-restore]" >&2
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

if [ "$rid" != "linux-x64" ] && [ "$rid" != "linux-arm64" ]; then
  usage
  exit 2
fi

if [[ ! "$version" =~ ^[0-9][0-9A-Za-z.+~-]*$ ]] || [ -z "$output_directory" ]; then
  usage
  exit 2
fi

for required_command in dotnet dpkg-deb gzip sha256sum tar; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $required_command" >&2
    exit 1
  fi
done

case "$rid" in
  linux-x64)
    debian_architecture="amd64"
    ;;
  linux-arm64)
    debian_architecture="arm64"
    ;;
esac

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "$script_directory/.." && pwd)"
output_directory="$(mkdir -p "$output_directory" && cd "$output_directory" && pwd)"
work_directory="$(mktemp -d "${TMPDIR:-/tmp}/siquester-linux-package.XXXXXX")"

publish_directory="$work_directory/publish"
portable_name="SIQuester-$version-$rid"
portable_directory="$work_directory/$portable_name"
debian_root="$work_directory/debian-root"
tarball_path="$output_directory/$portable_name.tar.gz"
debian_path="$output_directory/siquester_${version}_${debian_architecture}.deb"
checksum_path="$output_directory/$portable_name.SHA256SUMS"
tarball_candidate="$output_directory/.$portable_name.tar.gz.tmp.$$"
debian_candidate="$output_directory/.siquester_${version}_${debian_architecture}.deb.tmp.$$"
checksum_candidate="$output_directory/.$portable_name.SHA256SUMS.tmp.$$"

cleanup() {
  rm -f -- "$tarball_candidate" "$debian_candidate" "$checksum_candidate"
  rm -rf -- "$work_directory"
}

trap cleanup EXIT

publish_arguments=(
  publish "$repository_root/src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj"
  --configuration Release
  --runtime "$rid"
  --self-contained true
  --output "$publish_directory"
  -p:SIQuesterCrossPlatformVersion="$version"
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

if [ ! -x "$publish_directory/SIQuester.Desktop" ]; then
  echo "Publish did not produce a native SIQuester.Desktop executable for $rid." >&2
  exit 1
fi

mkdir -p "$portable_directory"
cp -a "$publish_directory/." "$portable_directory/"
install -m 0644 "$repository_root/LICENSE" "$portable_directory/LICENSE"
install -m 0644 "$repository_root/THIRD_PARTY_NOTICES.md" "$portable_directory/THIRD_PARTY_NOTICES.md"

source_date_epoch="${SOURCE_DATE_EPOCH:-0}"
if [[ ! "$source_date_epoch" =~ ^[0-9]+$ ]]; then
  echo "SOURCE_DATE_EPOCH must contain a non-negative integer." >&2
  exit 2
fi

find "$portable_directory" -exec touch -h -d "@$source_date_epoch" {} +
tar --sort=name --mtime="@$source_date_epoch" --owner=0 --group=0 --numeric-owner \
  --directory "$work_directory" --create --file=- "$portable_name" | gzip -n > "$tarball_candidate"

install -d \
  "$debian_root/DEBIAN" \
  "$debian_root/usr/bin" \
  "$debian_root/usr/lib/siquester" \
  "$debian_root/usr/share/applications" \
  "$debian_root/usr/share/doc/siquester" \
  "$debian_root/usr/share/icons/hicolor/64x64/apps" \
  "$debian_root/usr/share/mime/packages"
cp -a "$publish_directory/." "$debian_root/usr/lib/siquester/"
ln -s ../lib/siquester/SIQuester.Desktop "$debian_root/usr/bin/siquester"
install -m 0644 "$repository_root/deploy/siquester-desktop/linux/siquester.desktop" \
  "$debian_root/usr/share/applications/siquester.desktop"
install -m 0644 "$repository_root/deploy/siquester-desktop/linux/siquester.xml" \
  "$debian_root/usr/share/mime/packages/siquester.xml"
install -m 0644 "$repository_root/deploy/SIQuester.Bootstrapper/Resources/logo.png" \
  "$debian_root/usr/share/icons/hicolor/64x64/apps/siquester.png"
install -m 0644 "$repository_root/LICENSE" "$debian_root/usr/share/doc/siquester/copyright"
install -m 0644 "$repository_root/THIRD_PARTY_NOTICES.md" \
  "$debian_root/usr/share/doc/siquester/THIRD_PARTY_NOTICES.md"
install -m 0755 "$repository_root/deploy/siquester-desktop/linux/postinst" "$debian_root/DEBIAN/postinst"
install -m 0755 "$repository_root/deploy/siquester-desktop/linux/postrm" "$debian_root/DEBIAN/postrm"

installed_size="$(du -sk "$debian_root/usr" | awk '{print $1}')"
sed \
  -e "s/@VERSION@/$version/g" \
  -e "s/@ARCHITECTURE@/$debian_architecture/g" \
  -e "s/@INSTALLED_SIZE@/$installed_size/g" \
  "$repository_root/deploy/siquester-desktop/linux/control.template" \
  > "$debian_root/DEBIAN/control"

find "$debian_root" -exec touch -h -d "@$source_date_epoch" {} +
SOURCE_DATE_EPOCH="$source_date_epoch" dpkg-deb --root-owner-group --build "$debian_root" "$debian_candidate"

if [ "$(dpkg-deb --field "$debian_candidate" Package)" != "siquester" ] \
  || [ "$(dpkg-deb --field "$debian_candidate" Version)" != "$version" ] \
  || [ "$(dpkg-deb --field "$debian_candidate" Architecture)" != "$debian_architecture" ]; then
  echo "Generated Debian metadata does not match the requested package." >&2
  exit 1
fi

dpkg-deb --contents "$debian_candidate" > "$work_directory/debian-contents.txt"
awk '{print $6}' "$work_directory/debian-contents.txt" > "$work_directory/debian-paths.txt"

for required_path in \
  ./usr/bin/siquester \
  ./usr/lib/siquester/SIQuester.Desktop \
  ./usr/share/applications/siquester.desktop \
  ./usr/share/icons/hicolor/64x64/apps/siquester.png \
  ./usr/share/mime/packages/siquester.xml; do
  if ! grep -Fqx "$required_path" "$work_directory/debian-paths.txt"; then
    echo "Debian package is missing $required_path." >&2
    exit 1
  fi
done

if find "$debian_root/usr" -type d \( -name config -o -name data -o -name cache -o -name logs -o -name recovery \) -print -quit | grep -q .; then
  echo "Debian package unexpectedly contains a mutable user-state directory." >&2
  exit 1
fi

mv -f -- "$tarball_candidate" "$tarball_path"
mv -f -- "$debian_candidate" "$debian_path"

(
  cd "$output_directory"
  sha256sum "$(basename "$tarball_path")" "$(basename "$debian_path")" > "$(basename "$checksum_candidate")"
)
mv -f -- "$checksum_candidate" "$checksum_path"

echo "Created $tarball_path"
echo "Created $debian_path"
echo "Created $checksum_path"
