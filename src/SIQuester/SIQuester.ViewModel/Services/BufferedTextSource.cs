using SIQuester.ViewModel.Contracts;
using System.Text;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Provides repeatable text decoding for a stream-only file selected through a desktop portal.
/// </summary>
internal sealed class BufferedTextSource : ITextSource
{
    private readonly byte[] _content;

    public string? FileName { get; }

    public BufferedTextSource(string fileName, byte[] content)
    {
        FileName = fileName;
        _content = content;
    }

    public string GetText(Encoding encoding)
    {
        using var stream = new MemoryStream(_content, writable: false);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public void Dispose() { }
}
