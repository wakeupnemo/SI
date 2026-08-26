using SIQuester.ViewModel.Contracts.Host;

namespace SIQuester.Desktop.Services;

/// <summary>
/// Preserves in-process cross-document clipboard behavior until the typed native adapter is complete.
/// </summary>
internal sealed class InMemoryClipboardService : IClipboardService
{
    private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);

    public bool ContainsData(string format) => _data.ContainsKey(format);

    public object? GetData(string format) => _data.TryGetValue(format, out var value) ? value : null;

    public void SetData(string format, object data) => _data[format] = data;
}
