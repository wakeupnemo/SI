# ADR 0003: Question preview WebView

Status: Accepted; protocol/capability foundation implemented, native backend validation pending

Reuse `QuestionPlayViewModel`, `IWebInterop`, the existing `wwwroot` assets, and JSON protocol behind `IQuestionPreviewService`. Keep protocol records and capability descriptors in the platform-neutral view-model assembly; native controls and lifecycle belong to the frontend/desktop host.

The implemented foundation provides typed records for every retained outbound JSON message and central camel-case serialization. `QuestionPreviewHostDescriptor` exposes only local-file or loopback HTTP(S) application sources and represents missing backend/assets without a URI. The WPF adapter resolves its existing application-owned `wwwroot/index.html`; Avalonia currently registers an explicit backend-unavailable capability, logs it at startup, and renders a localized non-fatal state. Playback commands stay disabled when no backend is available. Replaced or closed dialogs detach subscriptions so stale preview windows cannot clear newer dialogs.

Use the official open-source Avalonia WebView package only after a focused stable-version, license, Linux backend, and native-runtime spike. The eventual Avalonia host must serve application-owned assets from a controlled loopback origin, block unexpected navigation and popups, validate/materialize package media through explicit services, keep inbound messages narrowly parsed, and detach/dispose all native events and resources. Backend absence must never prevent application startup or ordinary editing.

The protocol and capability tests do not establish native preview parity. Remaining acceptance requires Linux dependency diagnostics, bidirectional bridge tests, replay and answer-option runtime receipts, external-link policy, repeated open/close process and memory measurements, and hosted Linux/macOS/Windows execution.
