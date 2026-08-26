# SIQuester Avalonia port plan

## Objective

Deliver a native Avalonia 12 SIQuester for Linux, macOS, and Windows while retaining the existing WPF application and the canonical `SIPackages` model and SIQ serialization.

## Verified starting point

- Repository baseline: `8c2bee9c38d250884a3e575809172e626448a0f9` on `feat/siquester-avalonia`.
- Portable libraries already target `net10.0`; the WPF host targets `net10.0-windows7.0`.
- `SIQuester.ViewModel.Tests` targets `net10.0-windows` and discovers no tests on Linux.
- `QDocument` owns persistence, media staging, search, import/export, transformations, preview, and editor commands.
- The view-model assembly contains 84 direct `PlatformManager.Instance` usages and 31 `async void` methods.
- Existing save-as truncates its destination before a replacement package is validated. Existing overwrite fallback is non-atomic.
- GitHub Actions builds only on Windows; the SIQuester release workflow still installs .NET 7.

## Migration rules

1. Preserve `SIDocument`, package models, XML, and media bytes as the source of truth.
2. Keep WPF buildable and provide WPF adapters for every extracted platform seam it consumes.
3. Move one tested workflow at a time away from `PlatformManager`; never introduce an Avalonia compatibility global.
4. Keep UI framework types out of `SIQuester.ViewModel` and `SIPackages`.
5. Validate temporary SIQ files before committing them to their destination.
6. Keep every milestone buildable and record evidence in `PORT_STATUS.md` and `FEATURE_PARITY.md`.

## Milestone sequence

### M0 - baseline and build boundary

- Add `SIQuester.CrossPlatform.sln`.
- Add reusable `SIQuester.Avalonia`, executable `SIQuester.Desktop`, and headless test projects.
- Pin the latest stable compatible Avalonia 12 release.
- Correct repository-root MSBuild paths for non-Windows hosts.
- Add durable architecture, parity, build, packaging, and ADR documents.

### M1 - platform-neutral foundation

- Add asynchronous file picker, dialog, lifetime, app-path, dispatcher, capability, and safe-persistence seams.
- Retarget view-model tests to `net10.0` and restore real Linux discovery.
- Migrate create/open/save/save-as/close to injected services.
- Implement versioned atomic settings and platform-conventional paths.
- Add WPF and Avalonia adapters.

### M2 - usable native editor

- Build the document shell, tabs, hierarchy, typed inspectors, theme/language controls, and status surface.
- Support package/round/theme/question creation and deletion plus common text and answer editing.
- Verify create, save, reload, semantic comparison, media hashes, Unicode paths, and failure preservation.
- Run a Linux startup smoke test and retain its logs and SIQ compatibility receipt.

### M3 - core parity

- Complete metadata, parameters, scenarios, media, validation, undo/redo, search, recent files, autosave/recovery, clipboard, tree/flat reordering, and drag/drop.
- Add stress fixtures, race tests, keyboard tests, and deterministic visual snapshots.

### M4 - advanced parity

- Add controlled question WebView preview, image/audio/video/HTML preview, structural SPARD control, imports, exports, transformations, GPT, optional Steam, and safe update checking.
- Preserve JSON protocol and export semantics with platform-neutral tests.

### M5 - release quality

- Finish accessibility, localization, performance, lifecycle/leak review, cross-platform CI, package associations, `.deb`, tarballs, macOS bundles, and release documentation.
- Run final adversarial data-loss, dependency-license, DESLOP, and architecture reviews.

## Review gates

Every milestone requires: applicable builds/tests, `git diff --check`, full diff inspection, searches for placeholders/global platform calls/hardcoded UI text/`async void`, disposal and path review, dependency-license review, and a focused data-loss review.
