# SIQuester Avalonia port status

Updated: 2026-08-27

## Verified baseline

- Initial HEAD: `8c2bee9c38d250884a3e575809172e626448a0f9` on `feat/siquester-avalonia`; the worktree was initially clean.
- Host: Debian 13.6, Linux x64; .NET SDK 10.0.400 at `/tmp/dotnet10` for this session.
- Before modification, `SIPackages.Tests` had 85 passed and 5 skipped tests. `SIQuester.ViewModel.Tests` targeted `net10.0-windows` and discovered no tests on Linux.
- The initial view-model inventory contained 84 `PlatformManager.Instance` calls and 31 `async void` methods. The current counts are 67 and 28 respectively.
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
- Replaced the Avalonia session-only clipboard with typed native text, Unicode file-list, PNG, and custom-format transfer. SIQuester item/package payloads now use a bounded version-1 JSON envelope with explicit kind/version checks while writing and reading the legacy WPF formats during migration. Copy/paste works across documents; Cut removes only after a successful clipboard write. The WPF adapter implements the same neutral contract.
- Fixed an adversarially discovered shortcut defect where window-level Ctrl+C intercepted a focused text editor. Routed document shortcuts now yield to text controls and support Control or macOS Command modifiers; headless tests cover both document routing and text-editing isolation.

## Current verified commands and results

Run with the system SDK or prepend the temporary SDK environment shown in `BUILDING.md`.

```bash
dotnet restore SIQuester.CrossPlatform.sln -p:Configuration=Release -m:1
dotnet build SIQuester.CrossPlatform.sln -c Release --no-restore -m:1
dotnet test test/Common/SIPackages.Tests/SIPackages.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.ViewModel.Tests/SIQuester.ViewModel.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.Avalonia.Tests/SIQuester.Avalonia.Tests.csproj -c Release --no-build --no-restore -m:1
```

- Cross-platform Release build: passed, 0 errors and 0 warnings. Every portable project emitted to `bin/AnyCPU.Release`; adding the complete dependency graph fixed an adversarially discovered mixed Debug/Release evaluation.
- `SIPackages.Tests`: 86 passed, 5 skipped, 0 failed.
- `SIQuester.ViewModel.Tests`: 64 passed, 0 failed. Ten clipboard tests cover versioning, legacy fallback, defensive typed values, cross-document copy/paste, cut failure preservation, package metadata, and SPARD text commands; nine named `SettingsStoreTests` cover missing/current/legacy/corrupt/invalid/future-schema/cancelled/replacement paths.
- `SIQuester.Avalonia.Tests`: 13 passed, 0 failed. Native headless clipboard coverage includes text, a Unicode file path, lossless PNG bytes, and custom data; shortcut tests cover document Ctrl+C and focused-text isolation.
- Existing WPF project cross-compiled on Linux with `-p:EnableWindowsTargeting=true`: passed, 0 errors and 0 warnings in the final incremental compatibility build. Native WPF execution remains a Windows-only verification.
- Native Linux Release smoke: process stayed alive for a 12-second Xvfb window (expected timeout 124), then a second run exposed a visible `SIQuester` window at 1200x760, opened the compatibility package, and exited normally with code 0 through the application-owned `Ctrl+Q` path.
- Settings runtime receipt: `/tmp/siquester-settings-smoke.1LErfv/config/SIQuester/settings.json`, 1,492 bytes, SHA-256 `eb7dcd4289dbc07260886ddb6672cc02e8aec831b348d209345854340f651b9b`; the log records document close and `Application settings were committed successfully` with no fatal/unhandled exception.
- `tools/smoke-siquester-linux.sh` reproduces that graceful window/open/exit/settings/log receipt and passed locally under Xvfb; cross-platform CI invokes it after installing `xvfb` and `xdotool`.

## Compatibility artifact

- Test: `CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia`.
- Release output: `bin/AnyCPU.Release/SIQuester.ViewModel.Tests/net10.0/compatibility-artifacts/avalonia-core-roundtrip.siq`.
- Receipt: adjacent `avalonia-core-roundtrip.receipt.json`.
- Current artifact: 1,078 bytes; SHA-256 `4551e2eae33c3110723f60da750d2ff9f6145b1926a1ecd38b76e2c4a534b882`. ZIP metadata can change this hash between generated runs; semantic and media receipts remain authoritative.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one image with semantic and byte comparison.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No external blocker for continued implementation.
- Native Windows WPF/Avalonia execution and macOS runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

1. Add recent-file UI plus serialized autosave/recovery and save/close race tests.
2. Expand typed inspectors to all metadata, scenarios, parameters, answers, and media operations.
3. Add self-contained publish/package scripts and Linux tarball/`.deb` receipts.
4. Extract cancellable search scheduling from `QDocument` and cover rapid switching/close races.
5. Replace clipboard media materialization paths with a bounded, stable-lifetime transfer representation and add media-rich cross-process compatibility coverage.

## Known limitations

- This is a verified first vertical slice plus settings and native clipboard foundations, not Milestone 2 completion. Recent-file UI, recovery, clipboard media lifetime completion, full metadata/media editing, flat mode, preview, structural SPARD, advanced import/export, and packaging remain incomplete.
- Language changes intentionally apply after restart and communicate that boundary. System/light/dark selection is live and persisted, but visual theme snapshots and a macOS runtime Command-key receipt remain pending.
- WebView and audio/video backends have architectural decisions but no implementation receipt.
- No performance baseline has been measured yet.
- CI workflow syntax and commands are locally mirrored, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed implementation commit: `8d9ccb0c` (typed cross-platform clipboard). Full Release build/tests, WPF cross-build, diff checks, data-loss review, and native Linux open/exit smoke passed at this commit.
