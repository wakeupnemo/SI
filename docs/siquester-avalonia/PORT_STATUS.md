# SIQuester Avalonia port status

Updated: 2026-08-28

## Current v0.2.1 release checkpoint

- v0.2.1 is a focused responsiveness and persistence hardening update over the
  existing v0.2.0 authoring feature set. It adds no SIQ semantics or advanced
  parity features and leaves the legacy WPF version at 6.9.1.
- A complete ordinary-Avalonia call-path review found four additional UI lock
  inversions: validation file-size checks, quality-control checks, media-aware
  copy, and media collision checks during paste. These paths now use cancellable
  asynchronous document-lock acquisition and package-stream access. The current
  Avalonia clipboard embeds canonical media bytes without invoking WPF-only
  `PlatformManager.PrepareMedia`; temporary legacy-compatible paths have tested
  publish, replacement-failure, and close ownership.
- Durable flush, ZIP finalization, and canonical pre/post-commit validation run
  away from the UI thread. Canonical save no longer parses the same temporary
  SIQ twice; recovery snapshots retain their independent validation. Existing
  same-directory staging, validation-before-replace, rollback, backup, and
  late-cancellation semantics are unchanged.
- Release suite: 370 passed, 0 failed (`SIPackages.Tests` 96,
  `SIQuester.ViewModel.Tests` 213, `SIQuester.Avalonia.Tests` 61). The explicit
  20-warm-up/50-measured-cycle managed soak passed with p50 8.9 ms, p95 21.8 ms,
  max 23.7 ms, zero retained package descriptors, and bounded final thread/FD
  counts (21 to 24 threads, 197 to 201 FDs).
- Native Debian 13/X11 review used the current code on a 104 MiB real package.
  The dedicated WebKitGTK 2.52.6 receipt rendered controlled package media,
  replayed, and disposed both sessions (2/2). A 60-second selection,
  preview/close, and media-copy trace contains no synchronous document-lock or
  media-open stack on the UI thread; the largest sampled managed UI interval was
  236.6 ms. The user accepted this native review and requested no further GUI
  automation while the desktop is in use. The final version-only rebuilt
  packages pass checksum, metadata, launcher, payload, and tar/DEB identity
  checks without an additional GUI launch.
- GitHub release `v0.2.1` is published from
  `feat/siquester-avalonia` at release checkpoint `ebab3645`:
  <https://github.com/wakeupnemo/SI/releases/tag/v0.2.1>. Release id
  `378488721` contains exactly three uploaded assets. GitHub reports SHA-256
  digests identical to the local tar and DEB; the 191-byte checksum asset digest
  is `e5994d99cf7ccd051db121ce1b46082505b6af18fce65394b3b7c220628549dd`.
  Remote `master` remains unchanged at `8c2bee9c`.

- Question-authoring implementation commit: `fcfbfb51`; stability
  implementation commit: `ebf5e604`; Linux preview-launcher and Wayland
  implementation commit: `ff5b9e3a`; content-storyboard implementation commit:
  `37eb5931`; storyboard-selection fix commit: `82f05ef2` on
  `feat/siquester-avalonia`; first UI-stall fix commit: `4b6682e1`; v0.2.1
  hardening implementation commit: `32a62910`.
- All three suspected hardening defects were confirmed and fixed. Desktop close
  now serializes overlapping requests, persists settings before document close,
  catches and logs failures, keeps the window/data open on failure, and reports
  one actionable error. Failed ordinary/corrupt opens now remove their loader in
  `finally` and restore workspace selection. Persistent XDG logs now record
  version/session/runtime/OS/architecture, original exceptions, Avalonia
  Error/Fatal events, and last-resort failures without transient binding noise.
- Interactive profiling of a 108,168,147-byte package found two real UI-lock
  defects and one synchronous persistence hot path. Before the fix, image
  preview and point selection could synchronously wait for the document lock
  while recovery required a UI continuation; one captured hang retained that
  stack for 167.747 seconds. The same trace measured about 15.03 seconds of
  recovery work on the UI thread: 9.694 seconds in package `SaveAs` (9.620
  seconds copying the source ZIP) and 5.265 seconds finalizing the temporary
  ZIP. `4b6682e1` adds cancellable asynchronous archive copying and media stream
  acquisition, finalizes and validates recovery snapshots off the UI thread,
  and preserves the existing locked model serialization and safe-save flow.
  Focused lock-contention tests pass, and the user confirmed that the rebuilt
  application remains responsive when `Select a point` is invoked during the
  previously failing workflow. No general performance gain or leak claim is
  made from this focused trace.
- Every semantically distinct behavior in `SIGameTestNew.siq` is now authorable
  through the normal typed inspector from a new package: round default, simple,
  stake, stake-all, all Secret/public/no-question price and recipient shapes,
  no-risk, for-everyone, custom/manual, and empty structural cells.
- The inspector also authors answer duration and canonical post-answer
  text/image/audio/video content. Unknown/future type names and parameters are
  preserved during open/display/save and change only after explicit selection.
- The superseded v0.2.0 release suite had 364 passing tests; current v0.2.1
  evidence is recorded above.
- Native Debian 13/X11 tar smoke opened a Unicode/space-path SIQ, authored a
  Secret question with theme `NativeSecretTheme` and fixed price 700, added
  `PostAnswerSmoke`, safely saved, closed, reopened, and closed cleanly. The
  canonical XML and exact package were retained in the release receipt; the log
  records validated commits and no error/fatal/unhandled exception.
