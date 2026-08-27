# ADR 0004: Audio/video preview backend

Status: Accepted

## Decision

Use the official MIT-licensed Avalonia WebView adapter and an application-owned HTML5 media page behind `IMediaPreviewService`. Reuse the bounded randomized loopback origin already required by question preview, but give media-library playback a separate page and session contract. Each selected audio/video item gets an opaque extension-preserving route, range support, a 512 MiB bound, and deterministic removal. The Avalonia view owns one native control for the current selection and stops it before disposing the route on selection, detach, document close, or application exit. Backend probing remains lazy and missing WebKit/WebView2 is a localized non-fatal capability state.

The application preserves original package bytes; preview never transcodes or replaces media. The page accepts only an audio/video kind and an opaque same-origin URL in its fragment. It rejects other origins and paths, blocks top-level navigation and popups, and relies on the native HTML5 controls for play/pause, seek, and volume. Stopping is deterministic when the current session is replaced or closed.

## Spike result

1. Controlled WebView reuse required no second native runtime and already had MIME allowlisting, bounded byte ranges, navigation policy, backend diagnostics, and tested disposal. Native WebKitGTK 2.52.6 decoded the repository's 52,079-byte MP3 to 0:03 and rendered changing frames from its 1,046,987-byte MP4.
2. LibVLCSharp would add LGPL/native distribution, architecture packaging, airspace, initialization, and lifecycle work without improving the verified core formats enough to justify it now.
3. An external-player fallback cannot provide embedded controls, cannot enforce the same package-content boundary, and transfers user media to another process. It remains a possible explicit future action, not the default preview.

Codec coverage is supplied by each platform's native WebView/media stack. Linux packaging recommends WebKitGTK plus GStreamer base/good/bad/ugly/libav plugin sets; missing codecs fail only the selected preview. Native macOS and Windows codec acceptance remains a release-runner verification boundary.
