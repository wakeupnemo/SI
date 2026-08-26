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

- `SIQuester.Desktop` creates and owns the service provider, application lifetime, top-level window, logs, and native adapters. It is the composition boundary for the versioned settings store that remains to be implemented.
- Views receive view models through construction/composition. They do not resolve services or reach through `Application.Current` to a window data context.
- `QDocument` remains the document workspace. The port binds to its item view models rather than building a second package-editing model.
- Framework-specific behavior is limited to views/controls and desktop adapters.

## Persistence

The save transaction is: create a unique temporary file in the destination directory; serialize package XML and staged media; flush; load with `SIDocument.Load`; release the old package container; atomically replace when supported; otherwise preserve a backup and use same-filesystem renames with rollback; reload the committed path; only then clear staged media and the dirty flag.

ZIP byte equality is not a compatibility oracle. Tests compare model semantics, media names, and media hashes.

## Platform contracts

Contracts are introduced only for real seams: file selection, dialogs, lifetime, paths, settings/secrets, launcher, dispatcher, capabilities, persistence/materialization, preview, media preview, and export. Picker results expose neutral metadata and stream operations, not Avalonia storage objects.

## UI structure

The initial shell uses Fluent theme plus semantic tokens. A command surface and document tabs surround a three-pane document editor: hierarchy, typed editor, and inspector/media context. Pane sizes and settings are persisted outside the package.

## Security boundaries

- Package data is never executed.
- Web content uses an application-owned origin and explicit navigation policy.
- External links require an explicit launcher action.
- Secrets use OS secure storage or remain memory-only.
- Native preview and Steam dependencies load only after capability checks and explicit use.