- The earlier `ebf5e604` self-contained tar completed 50 native preview
  lifecycle open/close cycles plus
  a 10-second settle: 51/51 sessions disposed, three stable processes, two
  WebKit children, 79 settled threads, 339 settled file descriptors, and no
  warning/error/fatal/unhandled log entry. RSS was non-monotonic and settled at
  908,800 KiB versus 887,080 KiB before repeated cycles; no leak or speedup is
  claimed from this bounded run.
- A real installed-DEB run on Debian 13/NVIDIA exposed blank preview rendering
  with GBM/KMS allocation failures even though preview sessions, controlled
  media routes, and disposal succeeded. The managed environment default was too
  late for this native initialization path. `ff5b9e3a` replaces the Debian
  symlink with a launcher that sets the default before apphost execution and
  adds the same launcher to the portable tar. Explicit values (`0` and empty
  included) remain untouched.
- Official Avalonia Wayland 12.1.1 support is registered with automatic X11
  fallback. The Release build and package topology pass; a native Wayland
  receipt is pending because this host is an X11 session without a test
  compositor.
- The user confirmed that preview renders through the rebuilt launcher on the
  affected Debian/NVIDIA desktop. The compositor workaround is therefore
  visually accepted for that host.
- Hosted run `33164408794` built the v0.2.0 linux-x64 tar and amd64 DEB from
  `a6bf38a7`; checksums and exact packaged startup/open/clean-close pass. The
  artifacts contain the regular launcher, storyboard selection fix, and
  `Avalonia.Wayland.dll`/`NWayland.dll`. Its dedicated WebKitGTK preview job
  also passes.

## Verified baseline

- Initial HEAD: `8c2bee9c38d250884a3e575809172e626448a0f9` on `feat/siquester-avalonia`; the worktree was initially clean.
- Host: Debian 13.6, Linux x64; .NET SDK 10.0.400 at `/tmp/dotnet10` for this session.
- Before modification, `SIPackages.Tests` had 85 passed and 5 skipped tests. `SIQuester.ViewModel.Tests` targeted `net10.0-windows` and discovered no tests on Linux.
- The initial view-model inventory contained 84 `PlatformManager.Instance` calls and 31 `async void` methods. The current literal counts are 60 and 23 respectively.
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
- Completed the focused v0.2.0 demo-question authoring slice. A localized high-level selector maps ordinary author intent to the existing canonical question type and parameters; contextual controls cover every Secret price/recipient shape, answer duration, and post-answer content. Secret-family switches preserve compatible settings, endpoint step derivation is included in one undoable edit, and opened unknown/future values remain unmodified. Three shared-model acceptance tests and one compiled headless UI test create representative questions from a new package, safely save, reload through `SIDocument.Load`, and verify every distinct type/parameter/answer/content/media shape in `SIGameTestNew.siq`. The native packaged receipt additionally authors and reloads a Secret question and post-answer content.
- Added an image-backed point-answer picker over the existing neutral selection request. A framework-neutral controller owns uniform-image coordinate/aspect calculations, invariant `x,y,aspect` serialization, deviation bounds, pointer clamping, and keyboard nudging; the Avalonia dialog performs cancellable bounded image loading, visible failure fallback, compiled localized controls, and deterministic stream/bitmap/event cleanup. Semantic SIQ reload and real PNG headless decode receipts pass.
- Added compiled typed scenario inspectors over the canonical models. Legacy question content stays legacy; explicit scripts expose each real step and recursively edit simple/reference, content, group, and number-set parameters. Content items support value, placement, duration, wait/reference flags, text/replic add, move, and guarded delete, with script-owned changes entering the existing dirty/undo path.
- Replaced the raw content list in the Avalonia inspector with a localized
  presentation storyboard. It derives moments directly from canonical
  `WaitForFinish` boundaries, shows screen/showman/background/unknown placement
  lanes, and supports select, join, split, item movement, and whole-moment
  movement with undo. Both question and post-answer content reuse it; explicit
  script steps remain separate editors, and unknown types/placements are not
  normalized. Initial selection now owns the visible state and command
  subscriptions, while moment-move undo/redo resynchronizes selected object and
  index before later index-based commands. `ContentMoments_GroupCanonicalItemsByWaitBoundaryAndPlacement`,
  `StoryboardCommands_QuestionAndPostAnswerContent_SaveCanonicalRoundTrip`,
  and `ContentStoryboard_JoinsAndSplitsCanonicalMomentsInCompactLayout` cover
  boundary semantics, malformed trailing groups, move/undo, Unicode, compact
  layout, accessibility, and `SIDocument.Load` round-trip.
