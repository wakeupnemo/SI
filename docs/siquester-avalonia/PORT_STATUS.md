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
- Added repository-owned self-contained package builders for Linux x64/ARM64 tarballs and Debian packages plus macOS x64/ARM64 app bundles. Output is staged privately, validated before atomic publication, normalized with `SOURCE_DATE_EPOCH`, shipped with license notices and checksums, and integrated into a five-RID CI artifact matrix. Linux packages include desktop/MIME registration and complete .NET/Avalonia native dependency metadata; macOS bundles declare editable SIQ document types.
- Added the native SIQuester icon to the Avalonia window and platform artifacts. The retained WPF release workflow now uses .NET 10 and current official action majors without changing the WPF/MSI authority boundary.

## Current verified commands and results

Run with the system SDK or prepend the temporary SDK environment shown in `BUILDING.md`.

```bash
dotnet restore SIQuester.CrossPlatform.sln -p:Configuration=Release -m:1
dotnet build SIQuester.CrossPlatform.sln -c Release --no-restore -m:1
dotnet test test/Common/SIPackages.Tests/SIPackages.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.ViewModel.Tests/SIQuester.ViewModel.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.Avalonia.Tests/SIQuester.Avalonia.Tests.csproj -c Release --no-build --no-restore -m:1
```

- Fresh cross-platform Release build: passed with 0 errors and 130 existing nullable/obsolete-API warnings in `SIPackages`, `QTxtConverter`, and legacy view-model code. An incremental build can report 0 warnings because those projects are not recompiled; analyzers and warnings remain enabled. Every portable project emitted to `bin/AnyCPU.Release`.
- `SIPackages.Tests`: 86 passed, 5 skipped, 0 failed.
- `SIQuester.ViewModel.Tests`: 73 passed, 0 failed. Nine recovery tests cover unsaved/four-media snapshots, independent same-name documents, generation replacement, cancellation cleanup, autosave/manual-save/close races, startup restore, traversal rejection, and corrupt-newer-canonical handling. Clipboard and settings coverage remains green.
- `SIQuester.Avalonia.Tests`: 14 passed, 0 failed. Recent-file command/path rendering joins the native clipboard, shortcut, compiled-binding, settings, selection, and lifecycle coverage.
- Existing WPF project cross-compiled on Linux with `-p:EnableWindowsTargeting=true`: passed, 0 errors and 0 warnings in the final incremental compatibility build. Native WPF execution remains a Windows-only verification.
- Native Linux Release smoke: the self-contained x64 tar payload and the identical executable extracted from the Debian package exposed a visible `SIQuester` window at 1200x760, opened `SIGameTestNew.siq`, and exited normally through the application-owned `Ctrl+Q` path. Both logs record successful open/settings commit and no fatal or unhandled exception.
- Settings runtime receipt: `/tmp/siquester-settings-smoke.1LErfv/config/SIQuester/settings.json`, 1,492 bytes, SHA-256 `eb7dcd4289dbc07260886ddb6672cc02e8aec831b348d209345854340f651b9b`; the log records document close and `Application settings were committed successfully` with no fatal/unhandled exception.
- `tools/smoke-siquester-linux.sh` now accepts either a framework-dependent DLL or a self-contained native executable. It reproduced the graceful window/open/exit/settings/log receipt under Xvfb; cross-platform CI invokes it for both the ordinary Release build and packaged x64 tarball.

## Release artifacts

All artifacts below were built from the reviewed `29ad371e` implementation tree with that commit's timestamp (`1787787265`) as `SOURCE_DATE_EPOCH`; they are local receipts, not hosted GitHub releases.

