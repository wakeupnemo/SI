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
- Added optional answer-time-limit authoring.
- Preserved unknown question types and parameters during ordinary open/save.
  Choosing a known behavior is an explicit converting edit and remains
  undoable.

The canonical `SIPackages.Question` type, parameters, ordered question content,
optional `answer` content, answers, and media remain the only persisted model.
No parallel model or alternate serializer was introduced.

## Verification

- Release test suite: 350 passed, 0 failed, 0 skipped
  (`SIPackages.Tests` 96, `SIQuester.ViewModel.Tests` 198,
  `SIQuester.Avalonia.Tests` 56).
- `DemoBehaviors_CreatedFromNewPackageCommands_SaveAndReloadCanonicalSemantics`
  creates representative demo-equivalent questions from a new package through
  the retained command/property paths, safely saves them, reloads through
  `SIDocument.Load`, and verifies types, parameters, content order, answers,
  media references, and exact media bytes.
- Focused tests verify unknown-value preservation, one-step Secret price undo,
  Secret-family switching, compiled UI command routing, contextual controls,
  Russian resources, and accessible names.
- The retained WPF SIQuester project cross-builds on Linux with Windows
  targeting enabled: 0 errors, 0 warnings. Native Windows execution remains a
  Windows-runner boundary.
- Native Debian 13/X11 tar smoke opened a Unicode/space SIQ path, authored a
  Secret question with fixed price 700, added post-answer text, saved twice,
  closed, reopened, and closed cleanly. Both saves were validated and committed;
  no error, fatal, or unhandled-exception entry was logged. The receipt is under
  `artifacts/siquester-cross-platform-v0.2.0/native-smoke/`.

## Linux artifacts

- `SIQuester-0.2.0-linux-x64.tar.gz`
- `siquester_0.2.0_amd64.deb`
- `SIQuester-0.2.0-linux-x64.SHA256SUMS`

The Debian package includes the desktop entry, icon, `.siq` MIME registration,
license notices, native dependency metadata, and WebKit/GStreamer recommendations.

## Deferred boundaries

This focused release does not add advanced imports/exports, transformations,
Steam, GPT, updater behavior, package HTML execution, or another preview
backend. Custom/manual questions are preserved and selectable, but their
application-specific semantics are intentionally not invented. Native macOS,
Windows, ARM64, installed-DEB file-association, and hosted-CI receipts remain
separate environment-dependent verification work.