- Added script-step add/delete/reorder and generic parameter create/delete controls for simple, content, group, number-set, and reference values. The ordered adapter mirrors canonical script identity by index, dynamically attaches document listeners, supports undo/redo in both move directions, preserves opaque future parameter types as editable values, and saves/reloads empty and Unicode parameter values. The package reader now retains self-closing empty parameters instead of dropping them.
- Completed typed parameter rename and explicit type conversion. Rename preserves the exact underlying value, rejects empty and duplicate keys, and is one undoable change. Conversion offers only canonical simple/content/group/number-set/reference targets, reapplies established parameter ordering, preserves simple/reference text, initializes valid structured containers, and restores the exact previous model on Undo. Unknown future kinds remain visible and byte-semantically untouched unless the user explicitly selects a known target; Unicode rename/conversion survives `SIDocument.Load` save/reload.
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
- Replaced path-dependent cross-process item clipboard media with a self-contained version-2 payload. All four media classes are embedded under a 20 MiB/512-item raw limit inside the existing 32 MiB envelope, source streams are read asynchronously, application-owned paste staging is flushed and released on commit, and slash traversal plus unequal same-name collisions fail closed. Version-1 envelopes and the original WPF `siqdata` JSON remain readable; the legacy writer keeps its exact path-only shape without duplicating media bytes.
- Added the first typed flat-mode vertical slice. A framework-neutral operation seam addresses questions by canonical round/theme/question indexes plus a serialized-content fingerprint, rejects stale/foreign/invalid drops, and applies move or copy as one undoable change. Same-theme and cross-theme moves, duplicate copy, cancellation, invalid/foreign targets, explicit version-1 drag serialization, positional-price recalculation, disabled recalculation, invalid-price preservation, canonical model synchronization, and undo/redo are covered. The Avalonia view persists Tree/Flat choice, renders localized compiled round/theme/question rows, uses pointer-threshold/capture-loss cancellation, shows insertion indicators, and maps Ctrl/Command to copy without carrying live UI objects.
- Promoted flat mode from the navigator into the main authoring workspace while retaining the hierarchy and placing the typed inspector beside it. Persisted table/list layouts and explicit package/round/theme/question detail choices now preserve the legacy semantic levels, list mode virtualizes top-level rounds without an enclosing scroll extent, and question mode exposes localized move/duplicate actions. Alt+Arrow reorders according to layout and Ctrl/Command+D duplicates through the same framework-neutral commands; the headless receipt traverses every scale, both persisted modes, inspector synchronization, virtualization, keyboard operations, and visible action routing.
- Fixed two flat-operation defects shared with the retained editor: downward `ObservableCollection.Move` now updates canonical `Theme.Questions` in the same order, and price-slot capture excludes `Question.InvalidPrice` so structural questions cannot leak the invalid sentinel into ordinary question prices. WPF and Avalonia use the corrected price semantics.
- Added an isolated external-file drop path to the flat authoring workspace. A framework-neutral classifier accepts `.siq`, `.txt`, the complete image/audio/video extension inventory, and HTML while rejecting mismatched or unsupported executable extensions before opening a stream. SIQ and text files delegate to an explicitly attached `MainViewModel` host callback; media stages through the existing application-owned storage path and adds a canonical reference to the targeted legacy or scripted question as one undoable change. Stream-only desktop-portal files, Unicode names, cancellation, malformed-package cleanup, repeatable BOM-aware text decoding, all-four-media byte save/reload, and legacy-content undo/redo are verified. Package HTML is imported as inert media bytes and is not executed.
- Flattened the detailed flat-list workspace to one framework-neutral row per theme while retaining header-only rows for empty rounds. Avalonia now virtualizes representative packages that contain few rounds and many themes instead of only virtualizing top-level rounds. `FlatWorkspace_LargePackageVirtualizesQuestionCardsAndScrollsToLastRound` generated 4 rounds, 400 themes, and 2,000 questions, realized 15 question cards at both the first and final viewport, reached `Theme 4.100`, and measured 1,045-1,062 ms for first layout on this Debian host. This is the first reproducible baseline, not a claimed improvement.
- Added a compiled, localized media-library inspector for images, audio, video, and HTML. Its neutral asynchronous picker supports multiple stream-only portal files as one undoable operation, selected images decode through the existing bounded Avalonia loader without invoking the legacy media backend, and selected library files can link to the active question or navigate to existing usage. Removal revalidates references at execution and refuses to delete referenced bytes; unreferenced removal and undo, close-time picker cancellation, deterministic stream/bitmap/subscription cleanup, real PNG decode, and semantic/byte SIQ reload are tested.
- Added a compiled, localized validation/statistics sidebar in tree and flat modes over the retained shared model. It publishes typed round/theme/question/media counts, a bounded issue list, optional author/source/bracket checks, detailed legacy report text, and direct navigation to canonical editor items or the correct media-library tab. Refresh and unused-file cleanup are owned cancellable tasks; cleanup uses the neutral dialog service, serializes against refresh, groups multi-category removal into one undo action, and revalidates references after confirmation before deleting. Six shared-model tests and one headless interaction test cover counts, issue navigation, category routing, cancellation, undo, localization, and the post-confirmation data-loss guard.
- Added typed round/theme move and adjacent-duplicate commands to the compiled Avalonia inspector. Moves update the view-model and canonical `SIPackages` collections in lockstep, boundary commands disable predictably, and every move/duplicate is one undoable change. Adjacent duplication deep-clones descendants; the retained WPF `Clone` commands deliberately keep their historical append behavior. Five shared-model tests cover canonical order, boundaries, undo/redo, deep clone, Unicode-path `QDocument.Save`/`SIDocument.Load`, and WPF command compatibility; one headless test verifies localized compiled buttons for both item levels.
- Completed hierarchy structural commands in the typed inspector by adding question move and adjacent duplicate over the existing framework-neutral flat-operation engine. Question moves therefore share positional-price recalculation, invalid-price protection, canonical synchronization, selection, and one-step undo semantics with flat mode. `HierarchyCreateAndDelete_UpdateCanonicalCollectionsAndUndoAsSingleChanges` verifies round/theme/question creation and removal at every level; `QuestionMoveAndDuplicate_ReusePriceRulesAndRoundTripAsSingleChanges` verifies deep clone, boundary states, undo/redo, positional prices, and Unicode `SIDocument.Load` reload. The expanded headless inspector test executes every add/move/duplicate/delete route, while legacy WPF round/theme/question `Clone` commands retain append-at-end behavior.
- Hardened the native flat-question drag source around an always-visible localized 28-pixel grip. The grip owns press/move/release/capture-loss state, uses the existing bounded version-1 typed payload, and follows Avalonia's platform-owned `DataTransfer` lifetime contract. `DocumentEditor_FlatWorkspacePersistsLayoutAndScaleAndRoutesKeyboardOperations` now proves the grip is a real headless pointer hit target and selects the intended question. A hosted Xvfb diagnostic additionally reached `DragDrop.DoDragDropAsync`; target-side Xdnd delivery remains unverified and is not inferred from source activation.
- Added direct selection-targeted image/audio/video/HTML controls to every top-level content editor and an image control to bounded answer-option content. The existing WPF `AddFile` command now routes through the same neutral asynchronous picker instead of the platform global. All files are type-checked before opening, stream-only portal selections stage before mutation, media bytes plus references commit as one undoable change, answer content is limited to one file, visible progress disables repeat operations, and document close cancels the owned task. Four `ContentMediaPickerEditingTests` verify multi-file insertion, undo/redo, wrong-type fail-closed behavior, non-top-level replacement, close cancellation, Unicode SIQ reload, and all-four-media byte preservation; two headless receipts verify compiled controls and busy-state presentation.
- Connected the structural SPARD migration to the existing text-import workspace without copying WPF `RichTextBox`/`FlowDocument` or creating a second importer model. A platform-neutral session synchronizes each existing `SpardTemplateViewModel` with `SpardEditorController` in both directions and routes the established alias, optional, variant, cut, copy, and paste commands. The compiled localized Avalonia workspace renders all four importer states, uses the neutral asynchronous text picker, supports UTF-8/Windows-1251 preview, exposes visible progress and correction panes, and owns every SPARD session through visual attach/detach. Two session tests, four importer workspace tests, and four importer headless tests cover command routing, Unicode stream-only input, invalid-extension rejection before stream access, cancellation/disposal, MainWindow template resolution, explicit state visibility, synchronization, and detach cleanup. Buffered portal bytes are released immediately after approval while the independent filename metadata remains available for package naming and recovery.
- Completed the representative stock-SNS text-import receipt. `WorkspaceCommands_ConvertSaveAndReloadRepresentativeTextPackage` drives the neutral stream picker, approval, cancellable split, SNS template selection, conversion, production safe save to a Unicode/space path, and `SIDocument.Load` reload. It verifies package/round/theme/question names, prices, right answers, theme author, question comment/source combinations, and no unintended backup. The SNS-only normalization fixes legacy greedy matching without changing custom templates. Split, auto-detection, and conversion now own and observe their tasks, publish worker state through `IUiDispatcher`, use neutral dialogs, and dispose cancellation state; two legacy platform-global calls and one `async void` were removed. Text import is `VERIFIED`.
- Completed structural SPARD editor parity without copying the WPF rich-text control or adding a dependency. The real importer now presents controller-owned plain-text, alias, line, optional-boundary, and opaque tokens with localized automation names/tooltips, explicit nesting, semantic borders, existing alias color metadata, bounded values, and a 256-token safety cap. Six `SpardEditorControlTests`, ten controller tests, two session tests, the real-importer headless receipt, and the representative text-conversion safe-save receipt cover structural edits, malformed recovery, synchronization, background dispatch, duplicate alias metadata, bounded rendering, detach cleanup, and semantic `SIDocument.Load` reload.
- Integrated the official MIT-licensed `Avalonia.Controls.WebView` 12.1.0 behind the retained question-player protocol. Native engines are probed lazily; application assets are served from an ephemeral randomized loopback route with restrictive headers, exact navigation, denied popups, bounded JSON, an explicit readiness signal, and deterministic view detach. Missing Linux/Windows/unsupported backends remain localized non-fatal capability states. Six platform-neutral and six focused Avalonia tests pass both required missing-WebKit and staged-WebKit capability branches. A real WebKitGTK 2.52.6/Xvfb smoke opened `avalonia-core-roundtrip.siq`, advanced one fragment, rendered the exact Russian question text, and exited without preview-host/fatal errors. Broader runtime parity remains pending.
- Added the Debian 13 WebKitGTK compositing compatibility default. Follow-up
  native evidence showed that managed startup was not early enough on the
  reported NVIDIA/GBM path, so packaged Linux builds now apply the same default
  in a POSIX launcher before apphost execution. The launcher and managed
  fallback both preserve explicit values; shell probes cover unset, `0`, and
  explicitly empty states.
