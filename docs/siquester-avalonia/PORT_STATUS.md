# SIQuester Avalonia port status

Updated: 2026-08-27

## Verified baseline

- Initial HEAD: `8c2bee9c38d250884a3e575809172e626448a0f9` on `feat/siquester-avalonia`; the worktree was initially clean.
- Host: Debian 13.6, Linux x64; .NET SDK 10.0.400 at `/tmp/dotnet10` for this session.
- Before modification, `SIPackages.Tests` had 85 passed and 5 skipped tests. `SIQuester.ViewModel.Tests` targeted `net10.0-windows` and discovered no tests on Linux.
- The initial view-model inventory contained 84 `PlatformManager.Instance` calls and 31 `async void` methods. The current counts are 67 and 27 respectively.
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
- Replaced legacy XML-plus-media-path autosave writes with complete validated SIQ recovery generations under platform state storage. Schema-1 metadata points atomically to an immutable flushed generation and records document identity, original path, timestamp, length, and SHA-256. Unsaved documents and all four media collections are covered; manual save and close serialize against autosave and clear recovery only after success.
- Added recovery startup inventory/restore, stale detection that requires a valid newer canonical SIQ, retention of malformed entries for diagnosis, and a 20-second owned Avalonia autosave timer. Legacy recovery remains readable during migration.
- Added a localized recent-files section to the Avalonia empty state with full-path tooltips and the existing `OpenRecent` command.

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
- `SIQuester.ViewModel.Tests`: 73 passed, 0 failed. Nine recovery tests cover unsaved/four-media snapshots, independent same-name documents, generation replacement, cancellation cleanup, autosave/manual-save/close races, startup restore, traversal rejection, and corrupt-newer-canonical handling. Clipboard and settings coverage remains green.
- `SIQuester.Avalonia.Tests`: 14 passed, 0 failed. Recent-file command/path rendering joins the native clipboard, shortcut, compiled-binding, settings, selection, and lifecycle coverage.
- Existing WPF project cross-compiled on Linux with `-p:EnableWindowsTargeting=true`: passed, 0 errors and 0 warnings in the final incremental compatibility build. Native WPF execution remains a Windows-only verification.
- Native Linux Release smoke: process stayed alive for a 12-second Xvfb window (expected timeout 124), then a second run exposed a visible `SIQuester` window at 1200x760, opened the compatibility package, and exited normally with code 0 through the application-owned `Ctrl+Q` path.
- Settings runtime receipt: `/tmp/siquester-settings-smoke.1LErfv/config/SIQuester/settings.json`, 1,492 bytes, SHA-256 `eb7dcd4289dbc07260886ddb6672cc02e8aec831b348d209345854340f651b9b`; the log records document close and `Application settings were committed successfully` with no fatal/unhandled exception.
- `tools/smoke-siquester-linux.sh` reproduces that graceful window/open/exit/settings/log receipt and passed locally under Xvfb; cross-platform CI invokes it after installing `xvfb` and `xdotool`.

## Compatibility artifact

- Test: `CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia`.
- Release output: `bin/AnyCPU.Release/SIQuester.ViewModel.Tests/net10.0/compatibility-artifacts/avalonia-core-roundtrip.siq`.
- Receipt: adjacent `avalonia-core-roundtrip.receipt.json`.
- Current artifact: 1,078 bytes; SHA-256 `8896d53215e3c3bdbfa22c982befc0f923478f593c67d5c00449773c72927394`. ZIP metadata can change this hash between generated runs; semantic and media receipts remain authoritative.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one image with semantic and byte comparison.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No external blocker for continued implementation.
- Native Windows WPF/Avalonia execution and macOS runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

1. Add a per-entry recovery workspace with preview, restore, discard, reveal-location, stale-entry controls, and a documented retention policy.
2. Add self-contained publish/package scripts and Linux tarball/`.deb` receipts.
3. Expand typed inspectors to all metadata, scenarios, parameters, answers, and media operations.
4. Extract cancellable search scheduling from `QDocument` and cover rapid switching/close races.
5. Replace clipboard media materialization paths with a bounded, stable-lifetime transfer representation and add media-rich cross-process compatibility coverage.

## Known limitations

- This is a verified first vertical slice plus settings, native clipboard, and complete recovery persistence foundations, not Milestone 2 completion. Per-entry recovery controls, clipboard media lifetime completion, full metadata/media editing, flat mode, preview, structural SPARD, advanced import/export, and packaging remain incomplete.
- Language changes intentionally apply after restart and communicate that boundary. System/light/dark selection is live and persisted, but visual theme snapshots and a macOS runtime Command-key receipt remain pending.
- WebView and audio/video backends have architectural decisions but no implementation receipt.
- No performance baseline has been measured yet.
- CI workflow syntax and commands are locally mirrored, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed implementation commit: `3eb1d61a` (complete autosave recovery snapshots and recent files). Full Release build/tests, WPF cross-build, diff checks, data-loss review, and native Linux open/exit smoke passed at this commit.
