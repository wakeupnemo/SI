# Building SIQuester Cross-Platform

## Prerequisites

- .NET 10 SDK
- Linux desktop runtime libraries required by Avalonia (X11 or Wayland environment and standard font/configuration libraries)
- WebKit/media libraries are optional until preview is invoked; exact packages will be finalized with the preview spike.

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

## Current local environment note

During the 2026-08-26/27 baseline session the host had no system `dotnet`; SDK 10.0.400 was installed under `/tmp/dotnet10`. This path is not a repository requirement. Commands in that session used:

```bash
DOTNET_CLI_HOME=/tmp/siquester-dotnet-home \
NUGET_PACKAGES=/tmp/siquester-nuget-packages \
/tmp/dotnet10/dotnet <command>
```