- Added owned per-dialog question-preview media sessions. Embedded package image/audio/video references now resolve through opaque random loopback URLs without `PlatformManager.Instance`, package names, or frontend types entering the protocol; the server enforces an extension/MIME allowlist, 512 MiB item and 256-item session bounds, single-byte-range responses for media seeking, strict Host/path checks, bounded request lines/headers, request concurrency/timeouts, and cancellation-aware stream/session/server teardown. SVG and package HTML are excluded; WPF retains a compatible session-owned temporary-file adapter. Six platform-neutral preview tests and six focused Avalonia server/bridge tests cover byte ownership, ranges, unsafe types, bounds, malformed HTTP, deterministic removal, and dialog replacement/close/dispose. A fresh-state WebKitGTK 2.52.6 smoke fetched a referenced 97-byte PNG from the compatibility SIQ and rendered the mixed Russian text/image player surface.
- Completed the deterministic select-answer/replay preview slice. The terminal engine fragment now exposes Replay without an extra sentinel click; localized controls have explicit automation names, and a semantic fixture preserves ordered options, the right option, a Unicode embedded-image name, and exact media bytes through production safe save plus `SIDocument.Load`. The document editor owns one persistent native WebView and reuses it across dialog sessions while each dialog still owns and disposes its isolated media routes. This document-scoped pool fixed a reproduced WebKitGTK exit-time abort after two native-control lifetimes. Protocol/headless tests and a real two-session Linux receipt prove answer-option rendering, replay, two media fetches, two route create/dispose pairs, and clean process exit.
- Completed media-library audio/video preview behind the neutral `IMediaPreviewService`. A separate application-owned HTML5 page uses the existing randomized loopback origin, opaque extension-preserving URLs, byte ranges, MIME/size/session bounds, exact navigation and popup denial, and lazy backend probing. One native control is retained across selections while each selection owns an isolated route; view/document/app teardown stops the control and removes its route. Original media bytes and package persistence remain untouched. Focused VM, server, and headless tests cover stream ownership, unsupported backend presentation, range serving, and deterministic disposal. A native WebKitGTK 2.52.6 receipt decoded the repository MP3 through 0:03 and rendered changing MP4 frames; Audio was disposed before Video and Video on close.
- Verified native external-file Xdnd against the packaged Linux application. The target resolves routed X11 drop sources by ancestor and realized-card coordinates; a Unicode-named PNG import visibly marks the document dirty, enables Undo, saves safely, reopens cleanly, and preserves exact media bytes. Same-window synthetic internal Xdnd remains deferred because XTEST does not reliably deliver it.
- Completed the v0.1.0 accessibility pass without redesigning the workspace. The minimum window now fits a 1920x1080 display at 200% scaling, long command bars scroll instead of clipping, and the bilingual Light/Dark minimum-viewport test verifies navigator, inspector, focus, accessible search naming, and long English/Russian content at 900x480 logical pixels.
- Separated `SIQuesterCrossPlatformVersion` (currently 0.2.1) and the `SIQuester Cross-Platform` product identity from legacy WPF `SIQuesterVersion` 6.9.1. Linux/macOS/Windows Avalonia packaging overrides only the cross-platform property, so referenced domain libraries and the WPF product are not re-versioned.

