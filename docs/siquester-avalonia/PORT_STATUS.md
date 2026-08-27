# SIQuester Avalonia port status

Updated: 2026-08-27

## Verified baseline

- Initial HEAD: `8c2bee9c38d250884a3e575809172e626448a0f9` on `feat/siquester-avalonia`; the worktree was initially clean.
- Host: Debian 13.6, Linux x64; .NET SDK 10.0.400 at `/tmp/dotnet10` for this session.
- Before modification, `SIPackages.Tests` had 85 passed and 5 skipped tests. `SIQuester.ViewModel.Tests` targeted `net10.0-windows` and discovered no tests on Linux.
- The initial view-model inventory contained 84 `PlatformManager.Instance` calls and 31 `async void` methods. The current counts are 67 and 26 respectively.
- Latest stable compatible Avalonia packages were verified and pinned at 12.1.1.

## Completed vertical slices

- Added `SIQuester.CrossPlatform.sln`, including the complete portable project-reference graph, and separated reusable Avalonia UI, desktop host, and headless tests while retaining the WPF application.
- Added a native Fluent three-pane editor shell with tabs, empty state, hierarchy, package/round/theme/question inspectors, common field editing, add/delete commands, media counts, compiled bindings, English/Russian resources, and keyboard bindings for primary commands.
- Added reusable typed metadata inspectors at package, round, theme, and question levels. Authors, sources, package tags, comments, and showman comments bind directly to the existing view models; list editing is duplicate-safe by selected index and supports add, edit, move, and guarded delete without a second editor model.
- Completed safe package/round/theme typed fields: publisher, contact address, arbitrary creation date, package language, restriction, difficulty, and round type now edit canonical models through compiled localized controls. Standard/final actions coexist with a raw round-type field so unknown future values remain editable; Unicode save/reload, undo/redo, and non-normalization of out-of-range legacy difficulty are tested. Theme already has no typed model fields beyond its verified name and metadata.
- Added localized package quality-control status and enable/disable actions to the typed Avalonia inspector. Avalonia validation uses the injected asynchronous dialog service, rejects scripted external media without mutating the marker, prevents concurrent command execution, and records canonical marker changes through the existing dirty/undo path. Enabling and disabling both survive `SIDocument.Load` save/reload; the retained WPF binding keeps its existing synchronous adapter, and the repository-wide `PlatformManager.Instance` count did not increase.
- Fixed two quality/media semantic gaps exposed by the slice: `Question.GetContent()` now discovers both script-step and question-parameter content in stable order, so validation and media-copy callers cannot miss scripted media; `Package.Clone()`, equality, and hashing now preserve the quality marker state.
- Completed package-logo inspector parity. The localized compiled inspector selects through `IFilePickerService`, previews embedded media with bounded native decoding, removes the canonical reference without deleting reusable package media, and refuses to fetch external package URLs. Stream-only desktop-portal files are staged under `IAppPaths.TemporaryMediaDirectory`; ownership and cleanup are tested for commit, close, validation failure, cancellation, undo/redo, and failed-save retry.
- Replaced the question inspector's primary-answer-only fields with complete right/wrong text-answer collection editors. Order, duplicate-safe editing, last-right-answer protection, empty wrong-answer lists, Unicode save/reload, and client-managed visibility are tested; non-text answer types retain explicit separate-editor boundaries.
- Added dedicated numeric, point-coordinate, selectable-option, and client-managed answer inspectors over the existing answer view models and canonical parameters. Select options support content editing, add/delete, visible right-option selection, undo/redo, and idempotent type reapplication; answer-type parameter observation keeps compiled bindings and cached editor lifetimes correct through undo/redo.
- Added an image-backed point-answer picker over the existing neutral selection request. A framework-neutral controller owns uniform-image coordinate/aspect calculations, invariant `x,y,aspect` serialization, deviation bounds, pointer clamping, and keyboard nudging; the Avalonia dialog performs cancellable bounded image loading, visible failure fallback, compiled localized controls, and deterministic stream/bitmap/event cleanup. Semantic SIQ reload and real PNG headless decode receipts pass.
- Added compiled typed scenario inspectors over the canonical models. Legacy question content stays legacy; explicit scripts expose each real step and recursively edit simple/reference, content, group, and number-set parameters. Content items support value, placement, duration, wait/reference flags, text/replic add, move, and guarded delete, with script-owned changes entering the existing dirty/undo path.
- Added script-step add/delete/reorder and generic parameter create/delete controls for simple, content, group, number-set, and reference values. The ordered adapter mirrors canonical script identity by index, dynamically attaches document listeners, supports undo/redo in both move directions, preserves opaque future parameter types as editable values, and saves/reloads empty and Unicode parameter values. The package reader now retains self-closing empty parameters instead of dropping them.
- Fixed two persistence defects exposed by the scenario receipt: `Question.Clone()` now deep-clones scripts instead of silently dropping them during `SIDocument.SaveXml`, and isolated XML subtree readers now preserve every step/parameter in multi-step scripts. The five formerly ignored script-deserialization fixtures are enabled and green.
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
- Added an Avalonia per-entry recovery center with validated package preview, exact original/snapshot metadata, progress, restore, reveal-location, and two-step discard actions. Stale snapshots remain usable but restore as unsaved copies, so they cannot directly overwrite newer canonical packages. The WPF host retains its legacy bulk prompt behind an explicit host-capability seam.
- Added a localized recent-files section to the Avalonia empty state with full-path tooltips and the existing `OpenRecent` command.
- Added repository-owned self-contained package builders for Linux x64/ARM64 tarballs and Debian packages plus macOS x64/ARM64 app bundles. Output is staged privately, validated before atomic publication, normalized with `SOURCE_DATE_EPOCH`, shipped with license notices and checksums, and integrated into a five-RID CI artifact matrix. Linux packages include desktop/MIME registration and complete .NET/Avalonia native dependency metadata; macOS bundles declare editable SIQ document types.
- Added the native SIQuester icon to the Avalonia window and platform artifacts. The retained WPF release workflow now uses .NET 10 and current official action majors without changing the WPF/MSI authority boundary.
- Replaced `QDocument`'s cancellable `async void` search with an owned debounced task. Superseded and closing-document searches cancel without surfacing false errors, only the latest query may publish, state returns through an injected framework-neutral UI dispatcher, and cancellation sources are disposed after their tasks finish. Avalonia now exposes localized search/no-result controls with previous, next, clear, Ctrl+F, and macOS Meta+F behavior through compiled bindings.

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
- `SIPackages.Tests`: 96 passed, 0 skipped, 0 failed. `Clone_PreservesQualityControlSemanticState` and `GetContent_IncludesQuestionAndScriptParameters` cover marker clone/equality/hash semantics and complete modern/legacy content discovery; script save/reload, deep-clone, reader-state, and empty-parameter coverage remains green.
- `SIQuester.ViewModel.Tests`: 113 passed, 0 failed. Three `DocumentSearchTests` verify latest-only rapid-query publication through the injected dispatcher, stable package/round/theme/question result order and clear state, and close-during-debounce cancellation without affecting another document. Package logo, package/round fields, quality control, point selection, script CRUD, scenario content, metadata, answers, recovery, clipboard, and settings coverage remains green.
- `SIQuester.Avalonia.Tests`: 25 passed, 0 failed. `DocumentEditor_SearchBarRoutesFocusAndPublishesLatestResultState` verifies compiled search bindings, found/missing states, localized feedback, 900-pixel layout, and both Ctrl+F and Meta+F focus routing. Package logo, quality control, point selection, script CRUD, non-text answers, scenario content, metadata, recovery, clipboard, shortcuts, settings, selection, localization, and lifecycle coverage remains green.
- Existing WPF project cross-compiled on Linux with `-p:EnableWindowsTargeting=true`: passed, 0 errors and 0 warnings in the final incremental compatibility build. Native WPF execution remains a Windows-only verification.
- Native Linux Release smoke: the self-contained x64 tar payload and the identical executable extracted from the Debian package exposed a visible `SIQuester` window at 1200x760, opened `SIGameTestNew.siq`, and exited normally through the application-owned `Ctrl+Q` path. Both logs record successful open/settings commit and no fatal or unhandled exception.
- Recovery-center Linux receipt: the framework-dependent Release host opened a validated 2,973,900-byte recovery snapshot with Unicode display/original paths, rendered its per-entry actions, and asynchronously previewed the real package as 2 rounds, 7 themes, 35 questions, and 12 media files. The 1200x760 visual inspection found no overlap or clipping; `Ctrl+Q` exited with code 0, empty stderr, and no fatal/unhandled log entry. Receipt directory: `/tmp/siquester-recovery-smoke.w7Cnsd`.
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
- Current artifact: 1,078 bytes; SHA-256 `1d8825a697b419c747e5a82a7c853b31aead81a7db7fa6eb6c79083b3d477c70`. The 454-byte receipt SHA-256 is `37276273c69fec888b6f4ed009d8cd1e2c90277ed1b447fdbd643848c19f91e7`. ZIP metadata can change the SIQ hash between generated runs; semantic and media receipts remain authoritative.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one image with semantic and byte comparison.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No external blocker for continued implementation.
- Native Windows WPF/Avalonia execution, hosted package publication, and macOS signing/runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

