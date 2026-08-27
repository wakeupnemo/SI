using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIQuester.ViewModel.Services;
using System.ComponentModel;

namespace SIQuester.Avalonia.Controls;

/// <summary>
/// Presents a SPARD template while routing every mutation through a
/// <see cref="SpardEditorController"/>.
/// </summary>
public sealed class SpardEditorControl : TextBox
{
    public static readonly StyledProperty<SpardEditorController?> ControllerProperty =
        AvaloniaProperty.Register<SpardEditorControl, SpardEditorController?>(nameof(Controller));

    private SpardEditorController? _subscribedController;
    private bool _isAttached;
    private bool _isRefreshing;

    /// <summary>Gets or sets the UI-independent structural editor controller.</summary>
    public SpardEditorController? Controller
    {
        get => GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    public SpardEditorControl()
    {
        AcceptsReturn = true;
        TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
        IsUndoEnabled = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        SubscribeToController(Controller);
        RefreshFromController();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        SubscribeToController(null);
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

            RefreshFromController();
        }
        else if (change.Property == TextProperty && !_isRefreshing)
        {
            // Context-menu and automation edits must not bypass structural operations.
            RefreshFromController();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (Controller == null || string.IsNullOrEmpty(e.Text))
        {
            base.OnTextInput(e);
            return;
        }

        SynchronizeSelectionToController();
        Controller.InsertText(e.Text);
        RefreshFromController();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var controller = Controller;

        if (controller == null)
        {
            base.OnKeyDown(e);
            return;
        }

        var primaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (primaryModifier
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && e.Key is Key.X or Key.C or Key.V)
        {
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            base.OnKeyDown(e);
            SynchronizeSelectionToController();
            RefreshFromController();
            return;
        }

        SynchronizeSelectionToController();
        var handled = e.Key switch
        {
            Key.Back => controller.DeleteBackward(),
            Key.Delete => controller.DeleteForward(),
            Key.Left => controller.MoveCaretBackward(),
            Key.Right => controller.MoveCaretForward(),
            Key.Home => MoveCaret(controller, 0),
            Key.End => MoveCaret(controller, controller.DisplayText.Length),
            Key.Enter => InsertLine(controller),
            _ => false,
        };

        if (!handled)
        {
            base.OnKeyDown(e);
            return;
        }

        RefreshFromController();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (Controller != null)
        {
            SynchronizeSelectionToController();
            RefreshFromController();
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        SynchronizeSelectionToController();
        Controller?.Commit();
        base.OnLostFocus(e);
    }

    private static bool MoveCaret(SpardEditorController controller, int offset)
    {
        controller.SetCaret(offset);
        return true;
    }

    private static bool InsertLine(SpardEditorController controller)
    {
        controller.InsertLine();
        return true;
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
        if (e.PropertyName is nameof(SpardEditorController.DisplayText)
            or nameof(SpardEditorController.CaretOffset)
            or nameof(SpardEditorController.Selection))
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                RefreshFromController();
            }
            else
            {
                Dispatcher.UIThread.Post(RefreshFromController);
            }
        }
    }

    private void SynchronizeSelectionToController()
    {
        if (_isRefreshing || Controller == null)
        {
            return;
        }

        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        Controller.SetSelection(start, end - start);
    }

    private void RefreshFromController()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;

        try
        {
            var controller = Controller;
            SetCurrentValue(TextProperty, controller?.DisplayText ?? "");

            if (controller == null)
            {
                SelectionStart = 0;
                SelectionEnd = 0;
                CaretIndex = 0;
                return;
            }

            SelectionStart = controller.Selection.Start;
            SelectionEnd = controller.Selection.End;
            CaretIndex = controller.CaretOffset;
        }
        finally
        {
            _isRefreshing = false;
        }
    }
}