## Current verified commands and results

Run with the system SDK or prepend the temporary SDK environment shown in `BUILDING.md`.

```bash
dotnet restore SIQuester.CrossPlatform.sln -p:Configuration=Release -m:1
dotnet build SIQuester.CrossPlatform.sln -c Release --no-restore -m:1
dotnet test test/Common/SIPackages.Tests/SIPackages.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.ViewModel.Tests/SIQuester.ViewModel.Tests.csproj -c Release --no-build --no-restore -m:1
dotnet test test/SIQuester/SIQuester.Avalonia.Tests/SIQuester.Avalonia.Tests.csproj -c Release --no-build --no-restore -m:1
```

- Cross-platform Release build: passed with 0 errors. A source rebuild reported 130 pre-existing nullable/obsolete warnings across retained projects; the final full incremental validation reported 0 warnings. Analyzers and warnings remain enabled, and every portable project emitted to `bin/AnyCPU.Release`.
- `SIPackages.Tests`: 96 passed, 0 skipped, 0 failed. `Clone_PreservesQualityControlSemanticState` and `GetContent_IncludesQuestionAndScriptParameters` cover marker clone/equality/hash semantics and complete modern/legacy content discovery; script save/reload, deep-clone, reader-state, and empty-parameter coverage remains green.
- `SIQuester.ViewModel.Tests`: 207 passed, 0 failed. Four
  `MainLifecycleStabilityTests` cover default-token corrupt-loader cleanup,
  overlapping closes, one-message workspace failure, and host-owned close.
  `SaveDocument_UnwritableDirectory_ShouldLeaveExistingPackageUntouched` is a
  real Linux permission failure. The three demo-authoring tests and all prior
  import, SPARD, hierarchy, parameter, media, validation, recovery, clipboard,
  and safe-save coverage remain green.
  `OpenStreamAsync_WaitsForDocumentPersistenceWithoutBlockingAndSupportsCancellation`
  and `PointImageOpenAsync_DoesNotBlockWhileRecoveryOwnsDocumentLock` reproduce
  the profiled lock contention without blocking the caller and verify exact
  original PNG bytes after the lock is released.
- `SIQuester.Avalonia.Tests`: 61 passed, 0 failed. Main-window settings failure
  keeps the window open and reports once; startup version/environment, focused
  Error/Fatal logging, exact benign IBus shutdown filtering, continuous answer
  entry, preview, accessibility, SPARD, text import, hierarchy, parameter,
  media, keyboard, and lifecycle receipts remain green.
