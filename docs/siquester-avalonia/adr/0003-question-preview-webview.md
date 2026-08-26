# ADR 0003: Question preview WebView

Status: Accepted in principle; package/backend validation pending

Reuse `QuestionPlayViewModel`, `IWebInterop`, the existing `wwwroot` assets, and JSON protocol behind `IQuestionPreviewService`. Use the official open-source Avalonia WebView package only after a focused version/license/runtime spike. The host uses an application-owned local origin, blocks external navigation/popups, validates media URIs, and degrades without failing startup.
