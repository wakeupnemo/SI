using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Services;
using System.Text;
using System.Xml;

namespace SIQuester.Model;

/// <summary>
/// Contains package object data used in copy-paste and drag-and-drop operations.
/// </summary>
[Serializable]
public sealed class InfoOwnerData
{
    public enum Level { Package, Round, Theme, Question };

    public InfoOwnerData()
    {
    }

    public Level ItemLevel { get; set; }

    public string ItemData { get; set; } = "";

    public AuthorInfo[] Authors { get; set; } = Array.Empty<AuthorInfo>();

    public SourceInfo[] Sources { get; set; } = Array.Empty<SourceInfo>();

    public Dictionary<string, string> Images { get; set; } = new();

    public Dictionary<string, string> Audio { get; set; } = new();

    public Dictionary<string, string> Video { get; set; } = new();

    public Dictionary<string, string> Html { get; set; } = new();

    public Dictionary<string, byte[]> EmbeddedImages { get; set; } = new();

    public Dictionary<string, byte[]> EmbeddedAudio { get; set; } = new();

    public Dictionary<string, byte[]> EmbeddedVideo { get; set; } = new();

    public Dictionary<string, byte[]> EmbeddedHtml { get; set; } = new();

    public InfoOwnerData(QDocument document, IItemViewModel item)
        : this(document, item, includeLegacyMediaPaths: true)
    {
    }

    internal static InfoOwnerData CreateForAsyncClipboard(QDocument document, IItemViewModel item) =>
        new(document, item, includeLegacyMediaPaths: false);

    private InfoOwnerData(QDocument document, IItemViewModel item, bool includeLegacyMediaPaths)
    {
        var model = item.GetModel();

        var sb = new StringBuilder();

        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            model.WriteXml(writer);
        }

        ItemData = sb.ToString();

        ItemLevel =
            model is Package ? Level.Package :
            model is Round ? Level.Round :
            model is Theme ? Level.Theme : Level.Question;

        GetFullData(document, item, includeLegacyMediaPaths);
    }

    internal async Task EmbedMediaAsync(QDocument document, CancellationToken cancellationToken = default)
    {
        long totalBytes = 0;
        totalBytes = await EmbedCollectionAsync(
            document.Images,
            Images.Keys,
            EmbeddedImages,
            totalBytes,
            cancellationToken);
        totalBytes = await EmbedCollectionAsync(
            document.Audio,
            Audio.Keys,
            EmbeddedAudio,
            totalBytes,
            cancellationToken);
        totalBytes = await EmbedCollectionAsync(
            document.Video,
            Video.Keys,
            EmbeddedVideo,
            totalBytes,
            cancellationToken);
        await EmbedCollectionAsync(
            document.Html,
            Html.Keys,
            EmbeddedHtml,
            totalBytes,
            cancellationToken);
    }

    private static async Task<long> EmbedCollectionAsync(
        MediaStorageViewModel collection,
        IEnumerable<string> names,
        Dictionary<string, byte[]> destination,
        long currentTotal,
        CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var streamInfo = await collection.TryGetStreamInfoAsync(name, cancellationToken)
                ?? throw new InvalidDataException($"Referenced media is unavailable: {name}");

            if (streamInfo.Length < 0
                || streamInfo.Length > SIQuesterClipboardSerializer.MaximumEmbeddedMediaBytes - currentTotal)
            {
                streamInfo.Stream.Dispose();
                throw new InvalidDataException("The embedded clipboard media exceeds the supported size limit.");
            }

            await using var stream = streamInfo.Stream;
            using var buffer = new MemoryStream((int)streamInfo.Length);
            var copyBuffer = new byte[81920];

            while (true)
            {
                var read = await stream.ReadAsync(copyBuffer, cancellationToken);

                if (read == 0)
                {
                    break;
                }

                currentTotal += read;

                if (currentTotal > SIQuesterClipboardSerializer.MaximumEmbeddedMediaBytes)
                {
                    throw new InvalidDataException("The embedded clipboard media exceeds the supported size limit.");
                }

                await buffer.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken);
            }

            destination.Add(name, buffer.ToArray());
        }

        return currentTotal;
    }

    public InfoOwner GetItem()
    {
        InfoOwner item = ItemLevel switch
        {
            Level.Package => new Package(),
            Level.Round => new Round(),
            Level.Theme => new Theme(),
            _ => new Question(),
        };

        using (var sr = new StringReader(ItemData))
        {
            using var reader = XmlReader.Create(sr);
            reader.Read();
            item.ReadXml(reader);
        }

        return item;
    }

    /// <summary>
    /// Gets full object data including attached objects.
    /// </summary>
    /// <param name="documentViewModel">Document which contains the object.</param>
    /// <param name="item">Object having necessary data.</param>
    private void GetFullData(
        QDocument documentViewModel,
        IItemViewModel item,
        bool includeLegacyMediaPaths)
    {
        var model = item.GetModel();
        var document = documentViewModel.Document;

        var length = model.Info.Authors.Count;

        var authors = new HashSet<AuthorInfo>();

        for (int i = 0; i < length; i++)
        {
            var docAuthor = document.GetLink(model.Info.Authors, i);

            if (docAuthor != null)
            {
                authors.Add(docAuthor);
            }
        }

        Authors = authors.ToArray();

        length = model.Info.Sources.Count;

        var sources = new HashSet<SourceInfo>();

        for (int i = 0; i < length; i++)
        {
            var docSource = document.GetLink(model.Info.Sources, i);

            if (docSource != null)
            {
                sources.Add(docSource);
            }
        }

        Sources = sources.ToArray();

        GetMedia(documentViewModel, model, includeLegacyMediaPaths);
    }

    private void GetMedia(QDocument documentViewModel, InfoOwner model, bool includeLegacyMediaPaths)
    {
        if (model is Question question)
        {
            GetQuestion(documentViewModel, question, includeLegacyMediaPaths);
        }

        if (model is Theme theme)
        {
            GetTheme(documentViewModel, theme, includeLegacyMediaPaths);
        }

        if (model is Round round)
        {
            GetRound(documentViewModel, round, includeLegacyMediaPaths);
        }
    }

    private void GetRound(QDocument documentViewModel, Round round, bool includeLegacyMediaPaths)
    {
        foreach (var theme in round.Themes)
        {
            GetTheme(documentViewModel, theme, includeLegacyMediaPaths);
        }
    }

    private void GetTheme(QDocument documentViewModel, Theme theme, bool includeLegacyMediaPaths)
    {
        foreach (var question in theme.Questions)
        {
            GetQuestion(documentViewModel, question, includeLegacyMediaPaths);
        }
    }

    private void GetQuestion(QDocument documentViewModel, Question question, bool includeLegacyMediaPaths)
    {
        foreach (var contentItem in question.GetContent())
        {
            if (!contentItem.IsRef)
            {
                continue;
            }

            var collection = documentViewModel.TryGetCollectionByMediaType(contentItem.Type);

            if (collection == null)
            {
                continue;
            }

            var targetCollection = contentItem.Type switch
            {
                ContentTypes.Image => Images,
                ContentTypes.Audio => Audio,
                ContentTypes.Video => Video,
                ContentTypes.Html => Html,
                _ => null,
            };

            if (targetCollection == null)
            {
                continue;
            }

            var link = contentItem.Value;

            if (!targetCollection.ContainsKey(link))
            {
                targetCollection.Add(
                    link,
                    includeLegacyMediaPaths ? collection.Wrap(link).Uri : "");
            }
        }
    }
}