- Existing WPF frontend cross-compiled on Linux with `dotnet build src/SIQuester/SIQuester/SIQuester.csproj --no-restore -p:EnableWindowsTargeting=true -m:1`: passed with 0 errors; a fresh source rebuild reports 130 existing warnings. A broader `SIQuester.sln --no-restore` attempt failed only because the unrelated `Notions.Tests` assets file was absent in this no-restore environment; no product project failed. Native WPF execution remains a Windows-only verification.
- Native Linux v0.1.0 smoke: the final tar and DEB contain the identical executable (SHA-256 `5ec8318c2b70972758e055ba15b6568e5d4b75bceeac8ad2f94f89fceca50a6d`). It exposed `SIQuester Cross-Platform` at 1200x760, opened `SIGameTestNew.siq`, exited through Ctrl+Q, committed settings, and logged no fatal/unhandled exception. The packaged Xdnd receipt imported `/tmp/v0.1.0 release изображение.png`, saved, closed, reopened, and matched exact source/package media SHA-256 `2f9a7766a7c9eec27946f95cb41b9acd11b1012e3338866fe4269ea00d184b50`; receipt directory: `/tmp/siquester-v0.1.0-smoke/dragdrop-receipt`.
- Native Linux v0.2.0 authoring smoke: the extracted final tar opened `/tmp/siquester-v0.2.0-native-smoke/receipt/native authoring 例.siq` on Debian 13/X11. Through the actual compiled controls it selected Secret, entered theme `NativeSecretTheme`, fixed price 700, and post-answer text `PostAnswerSmoke`; Ctrl+S cleared dirty state after each edit. Ctrl+Q closed cleanly, the packaged application reopened the saved SIQ, and a second Ctrl+Q exited 0. `content.xml` contains canonical `type="secret"`, `selectionMode=exceptCurrent`, `numberSet(700,700,0)`, theme, and answer content. The log records two validated commits, recovery cleanup, two successful opens, and two complete close flows with no error/fatal/unhandled entry. Durable receipt: `artifacts/siquester-cross-platform-v0.2.0/native-smoke/RECEIPT.txt` (SHA-256 `b33b22e6852f91214fb47c5147465a8c54c3bb3223ae00c3ff9f21ae313fe426`).
- Final native stability receipt: the tar built from `ebf5e604` opened
  `SIGameTestNew.siq`, created/disposed 51/51 preview sessions across 50 repeated
  cycles, retained exactly one SIQuester plus two WebKit processes, settled for
  10 seconds, committed settings, closed the document, and exited 0. Latency was
  p50 650.549 ms, p95 654.523 ms, maximum 656.041 ms. Raw JSON/log and the
  managed-soak receipt are under
  `artifacts/siquester-cross-platform-v0.2.0/stability/`.
- Packaged preview fallback receipt: on this clean release host WebKitGTK is unavailable, so preview produced the localized actionable dependency state, stayed responsive, closed cleanly, committed settings, and logged no fatal/unhandled exception. Earlier WebKitGTK 2.52.6 native question and MP3/MP4 receipts remain valid implementation evidence; full playback was not re-run for v0.1.0.
- Native Linux question-preview receipt: staged WebKitGTK 2.52.6 under Xvfb opened `avalonia-preview-options.siq`, rendered the Russian select-answer question plus embedded yellow image, reached terminal Replay in three fragments, replayed, closed, reopened, fetched the image again, and exited 0. The receipt records `created_sessions=2`, `disposed_sessions=2`, `replay_count=1`, `media_fetches=2`, and bright-pixel fraction `0.0968566`; the visually inspected 92,678-byte screenshot SHA-256 is `2b19d35abb5f955ec961b1ad44b9e5e4e7bfd3459b46b8ae905b7c5fbf3007e7`. The 2,420-byte log SHA-256 is `9d68bc04c0fd2715670524f3b601dfeb6d92e389ef3076c3102b062608fc9890` and contains no preview-host failure, fatal, or unhandled exception. Receipt directory: `/tmp/siquester-preview-options-smoke-20260827-3/receipt`.
- Debian 13 compositing-fix acceptance: the user reproduced a blank native question surface under normal startup and a correctly rendered question with `WEBKIT_DISABLE_COMPOSITING_MODE=1`. The startup tests pass 2/2 and prove default/preserve behavior before `AppBuilder` creation; the user subsequently confirmed that preview renders through the rebuilt launcher on the affected Debian/NVIDIA desktop.
- Native Linux media-preview receipt: staged WebKitGTK 2.52.6 under Xvfb opened repository fixture `SIGameTestNew.siq`; the 52,079-byte MP3 fetched through the controlled route and its visible transport reached 0:03, while the 1,046,987-byte MP4 fetched through a new route on the retained native control and rendered changing decoded frames. Switching tabs logged Audio-session disposal before Video creation; `Ctrl+Q` logged Video-session disposal, document close, settings commit, and exited cleanly with no fatal/unhandled entry. The final MP3 screenshot SHA-256 is `6fcf23a8e6e3dc9adfc060b070bf777856bdc0094588fa3174e79861b19184b9`, the playing-MP4 screenshot SHA-256 is `d2a6450e6ac47a204909d7606ae19a1e0eedf943070c33128caaab540b31fbee`, and the log SHA-256 is `b5aab1fe919869482004547f9dd236d7a7cd048a2a5033d672aa81c0bfe550db`. Receipt directory: `/tmp/siquester-media-preview-smoke-20260827-pooled`.
- Linux codec-metadata package receipt: a fresh self-contained x64 tar and Debian package include `media-preview.html`/`.js`; both checksum entries pass. The DEB `Recommends` field contains WebKitGTK/WPE plus GStreamer base/good/bad/ugly/libav. Tar SHA-256: `2ad9a9570c49fd63f45868f15d8e32702e329183c7185c3d173f827b5001eac5`; DEB SHA-256: `0725a71a6e36a92a5125c418d5b6a855d0ef954b00a5261e15cae46c0b1d2238`; directory: `/tmp/siquester-media-preview-package`.
- Recovery-center Linux receipt: the framework-dependent Release host opened a validated 2,973,900-byte recovery snapshot with Unicode display/original paths, rendered its per-entry actions, and asynchronously previewed the real package as 2 rounds, 7 themes, 35 questions, and 12 media files. The 1200x760 visual inspection found no overlap or clipping; `Ctrl+Q` exited with code 0, empty stderr, and no fatal/unhandled log entry. Receipt directory: `/tmp/siquester-recovery-smoke.w7Cnsd`.
- Settings runtime receipt: `/tmp/siquester-settings-smoke.1LErfv/config/SIQuester/settings.json`, 1,492 bytes, SHA-256 `eb7dcd4289dbc07260886ddb6672cc02e8aec831b348d209345854340f651b9b`; the log records document close and `Application settings were committed successfully` with no fatal/unhandled exception.
- `tools/smoke-siquester-linux.sh` now accepts either a framework-dependent DLL or a self-contained native executable. It reproduced the graceful window/open/exit/settings/log receipt under Xvfb; cross-platform CI invokes it for both the ordinary Release build and packaged x64 tarball.

