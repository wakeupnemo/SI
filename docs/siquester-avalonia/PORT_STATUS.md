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
- Added versioned atomic JSON settings with same-directory staging and validation, future-schema protection, legacy WPF migration, unknown-field preservation, secret stripping, recoverable corrupt-file backup, and serialized writes. System/light/dark choice, restart-applied language choice, and navigator/inspector widths are wired to the Avalonia UI.

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
- `SIQuester.ViewModel.Tests`: 54 passed, 0 failed. Nine named `SettingsStoreTests` cover missing/current/legacy/corrupt/invalid/future-schema/cancelled/replacement paths.
- `SIQuester.Avalonia.Tests`: 10 passed, 0 failed, including settings compiled bindings and single-execution close persistence.
- Existing WPF project cross-compiled on Linux with `-p:EnableWindowsTargeting=true`: passed, 0 errors and 0 warnings in the final incremental compatibility build. Native WPF execution remains a Windows-only verification.
- Native Linux Release smoke: process stayed alive for a 12-second Xvfb window (expected timeout 124), then a second run exposed a visible `SIQuester` window at 1200x760, opened the compatibility package, and exited normally with code 0 through the application-owned `Ctrl+Q` path.
- Settings runtime receipt: `/tmp/siquester-settings-smoke.1LErfv/config/SIQuester/settings.json`, 1,492 bytes, SHA-256 `eb7dcd4289dbc07260886ddb6672cc02e8aec831b348d209345854340f651b9b`; the log records document close and `Application settings were committed successfully` with no fatal/unhandled exception.
- `tools/smoke-siquester-linux.sh` reproduces that graceful window/open/exit/settings/log receipt and passed locally under Xvfb; cross-platform CI invokes it after installing `xvfb` and `xdotool`.

## Compatibility artifact

- Test: `CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia`.
- Release output: `bin/AnyCPU.Release/SIQuester.ViewModel.Tests/net10.0/compatibility-artifacts/avalonia-core-roundtrip.siq`.
- Receipt: adjacent `avalonia-core-roundtrip.receipt.json`.
- Current artifact: 1,078 bytes; SHA-256 `ef79d8e74c14df033ac398272e4eadb8681039f3f6095d2b7e444b6f5ea4264b`. ZIP metadata can change this hash between generated runs; semantic and media receipts remain authoritative.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one image with semantic and byte comparison.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No external blocker for continued implementation.
- Native Windows WPF/Avalonia execution and macOS runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

1. Replace the temporary in-memory Avalonia clipboard adapter with typed native MIME payloads and versioned SIQuester serialization.
2. Add recent-file UI plus serialized autosave/recovery and save/close race tests.
3. Expand typed inspectors to all metadata, scenarios, parameters, answers, and media operations.
4. Add self-contained publish/package scripts and Linux tarball/`.deb` receipts.
5. Extract cancellable search scheduling from `QDocument` and cover rapid switching/close races.

## Known limitations

- This is a verified first vertical slice plus its settings foundation, not Milestone 2 completion. Recent-file UI, recovery, native clipboard, full metadata/media editing, flat mode, preview, SPARD, advanced import/export, and packaging remain incomplete.
- Language changes intentionally apply after restart and communicate that boundary. System/light/dark selection is live and persisted, but visual theme snapshots and macOS Command-key mapping remain pending.
- WebView and audio/video backends have architectural decisions but no implementation receipt.
- No performance baseline has been measured yet.
- CI workflow syntax and commands are locally mirrored, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed implementation commit: `5e934ff6` (`c30ab886` foundation, `48cd9a74` native vertical slice, and atomic settings). The later durable-state/CI smoke commit contains no application code.
