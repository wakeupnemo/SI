# SIQuester Avalonia port status

Updated: 2026-08-27

## Verified baseline

- Initial HEAD: `8c2bee9c38d250884a3e575809172e626448a0f9` on `feat/siquester-avalonia`; the worktree was initially clean.
- Host: Debian 13.6, Linux x64; .NET SDK 10.0.400 at `/tmp/dotnet10` for this session.
- Before modification, `SIPackages.Tests` had 85 passed and 5 skipped tests. `SIQuester.ViewModel.Tests` targeted `net10.0-windows` and discovered no tests on Linux.
- The initial view-model inventory contained 84 `PlatformManager.Instance` calls and 31 `async void` methods. The current counts are 71 and 28 respectively.
- Latest stable compatible Avalonia packages were verified and pinned at 12.1.1.

## Completed vertical slices

- Added `SIQuester.CrossPlatform.sln`, including the complete portable project-reference graph, and separated reusable Avalonia UI, desktop host, and headless tests while retaining the WPF application.
- Added a native Fluent three-pane editor shell with tabs, empty state, hierarchy, package/round/theme/question inspectors, common field editing, add/delete commands, media counts, compiled bindings, English/Russian resources, and keyboard bindings for primary commands.
- Migrated New/Open/Save/Save As/Save All/Close orchestration to injected neutral picker, dialog, lifetime, persistence, and media-materialization services. The Avalonia path does not initialize the legacy platform global.
- Replaced destructive save behavior with same-directory staging, flush, `SIDocument.Load` validation, atomic replacement where available, rollback/backup fallback, final reload, and commit-only media acceptance.
- Retargeted view-model tests to `net10.0`; added semantic/media/Unicode/cancellation compatibility coverage and Avalonia headless selection/lifecycle coverage.
- Fixed a discovered `Package.ReadXml` defect that skipped the first package section after `<global>`; a regression fixture covers metadata, tags, and rounds.
- Added XDG/macOS/Windows application paths, bounded file logging, startup diagnostics, WPF adapters, third-party notices, and a Linux/macOS/Windows CI build/test matrix with a Linux launch smoke.

## Current verified commands and results

Run with the system SDK or prepend the temporary SDK environment shown in `BUILDING.md`.

```bash
dotnet restore SIQuester.CrossPlatform.sln -p:Configuration=Release -m:1
dotnet build SIQuester.CrossPlatform.sln -c Release --no-restore -m:1
dotnet test test/Common/SIPackages.Tests/SIPackages.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.ViewModel.Tests/SIQuester.ViewModel.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.Avalonia.Tests/SIQuester.Avalonia.Tests.csproj -c Release --no-build --no-restore -m:1
```

- Cross-platform Release build: passed, 0 errors, 130 existing source/dependency warnings. Every portable project emitted to `bin/AnyCPU.Release`; adding the complete dependency graph fixed an adversarially discovered mixed Debug/Release evaluation.
- `SIPackages.Tests`: 86 passed, 5 skipped, 0 failed.
- `SIQuester.ViewModel.Tests`: 45 passed, 0 failed.
- `SIQuester.Avalonia.Tests`: 8 passed, 0 failed.
- Existing WPF project cross-compiled on Linux with `-p:EnableWindowsTargeting=true`: passed, 0 errors and 0 warnings in the final incremental compatibility build. Native WPF execution remains a Windows-only verification.
- Native Linux Release smoke: process stayed alive for the full 12-second Xvfb window (expected timeout 124); `/tmp/siquester-m1-final.Gz1Odr/state/SIQuester/logs/siquester.log` contained no fatal/unhandled exception.
- Native Linux open receipt: `avalonia-core-roundtrip.siq` opened successfully from the command line; an untouched-startup screenshot verified the expanded hierarchy, selected package, typed inspector, and image count.

## Compatibility artifact

- Test: `CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia`.
- Release output: `bin/AnyCPU.Release/SIQuester.ViewModel.Tests/net10.0/compatibility-artifacts/avalonia-core-roundtrip.siq`.
- Receipt: adjacent `avalonia-core-roundtrip.receipt.json`.
- Current artifact: 1,078 bytes; SHA-256 `8ec69c72689e7afa39ae8c7c9eb3d1ded37bf0a2b612dc861df6823f378fa07e`.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one image with semantic and byte comparison.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No external blocker for continued implementation.
- Native Windows WPF/Avalonia execution and macOS runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

1. Implement versioned atomic JSON settings, persisted theme/language/pane state, and tests for missing/corrupt/older files.
2. Replace the temporary in-memory Avalonia clipboard adapter with typed native MIME payloads and versioned SIQuester serialization.
3. Add recent files plus serialized autosave/recovery and save/close race tests.
4. Expand typed inspectors to all metadata, scenarios, parameters, answers, and media operations.
5. Add self-contained publish/package scripts and Linux tarball/`.deb` receipts.

## Known limitations

- This is a verified first vertical slice, not Milestone 2 completion. Theme/language selection, settings persistence, recent files, recovery, native clipboard, full metadata/media editing, flat mode, preview, SPARD, advanced import/export, and packaging remain incomplete.
- WebView and audio/video backends have architectural decisions but no implementation receipt.
- No performance baseline has been measured yet.
- CI workflow syntax and commands are locally mirrored, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed source state: `48cd9a74` (`c30ab886` foundation plus native vertical slice). This status/CI update records the verified receipts for those commits.
