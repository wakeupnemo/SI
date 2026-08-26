# ADR 0006: Linux and macOS packaging

Status: Accepted

Use `dotnet publish` plus repository-owned deterministic packaging scripts. Linux ships self-contained tarballs and a `.deb` with desktop/MIME integration. macOS ships architecture-specific `.app` bundles with SIQ document metadata. Signing/notarization remains a maintainer release step and is not required for pull requests.
