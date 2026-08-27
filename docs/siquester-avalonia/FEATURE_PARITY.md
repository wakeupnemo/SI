# SIQuester feature parity

Statuses are limited to `VERIFIED`, `IMPLEMENTED_NOT_VERIFIED`, `IN_PROGRESS`, `NOT_STARTED`, `BLOCKED`, and `NOT_APPLICABLE`.

| Area | Feature | Status | Evidence / boundary |
|---|---|---:|---|
| Runtime | Native Linux startup | VERIFIED | Self-contained tar and DEB payload exposed a 1200x760 window, opened a real SIQ, exited through Ctrl+Q, and committed clean settings/log receipts |
| Runtime | Native macOS startup | IMPLEMENTED_NOT_VERIFIED | x64/ARM64 Mach-O app bundles and SIQ plist metadata cross-build; native codesign/launch receipt requires macOS |
| Runtime | Avalonia Windows startup | NOT_STARTED | WPF remains the production Windows editor |
| Documents | New package | IMPLEMENTED_NOT_VERIFIED | Existing `NewViewModel` is hosted by Avalonia; end-to-end UI receipt pending |
| Documents | Open SIQ | VERIFIED | Command-line open of compatibility artifact plus startup log and visual receipt |
| Documents | Recent files / reopen | IMPLEMENTED_NOT_VERIFIED | Localized empty-state list, full-path tooltip, and `OpenRecent` command identity are headless-tested; real picker/reopen click receipt pending |
| Documents | Save | VERIFIED | `SaveDocument_*` tests and `SIDocument.Load` compatibility artifact |
| Documents | Save As | IMPLEMENTED_NOT_VERIFIED | Neutral picker and safe transaction are wired; UI picker receipt pending |
| Documents | Save All | IMPLEMENTED_NOT_VERIFIED | Async neutral flow implemented; multi-document UI test pending |
| Documents | Close save/discard/cancel | IN_PROGRESS | Typed dialog and lifetime seams implemented; empty-document close/reentrancy is headless-tested, dirty multi-document state matrix pending |
| Documents | Templates | NOT_STARTED | Existing built-in/custom templates inventoried |
| Documents | Autosave and recovery | VERIFIED | Ten recovery tests cover complete SIQ/media generations, identity, cancellation/races, startup, stale detection, per-entry preview/reveal/restore/discard, and safe stale restore-as-copy; Avalonia headless bindings and a real 2.9 MB Linux recovery-preview receipt pass |
| Data | SIDocument semantic round trip | VERIFIED | `SaveDocument_WithComplexStructure_ShouldPreserveAllData`, `SaveAndReload_PreservesMultiStepQuestionScript`, and compatibility artifact; scripted questions now survive package cloning and multi-step XML reload |
| Data | Media-byte round trip | VERIFIED | all-four-collection Unicode test plus artifact image hash comparison |
| Data | Unicode/long/space paths | IN_PROGRESS | Unicode/space/non-ASCII media names verified; generated long-path stress fixture pending |
| Data | Save failure preserves destination | VERIFIED | cancellation-before-write and injected post-commit validation-failure byte-rollback tests |
| Data | Versioned settings persistence | VERIFIED | Nine `SettingsStoreTests`: legacy/current/corrupt/invalid/future/cancel/replacement; graceful Linux exit produced validated XDG JSON |
| Security | ZIP traversal / malformed packages | NOT_STARTED | SIPackages container audit pending |
| Editor | Package fields and metadata | IN_PROGRESS | Name, publisher, contact, arbitrary date/language/restriction, bounded difficulty, authors/sources/tags/comments/showman comments, Unicode SIQ reload, undo/redo, and legacy out-of-range non-normalization are verified by `TypedPackageAndRoundFields_SaveAndReloadWithoutNormalizingFutureValues`, metadata tests, and the compiled inspector test. Package logo and quality-control UI remain pending |
| Editor | Round fields and metadata | VERIFIED | Name, known standard/final actions, unknown future round type preservation, authors/sources/comments/showman comments, undo/redo, compiled bindings, and Unicode SIQ reload pass in the package/round and metadata receipts |
| Editor | Theme fields and metadata | VERIFIED | The canonical theme has name, info, and questions only; name plus authors/sources/comments/showman comments are compiled-inspector and Unicode save/reload tested, while question collection behavior is tracked separately |
| Editor | Question text, price, simple answer lists | VERIFIED | Typed inspector supports complete right/wrong collections with add/edit/move/delete; `FullSimpleAnswerCollections_SaveAndReloadWithoutDroppingEntries`, removal-rule tests, and the headless inspector test cover Unicode/order, last-right protection, compiled bindings, and hiding for client-managed answers |
| Editor | Scenarios/content items | IN_PROGRESS | Existing legacy content and every existing script step are exposed without model conversion; typed content editing plus step add/delete/reorder is verified through Unicode SIQ reload, bidirectional move undo/redo, duplicate-equal identity, and rendered compiled controls by `ScriptCrudEditingTests`, `ScriptSteps_AllParameterKindsAndContentAttributes_SaveAndReload`, and both scenario headless tests. Media picker/link flows remain pending |
| Editor | Parameters / non-text answer types | IN_PROGRESS | Recursive generic editors support create/edit/delete for simple/reference, content, group, and number-set values, preserve opaque future types, and retain self-closing empty parameters. Dedicated numeric, image-backed point, select-option, and client-managed inspectors are verified by controller geometry/keyboard tests, real PNG headless decode/disposal, semantic `x,y,aspect` SIQ reload, `ScriptCrudEditingTests`, `NonTextAnswerEditingTests`, and package parser coverage. Direct key rename/type conversion remains pending |
| Editor | Authors/sources/tags/comments/showman comments | VERIFIED | `TypedMetadataEdits_SaveAndReloadAtEveryPackageLevel`, duplicate/index command and last-package-author tests, plus `Inspector_MetadataEditorsMutateExistingViewModelsThroughCompiledBindings` cover add/edit/move/delete, Unicode SIQ reload, all package levels, compiled bindings, and Russian resources |
| Editor | Add/delete/rename/duplicate/move | IN_PROGRESS | Basic package-item add/delete plus metadata/answer-list move and guarded delete are tested; package-item duplicate/move and focused tests remain |
| Editor | Undo/redo | IN_PROGRESS | Script-question text value/structural-add, script-step type/add/delete/bidirectional move, generic parameter delete, select-option add/delete, and answer-type binding transitions are verified; dynamic script-owned parameter and content listeners share the document operation path, while broader editor coverage remains pending |
| Editor | Search/navigation | NOT_STARTED | Existing cancellable `async void` search requires extraction |
| Editor | Tree mode | VERIFIED | Data-bound hierarchy, expansion, selection/inspector tests, and Linux visual receipt |
| Editor | Flat mode | NOT_STARTED | WPF geometry and drag/drop cannot be translated directly |
| Editor | Clipboard copy/cut/paste | IN_PROGRESS | Versioned item/package payloads, legacy fallback, native Avalonia/WPF adapters, cross-document copy/paste, failure-safe Cut, typed text/file/PNG/custom formats, and shortcut isolation are tested; stable embedded-media lifetime and media-rich cross-process receipt remain |
| Editor | Internal/external drag/drop | NOT_STARTED | Data-level controller pending |
| Editor | Validation/statistics | NOT_STARTED | Existing commands/sidebar inventoried |
| Media | Image add/preview/remove | IN_PROGRESS | Save preservation/count wired; native preview and edit UI pending |
| Media | Audio/video preview | NOT_STARTED | Backend spike recorded in ADR 0004 |
| Media | HTML preview | NOT_STARTED | Controlled WebView policy pending |
| Preview | Question player / JSON protocol | NOT_STARTED | Existing `QuestionPlayViewModel` and `wwwroot` inventoried |
| SPARD | Structural editor | NOT_STARTED | Controller extraction and behavior tests pending |
| Imports | SIQ attachment | NOT_STARTED | `QDocument.ImportSiq` inventoried |
| Imports | Text | NOT_STARTED | `ImportTextViewModel` inventoried |
| Imports | XML | NOT_STARTED | `MainViewModel.ImportXml` inventoried |
| Imports | YAML | NOT_STARTED | `MainViewModel.ImportYaml` inventoried |
| Imports | Question database | NOT_STARTED | `ImportDBStorageViewModel` inventoried |
| Imports | SI Store/package store | NOT_STARTED | `ImportSIStorageViewModel` inventoried |
| Imports | External media download | NOT_STARTED | `DownloadAllExternalMedia` inventoried |
| Exports | Preview image | NOT_STARTED | WPF implementation inventoried |
| Exports | Database / Dinabank | NOT_STARTED | WPF FlowDocument dependency requires neutral model |
| Exports | Table | NOT_STARTED | WPF/XPS dependency requires neutral writer |
| Exports | YAML | NOT_STARTED | Existing serializer inventoried |
| Exports | Steam Workshop | NOT_STARTED | Optional capability only |
| Transform | Competition TV SI | NOT_STARTED | Existing command inventoried |
| Transform | Simplified competition TV SI | NOT_STARTED | Existing command inventoried |
| Transform | Sport SI | NOT_STARTED | Existing command inventoried |
| Transform | Millionaire | NOT_STARTED | Existing command inventoried |
| Transform | Wikify | NOT_STARTED | Existing command inventoried |
| Transform | Theme selection/subpackage | NOT_STARTED | Existing dialog inventoried |
| UX | Tabs/dirty indicator/path tooltip | IN_PROGRESS | Tabs, close buttons, path tooltip implemented; dirty indicator verification pending |
| UX | Empty state | IMPLEMENTED_NOT_VERIFIED | New/Open surface implemented; headless content assertion pending |
| UX | System/light/dark themes | IMPLEMENTED_NOT_VERIFIED | Live host mapping, settings UI and persistence implemented; deterministic light/dark visual receipts pending |
| UX | Russian/English localization | IMPLEMENTED_NOT_VERIFIED | Both RESX sets, Russian resource/converter test, persisted choice and restart notice exist; runtime restart receipt pending |
| UX | Persisted pane sizes | IN_PROGRESS | Two-way navigator/inspector GridLength bindings and JSON round trip pass; splitter interaction receipt pending |
| UX | Core keyboard workflow | IN_PROGRESS | Ctrl+Q runtime route verified; document Ctrl+C and focused-text isolation are headless-tested; routed edit commands support Control/Command and Shift+Z redo, but the complete shortcut matrix and macOS runtime receipt remain |
| UX | Accessibility / 200% scaling | NOT_STARTED | Runtime and visual receipts pending |
| Optional | Secure GPT secret storage | NOT_STARTED | No plaintext downgrade permitted |
| Optional | Steam capability | NOT_STARTED | Must not load native Steam library at startup |
| Optional | Safe update checking | NOT_STARTED | Download-and-execute behavior will not be ported |
| Release | Linux x64 tarball | VERIFIED | Self-contained artifact checksum, byte-reproducibility `cmp`, x86-64 ELF inspection, and native open/exit smoke pass |
| Release | Linux arm64 tarball | IMPLEMENTED_NOT_VERIFIED | Checksum passes and host/Skia payloads are AArch64 ELF; native ARM64 launch receipt pending |
| Release | Debian package / SIQ MIME | IMPLEMENTED_NOT_VERIFIED | amd64 payload launch, apt install simulation, dependency metadata, desktop/MIME/icon paths, checksums, and reproducibility pass; installed file-association receipt pending |
| Release | macOS arm64/x64 app bundles | IMPLEMENTED_NOT_VERIFIED | Both architecture hosts, app topology, licenses, checksums, and SIQ document declarations pass structural checks; native ICNS/codesign/launch pending |
| Release | Cross-platform CI | IMPLEMENTED_NOT_VERIFIED | Build/test matrix plus five-RID artifact matrix and packaged Linux smoke are locally validated; hosted run pending |
| Legacy | Existing WPF application retained | VERIFIED | Existing project unchanged in role and contains no Avalonia dependency |
| Legacy | Existing WPF build remains green | IMPLEMENTED_NOT_VERIFIED | Cross-compile passed with 0 errors; native Windows CI receipt pending |
