# SIQuester cross-platform packaging

The Avalonia release uses self-contained .NET 10 publishes. It does not replace the existing WPF MSI until feature parity is verified.

## Reproducible build input

Pass the commit timestamp as `SOURCE_DATE_EPOCH` so archive ownership, ordering, timestamps, and gzip headers are stable:

```bash
export SOURCE_DATE_EPOCH="$(git show -s --format=%ct HEAD)"
```

All package builders stage into a private temporary directory, validate their output, and atomically replace an existing artifact only after validation. Release artifacts include `LICENSE`, `THIRD_PARTY_NOTICES.md`, and a SHA-256 receipt. Trimming and single-file extraction are deliberately disabled until the UI and serialization graph have dedicated trim analysis.

## Linux tarballs and Debian packages

Build tools: .NET 10 SDK, GNU `tar`, `gzip`, `coreutils`, and `dpkg-deb` (normally provided by `dpkg-dev`).

```bash
dotnet restore src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj -r linux-x64
tools/package-siquester-linux.sh --rid linux-x64 --version 6.9.1 --output artifacts --no-restore

dotnet restore src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj -r linux-arm64
tools/package-siquester-linux.sh --rid linux-arm64 --version 6.9.1 --output artifacts --no-restore
```

The portable tarball runs directly:

```bash
tar -xzf artifacts/SIQuester-6.9.1-linux-x64.tar.gz
./SIQuester-6.9.1-linux-x64/SIQuester.Desktop package.siq
```

Install the Debian package with:

```bash
sudo apt install ./artifacts/siquester_6.9.1_amd64.deb
siquester package.siq
```

The Debian package installs immutable files under `/usr/lib/siquester`, a launcher under `/usr/bin`, the existing SIQuester icon under `/usr/share/icons/hicolor`, a desktop entry under `/usr/share/applications`, and `application/x-siq` metadata under `/usr/share/mime/packages`. It never installs mutable user data under `/usr`; configuration, data, cache, logs, recovery, templates, and temporary media use the per-user paths documented in `BUILDING.md`.

### Linux runtime dependencies

The Debian package declares the native .NET/Avalonia runtime set: `libc6`, `libfontconfig1`, `libgcc-s1`, `libice6`, a supported ICU runtime (`libicu70`, `libicu72`, `libicu74`, or `libicu76`), `libsm6`, `libssl3`, `libstdc++6`, `libx11-6`, and `zlib1g`. Equivalent base packages are required by portable tarball users:

- Debian/Ubuntu: the names above;
- Fedora: `fontconfig`, `libICE`, `libicu`, `libSM`, `openssl-libs`, `libstdc++`, `libX11`, and `zlib`;
- Arch: `fontconfig`, `libice`, `icu`, `libsm`, `openssl`, `gcc-libs`, `libx11`, and `zlib`.

Question preview uses the official MIT-licensed Avalonia WebView adapter and probes it only when requested. The Debian package therefore recommends, rather than requires, `libwebkit2gtk-4.1-0` (with `libwpewebkit-2.0-1` as an alternative). Portable users can install the GTK backend with:

- Debian/Ubuntu: `sudo apt install libwebkit2gtk-4.1-0`
- Fedora: `sudo dnf install webkit2gtk4.1`
- Arch: `sudo pacman -S webkit2gtk-4.1`

Missing WebKit leaves the editor usable and produces an actionable preview message. Distribution GStreamer plugins determine optional audio/video codec coverage; native audio/video preview is not yet claimed by the text-player receipt.

## macOS application bundles

Run each architecture build on a current macOS runner:

```bash
dotnet restore src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj -r osx-arm64
tools/package-siquester-macos.sh --rid osx-arm64 --version 6.9.1 --output artifacts --no-restore

dotnet restore src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj -r osx-x64
tools/package-siquester-macos.sh --rid osx-x64 --version 6.9.1 --output artifacts --no-restore
```

The builder creates `SIQuester.app`, registers `.siq` as an editable document type, generates an ICNS icon with system tools, performs ad-hoc signing for CI, verifies the signature and property list, then emits an architecture-specific tarball. Ad-hoc signing is not a substitute for release signing.

For a public release, a maintainer with an Apple Developer ID should sign the complete bundle and submit it for notarization before creating the final archive:

```bash
codesign --force --deep --options runtime --timestamp --sign "Developer ID Application: ..." SIQuester.app
ditto -c -k --keepParent SIQuester.app SIQuester.zip
xcrun notarytool submit SIQuester.zip --keychain-profile SI_NOTARY --wait
xcrun stapler staple SIQuester.app
codesign --verify --deep --strict --verbose=2 SIQuester.app
spctl --assess --type execute --verbose=2 SIQuester.app
```

Signing and notarization credentials are release-only and are not required for pull-request CI.

## Windows

Cross-platform CI publishes a self-contained Avalonia `win-x64` ZIP as an additional artifact. The established WPF build and MSI remain authoritative and are built by the existing Windows workflow; the Avalonia artifact does not replace or modify them.

## CI receipts

`.github/workflows/siquester-cross-platform.yml` builds and tests on Linux, macOS, and Windows, then publishes `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`, and `win-x64`. The Linux x64 package job also extracts and launches the self-contained tarball under Xvfb, opens a real SIQ, exits through the application command, and uploads its settings/log receipt. A dedicated Linux WebKit job installs WebKitGTK, requires the native capability path, renders and advances the retained player from the semantic compatibility SIQ, applies a deterministic screenshot-content threshold, and uploads the screenshot, log, and receipt.
