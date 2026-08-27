# ADR 0007: SPARD editor strategy

Status: Accepted

Do not port the WPF `RichTextBox`/`FlowDocument` control. First extract and behavior-test a UI-independent parsed-expression/token/caret/selection controller, then render it with an Avalonia control. A temporary plain-text editor may unblock intermediate milestones but cannot satisfy final parity. A third-party editor is acceptable only after maintenance, MIT-compatible licensing, and structural-editing benefit are demonstrated.

## Implemented boundary

The first implementation uses no additional editor dependency. `SpardEditorController` in the platform-neutral view-model assembly owns the parsed expression tree, public token mapping, caret/selection normalization, structural edits, canonical serialization, and malformed-source recovery. Unknown parsed expression kinds are retained as opaque atomic nodes rather than discarded.

`SpardEditorControl` is an Avalonia `TextBox`-derived presentation control whose displayed text is not an independent editor model. Text, key, pointer-selection, and focus-loss operations route through the controller; bypass mutations are restored from controller state. Controller notifications are marshalled to the Avalonia dispatcher, and subscriptions exist only while the control is attached to a visual tree.

`SpardTemplateEditorSession` is the typed adapter over the retained `SpardTemplateViewModel`. It synchronizes the controller and importer template in both directions and routes the existing alias, optional-group, template-variant, cut, copy, and paste commands. The Avalonia importer owns these sessions through visual attach/detach.

`SpardTokenPresenter` is a read-only token projection over that same controller. It distinguishes text, aliases, line tokens, optional-group boundaries, and opaque future expressions by semantic classes; alias metadata may supply the existing color cue, but localized automation names, tooltips, structural borders, and visible nesting markers carry the meaning without color. Rendering is bounded to 256 tokens, long values are bounded for accessibility, and controller subscriptions exist only while the presenter is attached. Unknown or duplicate alias metadata fails safely, with the latest descriptor taking precedence.

No third-party text editor dependency is required. Controller, session, real-importer, localization, bounded-rendering, background-dispatch, and lifecycle tests establish structural behavior parity. Native light/dark/high-contrast visual acceptance remains a release-quality UX receipt rather than a separate editor implementation.
