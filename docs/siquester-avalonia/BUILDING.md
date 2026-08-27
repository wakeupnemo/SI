# Building SIQuester Cross-Platform

## Prerequisites

- .NET 10 SDK
- Linux desktop runtime libraries required by Avalonia (X11 or Wayland environment and standard font/configuration libraries)
- A native WebView backend for question preview. It is optional for startup and ordinary editing:
  - Debian/Ubuntu: `sudo apt install libwebkit2gtk-4.1-0`
  - Fedora: `sudo dnf install webkit2gtk4.1`
  - Arch: `sudo pacman -S webkit2gtk-4.1`
- Linux audio/video codec plugins for media-library and question playback:
  - Debian/Ubuntu: `sudo apt install gstreamer1.0-libav gstreamer1.0-plugins-base gstreamer1.0-plugins-good gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly`
  - Fedora: `sudo dnf install gstreamer1-libav gstreamer1-plugins-base gstreamer1-plugins-good gstreamer1-plugins-bad-free gstreamer1-plugins-ugly-free`
  - Arch: `sudo pacman -S gst-libav gst-plugins-base gst-plugins-good gst-plugins-bad gst-plugins-ugly`

The desktop host probes WebKit only when question or media preview is requested. If the runtime is absent, SIQuester shows an actionable localized message and does not attempt to navigate or fail application startup. Embedded image/audio/video references are streamed through per-session controlled loopback URLs; package HTML is not executed. The retained application page provides native HTML5 audio/video transport controls and tears playback down on selection, view, document, or application closure. Codec coverage depends on the distribution's GStreamer packages.

Packaged Linux builds start through the `siquester` launcher, which defaults
`WEBKIT_DISABLE_COMPOSITING_MODE` to `1` before the native apphost starts. This
works around blank WebKitGTK preview surfaces reproduced on Debian 13 with an
NVIDIA/GBM failure. An explicitly supplied value, including `0` or an empty
value, is always preserved. Direct `SIQuester.Desktop` launches retain a managed
fallback, but portable users should normally run `./siquester`.

The desktop host registers Avalonia's official Wayland backend after ordinary
platform detection. A usable Wayland compositor is selected natively; sessions
without one retain the X11 backend. Debian packages depend on
`libwayland-client0` and `libxkbcommon0`; equivalent Wayland client and XKB
runtime libraries are required by portable builds when using Wayland.

## Commands

```bash
dotnet restore SIQuester.CrossPlatform.sln -p:Configuration=Release
dotnet build SIQuester.CrossPlatform.sln --configuration Release --no-restore -m:1
dotnet test test/Common/SIPackages.Tests/SIPackages.Tests.csproj --configuration Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.ViewModel.Tests/SIQuester.ViewModel.Tests.csproj --configuration Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.Avalonia.Tests/SIQuester.Avalonia.Tests.csproj --configuration Release --no-build --no-restore -m:1
dotnet run --project src/SIQuester/SIQuester.Desktop/SIQuester.Desktop.csproj -- package.siq
```

`-m:1` is used in CI and in the recorded local receipts to avoid opaque MSBuild worker-process failures in constrained containers. Release restore explicitly sets `Configuration` because this repository intentionally keeps configuration-specific intermediate directories.

The existing WPF application remains in `SIQuester.sln` and is validated on Windows:

```powershell
dotnet build SIQuester.sln
dotnet test test/SIQuester/SIQuester.ViewModel.Tests/SIQuester.ViewModel.Tests.csproj
```

Do not use `SIQuester.sln` as the Linux/macOS build boundary because it intentionally includes WPF and WiX projects.

Self-contained Linux tar/DEB and macOS app-bundle commands, runtime dependencies, checksums, signing, and notarization are documented in [PACKAGING.md](PACKAGING.md).

## Settings locations

The Avalonia host stores versioned `settings.json` under platform conventions:

- Linux: `$XDG_CONFIG_HOME/SIQuester`, or `~/.config/SIQuester` when XDG is unset or relative;
- macOS: `~/Library/Application Support/SIQuester/Config`;
- Windows: `%LocalAppData%/SIQuester/config`.

Writes are staged and validated in the destination directory before replacement. A damaged existing file is retained as `settings.json.bak` (or a numbered variant) when defaults are committed. GPT/OpenAI keys are excluded; no plaintext fallback is implemented.

## Current local environment note

During the 2026-08-26/27 baseline session the host had no system `dotnet`; SDK 10.0.400 was installed under `/tmp/dotnet10`. This path is not a repository requirement. Commands in that session used:

```bash
DOTNET_CLI_HOME=/tmp/siquester-dotnet-home \
NUGET_PACKAGES=/tmp/siquester-nuget-packages \
/tmp/dotnet10/dotnet <command>
```