## Release artifacts

The final local v0.2.1 artifacts were reproducibly built from implementation
commit `32a62910af0e3ef492ffb7b4a0e1789f4084532b` using its commit timestamp as
`SOURCE_DATE_EPOCH`:

- Linux x64 tar: `artifacts/siquester-cross-platform-v0.2.1/SIQuester-0.2.1-linux-x64.tar.gz`, 59,018,645 bytes, SHA-256 `db306d1ee9433a8aeed8faf8757f624aa190e726f76796ac1bffea7feb3e4b6d`.
- Debian amd64: `artifacts/siquester-cross-platform-v0.2.1/siquester_0.2.1_amd64.deb`, 41,839,500 bytes, SHA-256 `a206a354574c5d6195b149d9f2cadeaa8a105527a180697e1a7dcf1d89fc9eab`.
- Adjacent checksum receipt passes both entries. The DEB declares package
  `siquester`, version 0.2.1, architecture amd64, and installed size 135,728
  KiB. Required launchers, apphost, WebView/Wayland assemblies, application
  assets, desktop/MIME metadata, licenses, and notices are present. Launcher
  shell syntax passes, the embedded assembly identifies
  `0.2.1+32a62910af0e3ef492ffb7b4a0e1789f4084532b`, and tar/DEB apphosts are
  byte-identical (SHA-256 `5ec8318c2b70972758e055ba15b6568e5d4b75bceeac8ad2f94f89fceca50a6d`).

The prior release v0.2.0 artifacts were built by hosted package job `98826279515`
from checkpoint `a6bf38a7`. They are published from
`feat/siquester-avalonia`; `origin/master` remains at `8c2bee9c`.

- Linux x64 tar: `artifacts/siquester-cross-platform-v0.2.0/ci/siquester-linux-x64/SIQuester-0.2.0-linux-x64.tar.gz`, 59,020,018 bytes, SHA-256 `958a08f6c163c707f3f088c40d6636a6bb1779d239b058960f6c54319060c8ca`.
- Debian amd64: `artifacts/siquester-cross-platform-v0.2.0/ci/siquester-linux-x64/siquester_0.2.0_amd64.deb`, 45,235,464 bytes, SHA-256 `81a25958eba83557cb3c52acc56d677b5e9d985ff7f123b49dd99450ee41dc42`.
- Adjacent checksum receipt passes both entries.
- Native authoring receipt and exact saved SIQ: `artifacts/siquester-cross-platform-v0.2.0/native-smoke/`.
- Final native and managed stability receipt: `artifacts/siquester-cross-platform-v0.2.0/stability/`.

The DEB declares version 0.2.0, architecture amd64, native dependencies,
WebKit/GStreamer recommendations, immutable installation paths, desktop entry,
icon, license/third-party notices, and `application/x-siq` MIME metadata. Its
native executable, `SIQuester.Avalonia.dll`, and `SIQuester.ViewModel.dll` are
byte-identical to the tar payload. The hosted matrix built all five RIDs, but
only the Linux x64 artifacts have release-level runtime acceptance.

## Compatibility artifact

- Test: `CompatibilityArtifact_CreateEditSaveReload_ShouldPreserveSemanticDataAndMedia`.
- Release output: `bin/AnyCPU.Release/SIQuester.ViewModel.Tests/net10.0/compatibility-artifacts/avalonia-core-roundtrip.siq`.
- Receipt: adjacent `avalonia-core-roundtrip.receipt.json`.
- Current Release artifact SHA-256: `bfe19dcd0b5c5c1be4007f39e94ebb9db0157a2f201b0df4de038f19bf9cddc8`; receipt SHA-256: `c37bca4e1e8f37d603b43436aaa1478054ceb476364fdc1d49108f01dbb8f8ed`. ZIP metadata can change generated SIQ hashes; semantic and media receipts remain authoritative.
- Verified through `SIDocument.Load`: one round, one theme, one question, and one referenced PNG with semantic and byte comparison.
- Select-answer preview artifact: `avalonia-preview-options.siq`, current Release SHA-256 `977d63480e371a1e0b8f4a94dbde5d74cd4c55be3419d1c5ca5d21939b067c07`; adjacent receipt SHA-256 `527ec083c19f4bf3a3c47866683731f75b6ea73aa86fc4971c53c2ade6ff465f`. `QuestionPreviewOptionsArtifact_CreateSaveReload_ShouldPreserveOptionMedia` verifies ordered labels `А`/`Б`, right option `Б`, `select` type, Unicode image reference, and exact image bytes through `SIDocument.Load`.
- Existing Windows SIQuester/SIGame runtime acceptance is not yet verified and must not be inferred from the loader receipt.

