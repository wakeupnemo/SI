using SIQuester.ViewModel.Services;
using System.Text.Json;

namespace SIQuester.ViewModel.Serializers;

/// <summary>
/// Serializes flat-editor drag data without transferring live view-model objects.
/// </summary>
public static class FlatQuestionDragDataSerializer
{
    public const int CurrentVersion = 1;
    public const int MaximumPayloadCharacters = 32 * 1024;

    /// <summary>
    /// Serializes drag data using the current payload version.
    /// </summary>
    public static string Serialize(FlatQuestionDragData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return JsonSerializer.Serialize(new Payload(CurrentVersion, data));
    }

    /// <summary>
    /// Deserializes supported drag data, or returns <see langword="null"/> for invalid input.
    /// </summary>
    public static FlatQuestionDragData? TryDeserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPayloadCharacters)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(value);
            return payload is { Version: CurrentVersion, Data: not null }
                ? payload.Data
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Payload(int Version, FlatQuestionDragData Data);
}
