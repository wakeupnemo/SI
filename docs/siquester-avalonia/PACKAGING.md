# SIQuester cross-platform packaging

## Required artifacts

- Linux: self-contained `linux-x64` and `linux-arm64` tarballs plus `.deb`.
- macOS: `osx-arm64` and `osx-x64` `.app` bundles; unsigned/ad-hoc CI artifacts and documented release signing.
- Windows: optional Avalonia `win-x64` artifact. The existing WPF MSI remains authoritative until verified parity.

## Linux layout

The Debian package will install immutable application files under `/usr/lib/siquester`, a launcher under `/usr/bin`, icons under `/usr/share/icons/hicolor`, a desktop entry under `/usr/share/applications`, and SIQ MIME metadata under `/usr/share/mime/packages`. Mutable configuration, data, cache, logs, recovery, and templates remain in XDG user directories.

The desktop entry must advertise `%F` file opening and `application/x-siq`. Package scripts must update MIME/desktop caches without deleting unrelated data.

## Native dependencies

Core editing must launch without WebView or media backends. Preview packages will be documented by distribution after the backend spike. Missing optional native libraries must disable only the affected capability and show an actionable diagnostic.

## Licenses

Artifacts include the repository license and `THIRD_PARTY_NOTICES.md`. New distributed dependencies must be reviewed for license, maintenance, platform support, and native requirements before packaging.
