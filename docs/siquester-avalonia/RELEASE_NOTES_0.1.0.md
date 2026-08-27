# SIQuester Cross-Platform 0.1.0

SIQuester Cross-Platform 0.1.0 is the first usable native Linux prototype. It is an additional Avalonia application and does not replace or re-version the Windows WPF SIQuester product.

## Included

- Native Linux package creation, open, hierarchy editing, typed package/round/theme/question inspectors, safe save/save-as, close, and semantic reload.
- Authors, sources, tags, comments, showman comments, answers, scenarios, parameters, validation, search, undo/redo, clipboard, recovery, and image/media authoring for ordinary packages.
- Tree and flat workspaces, native external file drops, English/Russian resources, system/light/dark themes, keyboard command routing, and a focused 200% scaling/accessibility pass.
- Controlled question and audio/video preview with an actionable non-fatal state when WebKitGTK or codecs are unavailable.
- Self-contained Linux x64 tarball and Debian amd64 package with `.siq` desktop/MIME registration.

## Install

Extract the tarball and run `SIQuester.Desktop`, or install the Debian package with `sudo apt install ./siquester_0.1.0_amd64.deb`. See `BUILDING.md` and `PACKAGING.md` for native preview dependencies.

## Deferred beyond 0.1.0

Advanced and legacy parity remains future work: XML/YAML/database/store imports, exports and transforms, Steam, GPT, updater, package HTML execution, native internal Xdnd acceptance, exhaustive shortcut/macOS receipts, installed desktop-cache acceptance, and native macOS/Windows execution. The original WPF editor remains the authority for those workflows.
