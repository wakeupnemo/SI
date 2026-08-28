using System.Text.Json;

namespace SIQuester.Avalonia.Views;

/// <summary>
/// Applies the framework-side protocol and navigation boundary around the retained question player.
/// </summary>
public static class QuestionPreviewWebBridge
{
    public const string ReadyMessageType = "siquesterBridgeReady";
    public const string AudioUnlockedMessageType = "siquesterAudioUnlocked";
    public const int MaxInboundMessageLength = 64 * 1024;
    public const int MaxOutboundMessageLength = 4 * 1024 * 1024;

    /// <summary>Builds a script that forwards one typed JSON message through the application bridge.</summary>
    public static string BuildHostDispatchScript(string messageJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageJson);

        if (messageJson.Length > MaxOutboundMessageLength)
        {
            throw new ArgumentException("The question-preview message exceeds the allowed size.", nameof(messageJson));
        }

        using var document = JsonDocument.Parse(messageJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The question-preview message must be a JSON object.", nameof(messageJson));
        }

        return "window.siquesterReceiveHostMessage(" + JsonSerializer.Serialize(messageJson) + ");";
    }

    /// <summary>Validates an inbound message before it reaches the framework-neutral interop seam.</summary>
    public static bool TryValidateInboundMessage(string? messageJson, out string validatedMessage)
    {
        validatedMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(messageJson) || messageJson.Length > MaxInboundMessageLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(messageJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            validatedMessage = messageJson;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Gets whether a validated message is the application bridge readiness signal.</summary>
    public static bool IsReadyMessage(string messageJson)
    {
        using var document = JsonDocument.Parse(messageJson);
        return document.RootElement.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), ReadyMessageType, StringComparison.Ordinal);
    }

    /// <summary>Gets whether a trusted click unlocked embedded audio playback.</summary>
    public static bool IsAudioUnlockedMessage(string messageJson)
    {
        using var document = JsonDocument.Parse(messageJson);
        return document.RootElement.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), AudioUnlockedMessageType, StringComparison.Ordinal);
    }

    /// <summary>Allows only the randomized application origin and its own asset path.</summary>
    public static bool IsAllowedNavigation(Uri applicationSource, Uri? requestedUri)
    {
        ArgumentNullException.ThrowIfNull(applicationSource);

        if (!applicationSource.IsAbsoluteUri || requestedUri is not { IsAbsoluteUri: true })
        {
            return false;
        }

        if (!string.Equals(applicationSource.Scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(applicationSource.Host, requestedUri.Host, StringComparison.OrdinalIgnoreCase)
            || applicationSource.Port != requestedUri.Port
            || !string.IsNullOrEmpty(requestedUri.UserInfo)
            || !string.IsNullOrEmpty(requestedUri.Query)
            || !string.IsNullOrEmpty(requestedUri.Fragment))
        {
            return false;
        }

        var lastSlash = applicationSource.AbsolutePath.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return false;
        }

        var applicationPath = applicationSource.AbsolutePath[..(lastSlash + 1)];
        return requestedUri.AbsolutePath.StartsWith(applicationPath, StringComparison.Ordinal)
            && requestedUri.AbsolutePath.Length > applicationPath.Length;
    }
}
