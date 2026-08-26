# SIQuester feature parity

Statuses are limited to `VERIFIED`, `IMPLEMENTED_NOT_VERIFIED`, `IN_PROGRESS`, `NOT_STARTED`, `BLOCKED`, and `NOT_APPLICABLE`.

| Area | Feature | Status | Evidence / boundary |
|---|---|---:|---|
| Runtime | Native Linux startup | VERIFIED | 12-second Release Xvfb smoke plus visible 1200x760 window, command-line SIQ open, application-owned Ctrl+Q shutdown, exit 0, and clean log |
| Runtime | Native macOS startup | NOT_STARTED | Requires macOS CI/runtime receipt |
| Runtime | Avalonia Windows startup | NOT_STARTED | WPF remains the production Windows editor |
| Documents | New package | IMPLEMENTED_NOT_VERIFIED | Existing `NewViewModel` is hosted by Avalonia; end-to-end UI receipt pending |
| Documents | Open SIQ | VERIFIED | Command-line open of compatibility artifact plus startup log and visual receipt |
| Documents | Recent files / reopen | NOT_STARTED | Existing WPF behavior inventoried |
| Documents | Save | VERIFIED | `SaveDocument_*` tests and `SIDocument.Load` compatibility artifact |
| Documents | Save As | IMPLEMENTED_NOT_VERIFIED | Neutral picker and safe transaction are wired; UI picker receipt pending |
| Documents | Save All | IMPLEMENTED_NOT_VERIFIED | Async neutral flow implemented; multi-document UI test pending |
| Documents | Close save/discard/cancel | IN_PROGRESS | Typed dialog and lifetime seams implemented; empty-document close/reentrancy is headless-tested, dirty multi-document state matrix pending |
| Documents | Templates | NOT_STARTED | Existing built-in/custom templates inventoried |
| Documents | Autosave and recovery | NOT_STARTED | Existing XML/media-change autosave inventoried |
| Data | SIDocument semantic round trip | VERIFIED | `SaveDocument_WithComplexStructure_ShouldPreserveAllData` and compatibility artifact |
| Data | Media-byte round trip | VERIFIED | all-four-collection Unicode test plus artifact image hash comparison |
| Data | Unicode/long/space paths | IN_PROGRESS | Unicode/space/non-ASCII media names verified; generated long-path stress fixture pending |
| Data | Save failure preserves destination | VERIFIED | cancellation-before-write and injected post-commit validation-failure byte-rollback tests |
| Data | Versioned settings persistence | VERIFIED | Nine `SettingsStoreTests`: legacy/current/corrupt/invalid/future/cancel/replacement; graceful Linux exit produced validated XDG JSON |
| Security | ZIP traversal / malformed packages | NOT_STARTED | SIPackages container audit pending |
| Editor | Package fields and metadata | IN_PROGRESS | Name/comments inspector works; complete metadata pending |
| Editor | Round fields and metadata | IN_PROGRESS | Name/comments inspector and add/delete commands implemented |
| Editor | Theme fields and metadata | IN_PROGRESS | Name/comments inspector and add/delete commands implemented |
| Editor | Question text, price, answers | VERIFIED | Typed inspector plus semantic edit/save/reload test; runtime package inspector visually verified |
| Editor | Scenarios/content items | NOT_STARTED | Full typed editor pending |
| Editor | Parameters / answer types | NOT_STARTED | Full typed editor pending |
| Editor | Authors/sources/tags/comments/showman comments | IN_PROGRESS | Comments exposed; remaining typed metadata editors pending |
| Editor | Add/delete/rename/duplicate/move | IN_PROGRESS | Basic add/delete wired; duplicate/move and focused tests pending |
| Editor | Undo/redo | IN_PROGRESS | script-question text value and structural-add undo/redo verified; broader editor coverage pending |
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
| Release | Linux x64/arm64 tarballs | NOT_STARTED | Packaging scripts pending |
| Release | Debian package / SIQ MIME | NOT_STARTED | Packaging scripts pending |
| Release | macOS arm64/x64 app bundles | NOT_STARTED | Packaging scripts pending |
| Release | Cross-platform CI | IMPLEMENTED_NOT_VERIFIED | Linux/macOS/Windows matrix and graceful Linux open/exit/settings smoke added and locally reproduced; hosted run pending |
| Legacy | Existing WPF application retained | VERIFIED | Existing project unchanged in role and contains no Avalonia dependency |
| Legacy | Existing WPF build remains green | IMPLEMENTED_NOT_VERIFIED | Cross-compile passed with 0 errors; native Windows CI receipt pending |
