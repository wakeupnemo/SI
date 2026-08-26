# ADR 0002: Platform-service decomposition

Status: Accepted

Do not reproduce `PlatformManager` for Avalonia. Add narrow asynchronous contracts as real workflows are migrated, beginning with file picking, dialogs, lifetime, app paths, and safe persistence. WPF adapters reuse `DesktopManager`; Avalonia adapters remain in the frontend/host. No compatibility global is created.
