# SIQuester Cross-Platform 0.2.1

SIQuester Cross-Platform 0.2.1 is a focused Linux responsiveness and
persistence hardening update. It adds no question behavior, changes no SIQ
format semantics, and does not change the retained Windows WPF product version
6.9.1.

## Changes

- Removed synchronous document-lock waits from validation file-size checks,
  package-quality checks, media-aware copy, and media collision checks during
  paste. These paths now acquire package streams asynchronously and support
  document-lifetime cancellation.
- Made Avalonia media clipboard payloads self-contained without invoking the
  WPF-only media materializer. Legacy-compatible temporary paths remain
  available while their source document is open, and tested ownership rules
  clean failed replacements and close/write races.
- Moved ZIP finalization, durable file flush, and canonical pre/post-commit SIQ
  validation away from the UI thread. Canonical saves no longer parse the same
  temporary package twice; recovery snapshots retain independent validation.
- Preserved validation-before-replace, same-directory staging, rollback,
  recoverable backup, pending-media acceptance, and late-cancellation safety.

## Verification

- Cross-platform Release build: 0 warnings, 0 errors.
- Release tests: 370 passed, 0 failed:
  - `SIPackages.Tests`: 96;
  - `SIQuester.ViewModel.Tests`: 213;
  - `SIQuester.Avalonia.Tests`: 61.
- Retained WPF SIQuester cross-build with Windows targeting: 0 warnings,
  0 errors. Native Windows execution remains an environment boundary.
- Focused regressions hold the document persistence lock while validation,
  quality checks, media copy, and media paste begin; all operations yield and
  complete after lock release instead of synchronously blocking the caller.
- Clipboard regressions verify canonical all-media copy/save/reload, WPF
  fallback lifetime, failed replacement cleanup, and close after clipboard
  acceptance but before caller continuation.
- The bounded managed soak passed 20 warm-up plus 50 measured cycles: p50
  8.9 ms, p95 21.8 ms, max 23.7 ms, zero retained package descriptors, threads
  21 to 24, and file descriptors 197 to 201.
- Current-code native Debian 13/X11 review used a 104 MiB package. WebKitGTK
  2.52.6 rendered controlled package media, replayed, and disposed 2/2 preview
  sessions. A 60-second selection/preview/media-copy trace contained no
  synchronous document-lock or media-open stack on the UI thread; its largest
  sampled managed UI interval was 236.6 ms. The user accepted this native
  review. At the user's request, no additional GUI automation was run against
  the version-only rebuilt packages.

## Linux artifacts

The artifacts were built reproducibly from implementation commit
`32a62910af0e3ef492ffb7b4a0e1789f4084532b` with that commit timestamp as
`SOURCE_DATE_EPOCH`.

- `SIQuester-0.2.1-linux-x64.tar.gz` — 59,018,645 bytes
- `siquester_0.2.1_amd64.deb` — 41,839,500 bytes
- `SIQuester-0.2.1-linux-x64.SHA256SUMS`

```text
db306d1ee9433a8aeed8faf8757f624aa190e726f76796ac1bffea7feb3e4b6d  SIQuester-0.2.1-linux-x64.tar.gz
a206a354574c5d6195b149d9f2cadeaa8a105527a180697e1a7dcf1d89fc9eab  siquester_0.2.1_amd64.deb
```

Checksum verification, tar/DEB topology, Debian package/version/architecture
metadata, launcher shell syntax, Wayland/NWayland/WebView payloads, notices,
desktop/MIME metadata, and byte-identical tar/DEB apphosts pass. Portable users
should launch `./siquester`, which applies the Debian WebKitGTK compositing
compatibility default before apphost initialization while preserving any
explicit user value.

## Deferred boundaries

This release deliberately does not add advanced imports/exports,
transformations, Steam, GPT, updater behavior, package HTML execution, or a new
preview backend. Native Wayland-session, macOS, Windows, ARM64, installed-DEB
file-association, and same-window internal Xdnd acceptance remain explicit
future environment work. Existing documented demo-question authoring and
ordinary Linux package workflows are unchanged.
