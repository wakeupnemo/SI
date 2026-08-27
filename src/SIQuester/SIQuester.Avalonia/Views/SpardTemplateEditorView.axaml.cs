using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Services;

namespace SIQuester.Avalonia.Views;

/// <summary>Owns one structural SPARD session while its template view is attached.</summary>
public partial class SpardTemplateEditorView : UserControl
{
    public static readonly StyledProperty<SpardTemplateViewModel?> TemplateModelProperty =
        AvaloniaProperty.Register<SpardTemplateEditorView, SpardTemplateViewModel?>(nameof(TemplateModel));

    public static readonly DirectProperty<SpardTemplateEditorView, SpardTemplateEditorSession?> SessionProperty =
        AvaloniaProperty.RegisterDirect<SpardTemplateEditorView, SpardTemplateEditorSession?>(
            nameof(Session),
            view => view.Session);

    private SpardTemplateEditorSession? _session;
    private bool _isAttached;

    public SpardTemplateViewModel? TemplateModel
    {
        get => GetValue(TemplateModelProperty);
        set => SetValue(TemplateModelProperty, value);
    }

    public SpardTemplateEditorSession? Session
    {
        get => _session;
        private set => SetAndRaise(SessionProperty, ref _session, value);
    }

    public SpardTemplateEditorView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ReplaceSession();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        ReplaceSession();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TemplateModelProperty && _isAttached)
        {
            ReplaceSession();
        }
    }

    private void ReplaceSession()
    {
        Session?.Dispose();
        Session = _isAttached && TemplateModel != null
            ? new SpardTemplateEditorSession(TemplateModel)
            : null;
    }
}
