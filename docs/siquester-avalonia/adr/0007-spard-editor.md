# ADR 0007: SPARD editor strategy

Status: Accepted

Do not port the WPF `RichTextBox`/`FlowDocument` control. First extract and behavior-test a UI-independent parsed-expression/token/caret/selection controller, then render it with an Avalonia control. A temporary plain-text editor may unblock intermediate milestones but cannot satisfy final parity. A third-party editor is acceptable only after maintenance, MIT-compatible licensing, and structural-editing benefit are demonstrated.

## Implemented boundary

The first implementation uses no additional editor dependency. `SpardEditorController` in the platform-neutral view-model assembly owns the parsed expression tree, public token mapping, caret/selection normalization, structural edits, canonical serialization, and malformed-source recovery. Unknown parsed expression kinds are retained as opaque atomic nodes rather than discarded.

`SpardEditorControl` is an Avalonia `TextBox`-derived presentation control whose displayed text is not an independent editor model. Text, key, pointer-selection, and focus-loss operations route through the controller; bypass mutations are restored from controller state. Controller notifications are marshalled to the Avalonia dispatcher, and subscriptions exist only while the control is attached to a visual tree.

This establishes behavior and lifecycle seams without taking a dependency on a rich-text component. It does not complete parity: the Avalonia text-import workspace still needs a typed adapter for `SpardTemplateViewModel`, localized alias/optional actions, template synchronization, and deliberate token-level presentation.
