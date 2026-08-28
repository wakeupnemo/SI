# SIQuester Cross-Platform 0.2.0

SIQuester Cross-Platform 0.2.0 makes every semantically distinct question
behavior demonstrated by `SIGameTestNew.siq` authorable through the normal
Avalonia inspector. It does not change the SIQ format or the retained Windows
SIQuester product version.

## Authoring changes

- Added a localized high-level question behavior selector for round default,
  simple, stake, all-in stake, secret, public-price secret, secret without a
  question, no-risk, for-everyone, and custom/manual questions.
- Added contextual Secret controls for announced theme, recipient eligibility,
  and fixed, round-min/max, endpoint, or stepped-range price choices.
- Added ordinary post-answer content authoring using the existing canonical
  content/media editor. Text, image, audio, and video keep the existing media,
  undo, dirty-tracking, and safe-save paths.
- Replaced the technical content-item list with a localized presentation
  storyboard. Consecutive moments play left to right; screen, showman, and
  background lanes make simultaneous content explicit. Authors can join or
  split moments and move complete moments without editing `WaitForFinish`.
- Kept storyboard selection, command targeting, and visible highlighting in
  sync across initial load and moment-move undo/redo.
- Added optional answer-time-limit authoring.
- Preserved unknown question types and parameters during ordinary open/save.
  Choosing a known behavior is an explicit converting edit and remains
  undoable.

The canonical `SIPackages.Question` type, parameters, ordered question content,
optional `answer` content, answers, and media remain the only persisted model.
No parallel model or alternate serializer was introduced.

## Release hardening

- Serialized overlapping application-close requests and removed the duplicate
  document-close pass from the close command. Settings and document close now
  form one guarded window-close flow; any failure is persisted, shown once, and
  leaves the window and dirty data open.
- Failed and corrupted ordinary opens now remove loader workspaces and restore
  selection deterministically, including default/non-cancellable token paths.
- Persistent XDG logs now include app version, session identifier, .NET runtime,
  OS and architecture, original operation exceptions, Avalonia Error/Fatal
  diagnostics, and last-resort process failures. Transient binding warnings and
  the exact benign Debian IBus `Destroy` shutdown diagnostic are excluded.
- Added bounded managed and native stability procedures covering repeated
  package/preview/media/edit/autosave/save/reload/close operations and settled
  process/RSS/thread/file-descriptor measurements.
- Added the official Avalonia Wayland backend with automatic X11 fallback.
- Moved the Debian 13 WebKitGTK compositing default into the packaged Linux
  launcher so it is present before native apphost initialization. Explicit user
  values remain authoritative; portable tar users should run `./siquester`.

## Verification

- Release test suite: 362 passed, 0 failed, 0 skipped
  (`SIPackages.Tests` 96, `SIQuester.ViewModel.Tests` 205,
  `SIQuester.Avalonia.Tests` 61). An explicit 20-warm-up/50-measured-cycle
  managed soak also passed.
- `DemoBehaviors_CreatedFromNewPackageCommands_SaveAndReloadCanonicalSemantics`
  creates representative demo-equivalent questions from a new package through
  the retained command/property paths, safely saves them, reloads through
  `SIDocument.Load`, and verifies types, parameters, content order, answers,
  media references, and exact media bytes.
- Focused tests verify unknown-value preservation, one-step Secret price undo,
  Secret-family switching, compiled UI command routing, contextual controls,
  Russian resources, and accessible names.
- The retained WPF SIQuester project cross-builds on Linux with Windows
  targeting enabled: 0 errors and 130 existing warnings. Native Windows
  execution remains a Windows-runner boundary.
- Native Debian 13/X11 tar smoke opened a Unicode/space SIQ path, authored a
  Secret question with fixed price 700, added post-answer text, saved twice,
  closed, reopened, and closed cleanly. Both saves were validated and committed;
  no error, fatal, or unhandled-exception entry was logged. The receipt is under
  `artifacts/siquester-cross-platform-v0.2.0/native-smoke/`.
- The earlier tar built from hardening commit `ebf5e604` completed 50 native
  question-preview open/close cycles plus a 10-second settle. All 51 sessions
  were disposed, process counts remained stable, settings/document close
  completed, and the persistent log contained no warning, error, fatal, or
  unhandled entry. Raw evidence is under
  `artifacts/siquester-cross-platform-v0.2.0/stability/`.
- Those lifecycle cycles did not verify rendered pixels. A later affected-host
  run exposed a blank WebKitGTK surface with NVIDIA GBM/KMS errors, which is why
  the rebuilt artifacts apply the compatibility value in the pre-apphost
  launcher. The user subsequently confirmed that preview renders through that
  launcher on the affected Debian/NVIDIA desktop.

## Linux artifacts

The hashes below identify the GitHub-hosted Linux package job output from
checkpoint `a6bf38a7`, including the storyboard selection fix. The exact tarball
also passed the packaged startup/open/clean-close smoke; the dedicated WebKitGTK
question-preview job passed on the same commit.

- `SIQuester-0.2.0-linux-x64.tar.gz`
- `siquester_0.2.0_amd64.deb`
- `SIQuester-0.2.0-linux-x64.SHA256SUMS`

Final SHA-256:

```text
958a08f6c163c707f3f088c40d6636a6bb1779d239b058960f6c54319060c8ca  SIQuester-0.2.0-linux-x64.tar.gz
81a25958eba83557cb3c52acc56d677b5e9d985ff7f123b49dd99450ee41dc42  siquester_0.2.0_amd64.deb
```

The Debian package includes the desktop entry, icon, `.siq` MIME registration,
license notices, native dependency metadata, and WebKit/GStreamer recommendations.

The broader hosted matrix also built Linux ARM64, macOS ARM64/x64, and Windows
x64 artifacts, but they are not release assets because native acceptance for
those platforms remains deferred. The aggregate run is not claimed green:
Linux Xdnd receipt launch failed on a missing executable bit, and Windows
cross-platform save tests exposed a separate Windows-only verification gap.

## Deferred boundaries

This focused release does not add advanced imports/exports, transformations,
Steam, GPT, updater behavior, package HTML execution, or another preview
backend. Custom/manual questions are preserved and selectable, but their
application-specific semantics are intentionally not invented. Native macOS,
Windows, ARM64, installed-DEB file-association, and hosted-CI receipts remain
separate environment-dependent verification work.
