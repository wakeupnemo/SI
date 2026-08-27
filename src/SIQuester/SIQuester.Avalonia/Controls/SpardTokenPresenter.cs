using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIQuester.Avalonia.Localization;
using SIQuester.ViewModel.Services;
using System.ComponentModel;

namespace SIQuester.Avalonia.Controls;

/// <summary>
/// Presents the structural SPARD token stream without becoming a second editor model.
/// </summary>
public sealed class SpardTokenPresenter : WrapPanel
{
    private const int MaxRenderedTokens = 256;
    private const int MaxAccessibleValueLength = 160;

    public static readonly StyledProperty<SpardEditorController?> ControllerProperty =
        AvaloniaProperty.Register<SpardTokenPresenter, SpardEditorController?>(nameof(Controller));

    public static readonly StyledProperty<IReadOnlyList<SpardAliasDescriptor>?> AliasesProperty =
        AvaloniaProperty.Register<SpardTokenPresenter, IReadOnlyList<SpardAliasDescriptor>?>(nameof(Aliases));

    private SpardEditorController? _subscribedController;
    private bool _isAttached;

    /// <summary>Gets or sets the authoritative structural editor controller.</summary>
    public SpardEditorController? Controller
    {
        get => GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    /// <summary>Gets or sets stable alias presentation metadata from the active editor session.</summary>
    public IReadOnlyList<SpardAliasDescriptor>? Aliases
    {
        get => GetValue(AliasesProperty);
        set => SetValue(AliasesProperty, value);
    }

    public SpardTokenPresenter() => Orientation = global::Avalonia.Layout.Orientation.Horizontal;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        SubscribeToController(Controller);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        SubscribeToController(null);
        Children.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ControllerProperty)
        {
            if (_isAttached)
            {
                SubscribeToController(Controller);
            }

            RequestRebuild();
        }
        else if (change.Property == AliasesProperty)
        {
            RequestRebuild();
        }
    }

    private void SubscribeToController(SpardEditorController? controller)
    {
        if (ReferenceEquals(_subscribedController, controller))
        {
            return;
        }

        if (_subscribedController != null)
        {
            _subscribedController.PropertyChanged -= Controller_PropertyChanged;
        }

        _subscribedController = controller;

        if (_subscribedController != null)
        {
            _subscribedController.PropertyChanged += Controller_PropertyChanged;
        }
    }

    private void Controller_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpardEditorController.Tokens))
        {
            RequestRebuild();
        }
    }

    private void RequestRebuild()
    {
        if (!_isAttached)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Rebuild();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_isAttached)
                {
                    Rebuild();
                }
            });
        }
    }

    private void Rebuild()
    {
        Children.Clear();

        if (Controller == null)
        {
            return;
        }

        var aliases = new Dictionary<string, SpardAliasDescriptor>(StringComparer.Ordinal);

        foreach (var alias in Aliases ?? Array.Empty<SpardAliasDescriptor>())
        {
            aliases[alias.Key] = alias;
        }

        foreach (var token in Controller.Tokens.Take(MaxRenderedTokens))
        {
            var kindLabel = GetKindLabel(token.Kind);
            var visibleText = token.Kind == SpardTokenKind.Line ? "↵" : token.Text;

            if (token.Depth > 0)
            {
                visibleText = new string('›', token.Depth) + visibleText;
            }

            var accessibleValue = token.Kind == SpardTokenKind.Line ? UiStrings.SpardLineToken : token.Value;
            var accessibleName = $"{kindLabel}: {Bound(accessibleValue)}; {UiStrings.SpardTokenDepth}: {token.Depth}";
            var tokenText = new TextBlock
            {
                Text = visibleText,
                TextWrapping = TextWrapping.NoWrap,
            };
            var tokenBorder = new Border { Child = tokenText, Tag = token };
            tokenBorder.Classes.Add("spard-token");
            tokenBorder.Classes.Add(GetKindClass(token.Kind));
            AutomationProperties.SetName(tokenBorder, accessibleName);
            ToolTip.SetTip(tokenBorder, accessibleName);

            if (token.Kind == SpardTokenKind.Alias
                && aliases.TryGetValue(token.Value, out var alias)
                && Color.TryParse(alias.Color, out var aliasColor))
            {
                tokenBorder.BorderBrush = new SolidColorBrush(aliasColor);
            }

            Children.Add(tokenBorder);
        }

        if (Controller.Tokens.Count > MaxRenderedTokens)
        {
            var truncation = new Border
            {
                Child = new TextBlock { Text = "…", TextWrapping = TextWrapping.NoWrap },
            };
            truncation.Classes.Add("spard-token");
            truncation.Classes.Add("opaque");
            AutomationProperties.SetName(truncation, UiStrings.SpardTokenPreviewTruncated);
            ToolTip.SetTip(truncation, UiStrings.SpardTokenPreviewTruncated);
            Children.Add(truncation);
        }
    }

    private static string Bound(string value) => value.Length <= MaxAccessibleValueLength
        ? value
        : value[..MaxAccessibleValueLength] + "…";

    private static string GetKindClass(SpardTokenKind kind) => kind switch
    {
        SpardTokenKind.Text => "text",
        SpardTokenKind.Alias => "alias",
        SpardTokenKind.Line => "line",
        SpardTokenKind.OptionalStart or SpardTokenKind.OptionalEnd => "optional",
        SpardTokenKind.Opaque => "opaque",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string GetKindLabel(SpardTokenKind kind) => kind switch
    {
        SpardTokenKind.Text => UiStrings.SpardTextToken,
        SpardTokenKind.Alias => UiStrings.SpardAliasToken,
        SpardTokenKind.Line => UiStrings.SpardLineToken,
        SpardTokenKind.OptionalStart => UiStrings.SpardOptionalStartToken,
        SpardTokenKind.OptionalEnd => UiStrings.SpardOptionalEndToken,
        SpardTokenKind.Opaque => UiStrings.SpardOpaqueToken,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
