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
    internal const int CurrentSchemaVersion = 2;
    internal const int LegacyItemSchemaVersion = 1;
    internal const int PackageInfoSchemaVersion = 1;
    internal const int MaximumPayloadSize = 32 * 1024 * 1024;
    internal const int MaximumEmbeddedMediaBytes = 20 * 1024 * 1024;
    internal const string ItemKind = "siquester-info-owner";
    internal const string PackageInfoKind = "siquester-package-info";

    internal static readonly ClipboardCustomFormat ItemFormat = new(
        "SIQuester.Item.v2",
        ClipboardCustomDataKind.Binary);

    internal static readonly ClipboardCustomFormat LegacyVersionedItemFormat = new(
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

    internal static byte[] SerializeItem(InfoOwnerData itemData)
    {
        if (!ValidateItemShape(itemData) || !ValidateEmbeddedMedia(itemData))
        {
            throw new InvalidOperationException("The SIQuester clipboard item is invalid or too large.");
        }

        return Serialize(CurrentSchemaVersion, ItemKind, itemData);
    }

    internal static string SerializeLegacyItem(InfoOwnerData itemData) =>
        JsonSerializer.Serialize(CreateLegacyItem(itemData));

    internal static bool TryDeserializeItem(ReadOnlySpan<byte> data, out InfoOwnerData? itemData)
    {
        if (!TryDeserialize(data, CurrentSchemaVersion, ItemKind, out itemData)
            || !ValidateItemShape(itemData!)
            || !ValidateEmbeddedMedia(itemData!))
        {
            itemData = null;
            return false;
        }

        return true;
    }

    internal static bool TryDeserializeLegacyVersionedItem(
        ReadOnlySpan<byte> data,
        out InfoOwnerData? itemData)
    {
        if (!TryDeserialize(data, LegacyItemSchemaVersion, ItemKind, out itemData)
            || !ValidateItemShape(itemData!))
        {
            itemData = null;
            return false;
        }

        return true;
    }

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

            if (itemData == null || !ValidateItemShape(itemData))
            {
                itemData = null;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static byte[] SerializePackageInfo<T>(T packageInfo) =>
        Serialize(PackageInfoSchemaVersion, PackageInfoKind, packageInfo);

    internal static bool TryDeserializePackageInfo(
        ReadOnlySpan<byte> data,
        out Dictionary<string, JsonElement>? packageInfo) =>
        TryDeserialize(data, PackageInfoSchemaVersion, PackageInfoKind, out packageInfo);

    private static byte[] Serialize<T>(int schemaVersion, string kind, T payload)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(
            new ClipboardEnvelope<T>(schemaVersion, kind, payload),
            SerializerOptions);

        if (data.Length > MaximumPayloadSize)
        {
            throw new InvalidOperationException("The SIQuester clipboard payload is too large.");
        }

        return data;
    }

    private static bool TryDeserialize<T>(
        ReadOnlySpan<byte> data,
        int expectedSchemaVersion,
        string expectedKind,
        out T? payload)
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
                || schemaVersion.GetInt32() != expectedSchemaVersion
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

    private static bool ValidateEmbeddedMedia(InfoOwnerData itemData)
    {
        long totalBytes = 0;
        var totalItems = 0;

        foreach (var (references, embedded) in new[]
        {
            (itemData.Images, itemData.EmbeddedImages),
            (itemData.Audio, itemData.EmbeddedAudio),
            (itemData.Video, itemData.EmbeddedVideo),
            (itemData.Html, itemData.EmbeddedHtml),
        })
        {
            if (references.Count != embedded.Count
                || references.Keys.Any(name => !embedded.ContainsKey(name)))
            {
                return false;
            }

            totalItems += embedded.Count;

            if (totalItems > 512)
            {
                return false;
            }

            foreach (var item in embedded)
            {
                if (!IsSafeMediaName(item.Key) || item.Value == null)
                {
                    return false;
                }

                totalBytes += item.Value.LongLength;

                if (totalBytes > MaximumEmbeddedMediaBytes)
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool IsSafeMediaName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 1024
        && name.IndexOfAny(['/', '\\']) < 0
        && name == Path.GetFileName(name)
        && name is not "." and not ".."
        && !name.Any(char.IsControl);

    private static bool ValidateItemShape(InfoOwnerData itemData) =>
        itemData.ItemData != null
        && itemData.Authors != null
        && itemData.Sources != null
        && itemData.Images != null
        && itemData.Audio != null
        && itemData.Video != null
        && itemData.Html != null
        && itemData.EmbeddedImages != null
        && itemData.EmbeddedAudio != null
        && itemData.EmbeddedVideo != null
        && itemData.EmbeddedHtml != null;

    private static LegacyInfoOwnerData CreateLegacyItem(InfoOwnerData itemData) => new()
    {
        ItemLevel = itemData.ItemLevel,
        ItemData = itemData.ItemData,
        Authors = itemData.Authors,
        Sources = itemData.Sources,
        Images = itemData.Images,
        Audio = itemData.Audio,
        Video = itemData.Video,
        Html = itemData.Html,
    };

    private sealed class LegacyInfoOwnerData
    {
        public InfoOwnerData.Level ItemLevel { get; init; }
        public string ItemData { get; init; } = "";
        public SIPackages.AuthorInfo[] Authors { get; init; } = Array.Empty<SIPackages.AuthorInfo>();
        public SIPackages.SourceInfo[] Sources { get; init; } = Array.Empty<SIPackages.SourceInfo>();
        public Dictionary<string, string> Images { get; init; } = new();
        public Dictionary<string, string> Audio { get; init; } = new();
        public Dictionary<string, string> Video { get; init; } = new();
        public Dictionary<string, string> Html { get; init; } = new();
    }

    private sealed record ClipboardEnvelope<T>(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("payload")] T Payload);
}
