# ADR 0007: SPARD editor strategy

Status: Accepted

Do not port the WPF `RichTextBox`/`FlowDocument` control. First extract and behavior-test a UI-independent parsed-expression/token/caret/selection controller, then render it with an Avalonia control. A temporary plain-text editor may unblock intermediate milestones but cannot satisfy final parity. A third-party editor is acceptable only after maintenance, MIT-compatible licensing, and structural-editing benefit are demonstrated.
