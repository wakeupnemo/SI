# SIQuester cross-platform architecture

## Boundaries

```text
SIPackages
  canonical model, XML and SIQ media persistence
       ^
SIQuester.ViewModel
  editor item/workspace VMs, commands, neutral service contracts
       ^
SIQuester.Avalonia
  reusable views, controls, styles, localization, headless-testable UI
       ^
SIQuester.Desktop
  executable, Avalonia lifetime, composition, native adapters, diagnostics
```

The existing `SIQuester` WPF project remains a sibling frontend. It references the view-model layer and supplies WPF adapters; it never references Avalonia.

## Composition and ownership

- `SIQuester.Desktop` creates and owns the service provider, application lifetime, top-level window, logs, native adapters, versioned settings store, and autosave timer.
- Views receive view models through construction/composition. They do not resolve services or reach through `Application.Current` to a window data context.
- `QDocument` remains the document workspace. The port binds to its item view models rather than building a second package-editing model.
- Framework-specific behavior is limited to views/controls and desktop adapters.

## Persistence

The save transaction is: create a unique temporary file in the destination directory; serialize package XML and staged media; flush; load with `SIDocument.Load`; release the old package container; atomically replace when supported; otherwise preserve a backup and use same-filesystem renames with rollback; reload the committed path; only then clear staged media and the dirty flag.

ZIP byte equality is not a compatibility oracle. Tests compare model semantics, media names, and media hashes.

Recovery uses a separate non-mutating snapshot path. Each document has a random recovery ID under the platform state directory. A complete SIQ generation is flushed, loaded, and hashed before schema-versioned metadata atomically points to it; the previous generation remains authoritative until that pointer commit. Autosave, canonical save, and close cleanup share the per-document lock. Capable hosts expose the validated inventory as per-entry view models; stale snapshots restore with an empty canonical path so Save must choose a new destination. See ADR 0008.

## Platform contracts

Contracts are introduced only for real seams: file selection, dialogs, lifetime, paths, settings/secrets, launcher, dispatcher, capabilities, persistence/materialization, preview, media preview, and export. Picker results expose neutral metadata and stream operations, not Avalonia storage objects. `IExternalLauncher` owns trusted shell reveal operations; `IPlatformCapabilities` lets Avalonia opt into the recovery center while WPF retains its compatible startup prompt without frontend-type checks in the view-model layer.

## UI structure

The initial shell uses Fluent theme plus semantic tokens. A command surface and document tabs surround a three-pane document editor: hierarchy, typed editor, and inspector/media context. Pane sizes and settings are persisted outside the package.

Scenario controls adapt the existing canonical objects instead of flattening them. Legacy question content remains in the top-level question parameters, while scripted questions expose one `ScriptStepViewModel` per existing `SIPackages.Step`; recursive parameter controls reuse `StepParametersViewModel` for simple/reference, content, group, and number-set values. Script-owned models are attached to the same document operation listeners as legacy parameters. Package cloning and XML parsing retain complete scripts before a save transaction is allowed to validate and commit them.

`ScriptStepsViewModel` is the ordered mutation boundary for scripts. It mirrors add/remove/move operations into the canonical `Script.Steps` list by index, preserving identity even when two steps compare equal, while `QDocument` attaches and detaches change listeners as wrappers enter or leave the collection. `StepParametersViewModel` similarly owns named parameter creation/deletion and recursive wrappers without converting unknown future parameter types; opaque types remain simple-value editable and serialize with their original type discriminator.

Package and round inspectors bind safe scalar fields directly to canonical models so the existing `QDocument` property listener remains the undo/dirty authority. Round type adds only a small command for known standard/final choices; the raw value remains editable so newer type discriminators are not normalized or dropped. Package quality-control enablement is intentionally withheld until its validation failure path is migrated from the WPF platform global to the neutral dialog service.

Answer inspectors follow the same adapter rule. Avalonia hosts the existing `NumericAnswerViewModel`, `PointAnswerViewModel`, answer collections, and grouped option parameters rather than creating a second answer model. `QuestionViewModel` observes the canonical answer-type parameter so undo/redo updates typed visibility and disposes point-editor subscriptions when that mode is left. Valid select options survive idempotent type selection; malformed option state remains repairable through the existing command.

Image-backed point selection retains the existing neutral `PointAnswerViewModel.SelectPointRequest` seam used by WPF. `PointSelectionController` contains only normalized coordinate, aspect, deviation, letterbox-layout, and keyboard-nudge logic; it has no Avalonia types. The Avalonia window owns pointer/focus behavior and bounded asynchronous bitmap decoding, and commits to the canonical view model only after explicit acceptance. Cancel and load failure leave package data unchanged, while close deterministically releases the source stream, decoded bitmap, cancellation source, and subscriptions.

## Security boundaries

- Package data is never executed.
- Web content uses an application-owned origin and explicit navigation policy.
- External links require an explicit launcher action.
- Secrets use OS secure storage or remain memory-only.
- Native preview and Steam dependencies load only after capability checks and explicit use.