## Current blockers

- No source, artifact, or publication blocker is known for the focused Linux
  v0.2.1 release. No merge to `master` was performed.
- Hosted run `33164408794` is not aggregate-green: the Xdnd smoke script failed
  to execute because its repository executable bit was absent, and 37 Windows
  view-model tests exposed a Windows-only safe-save verification gap. These are
  deferred from the Linux-only v0.2.0 assets and remain explicit CI work.
- Native Windows WPF/Avalonia execution, hosted package publication, and macOS signing/runtime receipts require those operating-system runners.
- The host has no system .NET SDK; the session-local SDK is not a repository requirement.

## Next independent tasks

None in the completed v0.2.1 scope. Stop after this checkpoint. Retain real
Wayland-session launch, Xdnd runner permission repair, and Windows safe-save
verification as separate future work; no broader parity work is implied.

## Known limitations

- v0.2.1 is a usable ordinary-authoring Linux release with demo-question behavior parity, not full legacy parity. `custom` is intentionally a preserved manual type because the demo defines no canonical library behavior for it. Native same-window internal question Xdnd, macOS/Windows runtime acceptance, complete shortcut/runtime localization receipts, and advanced import/export/transform/optional integrations remain deferred. Explicit known-behavior or parameter conversion replaces incompatible structure only after a deliberate undoable user action; load, display, and save do not normalize unknown values.
- Recovery retention is deliberately conservative: only the latest validated generation is kept per document; superseded generations are removed after pointer commit, and a successful canonical save, approved close, or explicit discard removes the recovery identity. Valid stale snapshots and malformed entries are not age-pruned silently because doing so could destroy the only recoverable user data.
- Language changes intentionally apply after restart and communicate that boundary. System/light/dark selection is live and persisted, but visual theme snapshots and a macOS runtime Command-key receipt remain pending.
- The retained question player now has verified native Linux text, embedded select-answer image, completion/replay, two-session close/reopen, and a 50-cycle resource receipt plus automated available/unavailable backend gates. The media library additionally has verified native Linux MP3/MP4 decoding through controlled per-selection routes. Native macOS/Windows codec acceptance remains pending. Package HTML is deliberately rendered as an inert localized warning until a separate restrictive HTML policy is implemented.
- Native external Xdnd target acceptance now passes with visible dirty/Undo state, exact media persistence, safe save, and reopen. XTEST still does not reliably deliver same-window internal question Xdnd `DragOver`/`Drop`, so that narrower acceptance is deferred rather than inferred from source activation.
- The first deterministic flat-list baseline is 1,045-1,062 ms to create and lay out a generated 2,000-question document at 1100x700 on this Debian host, with 15 question cards realized at either scroll endpoint. It is a headless realization receipt rather than a native rendering profile; no performance improvement is claimed yet.
- Linux packages are structurally accepted, the prior X11 build has native
  authoring evidence, and the rebuilt launcher's visual preview is user-confirmed;
  the new Wayland backend still needs a native receipt. Actual desktop/MIME cache
  registration after system install and native ARM64 launch remain pending.
  macOS bundles are cross-built only; ICNS/codesign and native launch are
  hosted-runner boundaries.
- CI workflow YAML and commands are locally validated, but hosted Linux/macOS/Windows acceptance awaits an actual GitHub Actions run.

## Review state

- Latest reviewed implementation commit: `32a62910`; release documentation and
  tag checkpoint: `ebab3645`. This file,
  `FEATURE_PARITY.md`, `V0.2_QUESTION_AUTHORING.md`, and
  `RELEASE_NOTES_0.2.1.md` form the v0.2.1 documentation checkpoint. All 370
  ordinary Release tests and the explicit 70-cycle managed soak pass. The WPF
  frontend cross-build passes with 0 errors. The hosted v0.2.0 tar/DEB checksums and
  launcher/Wayland metadata/topology pass through `a6bf38a7` but predate
  `4b6682e1`; the earlier
  native 50-cycle result remains lifecycle evidence only. Safe-save, loader
  cleanup, failure retention, settings failure, and close serialization checks
  pass. The latest review additionally verifies initial storyboard selection,
  moment-move undo/redo selection identity/index synchronization, the next
  index-based command, compact Avalonia selected-card state, round-trip safety,
  and frontend compatibility.
  The latest review additionally checks asynchronous archive ownership,
  cancellation, temporary-package finalization, original-byte media access,
  absence of synchronous media-open calls in the Avalonia/Desktop projects,
  focused lock-contention regressions, manual point-selection responsiveness,
  `git diff --check`, the cross-platform Release build, and the retained WPF
  cross-build. The local v0.2.1 tar/DEB identify `32a62910`, pass adjacent
  checksums and structural acceptance, and retain the user-accepted current-code
  native review without claiming a second packaged GUI run. Hosted asset
  sizes/digests and remote refs were independently queried after publication;
  tag `v0.2.1` resolves to `ebab3645`, the feature branch contained that commit,
  and `master` remained at `8c2bee9c`.