- Linux x64 tar: `/tmp/siquester-package-repro-a/SIQuester-6.9.1-linux-x64.tar.gz`, 57,808,542 bytes, SHA-256 `8c98fc07fed1cfa45a9892f8f60e91e8d409cd4c4f9a471bc51718e07c2c1bce`.
- Debian amd64: `/tmp/siquester-package-repro-a/siquester_6.9.1_amd64.deb`, 40,676,544 bytes, SHA-256 `688b749d3e2e7f65118713726150df883647951573ca43d2d7dbfecd5247b52c`.
- Linux ARM64 tar: `/tmp/siquester-package-arm64/SIQuester-6.9.1-linux-arm64.tar.gz`, 55,351,351 bytes, SHA-256 `337bfabe5e4bc3f58df038f29d073619814f3ba10367421bb0101e530cf4c1ec`.
- Debian arm64: `/tmp/siquester-package-arm64/siquester_6.9.1_arm64.deb`, 37,369,472 bytes, SHA-256 `eff1142cf90aee16fd66b99915198d1046d1559ad86232babf1cee96dce5a1f8`.
- macOS ARM64 app archive: `/tmp/siquester-package-osx-arm64/SIQuester-6.9.1-osx-arm64.app.tar.gz`, 57,463,620 bytes, SHA-256 `48f1381e15b14579a41954a11652885f8d34058bb33b2bcea5b20a807c2b63c1`.
- macOS x64 app archive: `/tmp/siquester-package-osx-x64/SIQuester-6.9.1-osx-x64.app.tar.gz`, 59,702,421 bytes, SHA-256 `bdc08c45deac1b51015ac15e80c3bece1c5a52fb16025d31a7c5d8a22f114ae3`.

Two independent final-source x64 builds are byte-identical by `cmp` for both tar and DEB. All six checksum receipts pass. `file` identifies the expected x86-64/AArch64 ELF and x86-64/ARM64 Mach-O hosts; both macOS property lists are valid XML and declare `.siq` plus `application/x-siq`. `apt-get --simulate install` accepts the amd64 DEB and its dependency alternatives on Debian 13. The tar and DEB contain the same executable bytes (SHA-256 `5ec8318c2b70972758e055ba15b6568e5d4b75bceeac8ad2f94f89fceca50a6d`), tying both final artifacts to the Linux runtime smoke.

The macOS archives were structurally cross-published on Linux. Native ICNS generation, ad-hoc `codesign`, Gatekeeper behavior, and application launch remain unverified until the hosted macOS job runs.

## Compatibility artifact

- Test: `CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia`.
- Release output: `bin/AnyCPU.Release/SIQuester.ViewModel.Tests/net10.0/compatibility-artifacts/avalonia-core-roundtrip.siq`.
- Receipt: adjacent `avalonia-core-roundtrip.receipt.json`.
- Current artifact: 1,078 bytes; SHA-256 `e8251135107b87f71f81c3b6fa32ec35ba5e732304ea9c3798da1a48f0d68659`. ZIP metadata can change this hash between generated runs; semantic and media receipts remain authoritative.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one image with semantic and byte comparison.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No external blocker for continued implementation.
- Native Windows WPF/Avalonia execution, hosted package publication, and macOS signing/runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

1. Add a per-entry recovery workspace with preview, restore, discard, reveal-location, stale-entry controls, and a documented retention policy.
2. Expand typed inspectors to all metadata, scenarios, parameters, answers, and media operations.
3. Extract cancellable search scheduling from `QDocument` and cover rapid switching/close races.
4. Replace clipboard media materialization paths with a bounded, stable-lifetime transfer representation and add media-rich cross-process compatibility coverage.
5. Obtain hosted Linux/macOS/Windows CI receipts, including native macOS bundle signing/launch and native Windows WPF acceptance.

## Known limitations

- This is a verified first vertical slice plus settings, native clipboard, complete recovery persistence, and release packaging foundations, not Milestone 2 completion. Per-entry recovery controls, clipboard media lifetime completion, full metadata/media editing, flat mode, preview, structural SPARD, and advanced import/export remain incomplete.
- Language changes intentionally apply after restart and communicate that boundary. System/light/dark selection is live and persisted, but visual theme snapshots and a macOS runtime Command-key receipt remain pending.
- WebView and audio/video backends have architectural decisions but no implementation receipt.
- No performance baseline has been measured yet.
- Linux packages are locally executable and structurally accepted, but actual desktop/MIME cache registration after system install and native ARM64 launch remain pending. macOS bundles are cross-built only; ICNS/codesign and native launch are hosted-runner boundaries.
- CI workflow YAML and commands are locally validated, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed implementation commit: `29ad371e` (reproducible cross-platform packages and five-RID CI). Fresh Release build, 173 passing tests plus 5 explicit skips, WPF cross-build, workflow/XML/desktop validators, full diff/data-loss/dependency review, byte-reproducibility checks, and self-contained tar/DEB Linux smoke passed for this implementation tree.
