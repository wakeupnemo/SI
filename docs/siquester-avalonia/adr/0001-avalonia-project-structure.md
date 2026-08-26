# ADR 0001: Avalonia project structure

Status: Accepted

Use `SIQuester.Avalonia` for reusable framework UI and `SIQuester.Desktop` for the executable/lifetime/native composition. Keep the WPF project intact. This permits headless UI testing without launching a desktop host and prevents platform composition from leaking into views or the view-model/domain layers.