1. Replace clipboard media materialization paths with a bounded, stable-lifetime transfer representation and add media-rich cross-process compatibility coverage.
2. Implement data-level flat-mode reorder operations and their move/copy/cancellation tests.
3. Obtain hosted Linux/macOS/Windows CI receipts, including native macOS bundle signing/launch and native Windows WPF acceptance.

## Known limitations

- This is a verified first vertical slice plus package/round/theme safe typed fields, metadata, package logo and quality control, complete text-answer lists, dedicated non-text answer inspectors with image-backed point picking, scenario/parameter editing with step and generic-parameter CRUD, settings, native clipboard, complete recovery persistence/management, and release packaging foundations, not Milestone 2 completion. Direct parameter-key rename/type conversion, clipboard media lifetime, general media-library picker/preview, flat mode, question preview, structural SPARD, and advanced import/export remain incomplete.
- Recovery retention is deliberately conservative: only the latest validated generation is kept per document; superseded generations are removed after pointer commit, and a successful canonical save, approved close, or explicit discard removes the recovery identity. Valid stale snapshots and malformed entries are not age-pruned silently because doing so could destroy the only recoverable user data.
- Language changes intentionally apply after restart and communicate that boundary. System/light/dark selection is live and persisted, but visual theme snapshots and a macOS runtime Command-key receipt remain pending.
- WebView and audio/video backends have architectural decisions but no implementation receipt.
- No performance baseline has been measured yet.
- Linux packages are locally executable and structurally accepted, but actual desktop/MIME cache registration after system install and native ARM64 launch remain pending. macOS bundles are cross-built only; ICNS/codesign and native launch are hosted-runner boundaries.
- CI workflow YAML and commands are locally validated, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed implementation commit: `37218d55` (owned cancellable document search), following `02a83082`, `05dfc423`, `82e8e520`, `6908f32d`, and the earlier typed-inspector commits. Release cross-platform and WPF builds pass with 0 errors and 0 warnings in the final incremental builds; 234 tests pass with no skips. The adversarial review fixed a search-run publication/disposal race and verified latest-only results, expected-cancellation silence, close isolation, dispatcher ownership, localized compiled UI, Ctrl/Meta focus routing, responsive minimum width, and no new global platform call, `async void`, debug print, dependency, or hardcoded user-facing XAML string. A new manual Linux screenshot receipt was not produced; the existing native Linux startup/open receipt remains valid.
