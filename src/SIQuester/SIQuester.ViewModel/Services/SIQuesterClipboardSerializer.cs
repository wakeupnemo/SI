using SIQuester.Model;
using SIQuester.ViewModel.Contracts.Host;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Serializes SIQuester clipboard payloads across process and frontend boundaries.
/// </summary>
internal static class SIQuesterClipboardSerializer
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumPayloadSize = 32 * 1024 * 1024;
    internal const string ItemKind = "siquester-info-owner";
    internal const string PackageInfoKind = "siquester-package-info";

    internal static readonly ClipboardCustomFormat ItemFormat = new(
        "SIQuester.Item.v1",
        ClipboardCustomDataKind.Binary);

    internal static readonly ClipboardCustomFormat LegacyItemFormat = new(
        "siqdata",
        ClipboardCustomDataKind.Utf8Text,
        ClipboardCustomDataScope.Platform);

    internal static readonly ClipboardCustomFormat PackageInfoFormat = new(
        "SIQuester.PackageInfo.v1",
        ClipboardCustomDataKind.Binary);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static byte[] SerializeItem(InfoOwnerData itemData) =>
        Serialize(ItemKind, itemData);

    internal static string SerializeLegacyItem(InfoOwnerData itemData) =>
        JsonSerializer.Serialize(itemData);

    internal static bool TryDeserializeItem(ReadOnlySpan<byte> data, out InfoOwnerData? itemData) =>
        TryDeserialize(data, ItemKind, out itemData);

    internal static bool TryDeserializeLegacyItem(ReadOnlySpan<byte> data, out InfoOwnerData? itemData)
    {
        itemData = null;

        if (data.Length is 0 or > MaximumPayloadSize)
        {
            return false;
        }

        try
        {
            itemData = JsonSerializer.Deserialize<InfoOwnerData>(data, SerializerOptions);
            return itemData != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static byte[] SerializePackageInfo<T>(T packageInfo) =>
        Serialize(PackageInfoKind, packageInfo);

    internal static bool TryDeserializePackageInfo(
        ReadOnlySpan<byte> data,
        out Dictionary<string, JsonElement>? packageInfo) =>
        TryDeserialize(data, PackageInfoKind, out packageInfo);

    private static byte[] Serialize<T>(string kind, T payload)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(
            new ClipboardEnvelope<T>(CurrentSchemaVersion, kind, payload),
            SerializerOptions);

        if (data.Length > MaximumPayloadSize)
        {
            throw new InvalidOperationException("The SIQuester clipboard payload is too large.");
        }

        return data;
    }

    private static bool TryDeserialize<T>(ReadOnlySpan<byte> data, string expectedKind, out T? payload)
    {
        payload = default;

        if (data.Length is 0 or > MaximumPayloadSize)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || schemaVersion.GetInt32() != CurrentSchemaVersion
                || !root.TryGetProperty("kind", out var kind)
                || kind.GetString() != expectedKind
                || !root.TryGetProperty("payload", out var payloadElement))
            {
                return false;
            }

            payload = payloadElement.Deserialize<T>(SerializerOptions);
            return payload != null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record ClipboardEnvelope<T>(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("payload")] T Payload);
}
