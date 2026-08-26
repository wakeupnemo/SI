# ADR 0004: Audio/video preview backend

Status: Spike required

Evaluate in order: controlled reuse of the question WebView, LibVLCSharp Avalonia, and explicit system-player fallback. Select the least complex reliable open-source option. Do not use paid Avalonia media components. Native libraries load only on demand and all players are explicitly stopped/disposed with document/preview closure.
