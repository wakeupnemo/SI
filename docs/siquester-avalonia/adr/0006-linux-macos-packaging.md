# ADR 0006: Linux and macOS packaging

Status: Accepted

Use `dotnet publish` plus repository-owned deterministic packaging scripts. Linux ships self-contained tarballs and a `.deb` with desktop/MIME integration. macOS ships architecture-specific `.app` bundles with SIQ document metadata. Signing/notarization remains a maintainer release step and is not required for pull requests.

Package builders stage into private temporary directories and publish output files atomically only after structural validation. `SOURCE_DATE_EPOCH`, sorted archive members, normalized ownership, and timestamp-free gzip headers provide reproducible Linux artifacts. Immutable Linux payloads live under `/usr/lib/siquester`; all mutable state remains in platform user directories. Native WebView/media dependencies are optional and must not be loaded by the core editor at startup.

The CI app bundles are ad-hoc signed on macOS and architecture-specific. Developer ID signing, notarization, and stapling are separate maintainer operations so pull requests require no secrets. The existing WPF MSI remains the authoritative Windows installer until feature parity is independently verified.
