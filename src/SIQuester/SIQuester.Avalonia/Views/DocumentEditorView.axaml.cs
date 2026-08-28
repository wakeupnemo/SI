using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIQuester.Avalonia.Localization;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;
using System.Globalization;

namespace SIQuester.Avalonia.Views;

public partial class DocumentEditorView : UserControl
{
    private const double DragThreshold = 4.0;

    private PointerPressedEventArgs? _pendingHierarchyPointerPress;
    private IItemViewModel? _pendingHierarchyItem;
    private Point _hierarchyDragStart;
    private bool _isHierarchyDragging;

    public DocumentEditorView()
    {
        InitializeComponent();
        KeyDown += DocumentEditorView_KeyDown;
    }

    private void DocumentEditorView_KeyDown(object? sender, KeyEventArgs e)
    {
        var primaryModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (!e.Handled
            && primaryModifier
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
            && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearPendingHierarchyDrag();
        base.OnDetachedFromVisualTree(e);
    }

    private void Navigator_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is QDocument document
            && sender is TreeView { SelectedItem: IItemViewModel selectedItem })
        {
            document.ActiveNode = selectedItem;
        }
    }

    private void HierarchyDragHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not QDocument document
            || sender is not Control { DataContext: IItemViewModel item } control
            || item is not (ThemeViewModel or QuestionViewModel)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Select(document, item);
        control.Focus();
        _pendingHierarchyPointerPress = e;
        _pendingHierarchyItem = item;
        _hierarchyDragStart = e.GetPosition(this);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private async void HierarchyDragHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isHierarchyDragging
            || _pendingHierarchyPointerPress == null
            || _pendingHierarchyItem == null
            || DataContext is not QDocument document)
        {
            return;
        }

        var position = e.GetPosition(this);

        if (Math.Abs(position.X - _hierarchyDragStart.X) <= DragThreshold
            && Math.Abs(position.Y - _hierarchyDragStart.Y) <= DragThreshold)
        {
            return;
        }

        var pointerPress = _pendingHierarchyPointerPress;
        var item = _pendingHierarchyItem;
        ClearPendingHierarchyDrag();
        _isHierarchyDragging = true;
        e.Pointer.Capture(null);

        try
        {
            var transfer = new DataTransfer();
            var allowedEffects = DragDropEffects.Move;

            switch (item)
            {
                case ThemeViewModel theme:
                    transfer.Add(DataTransferItem.Create(
                        InternalDragFormats.Theme,
                        ThemeDragDataSerializer.Serialize(document.ThemeMoves.CreateDragData(theme))));
                    break;

                case QuestionViewModel question:
                    transfer.Add(DataTransferItem.Create(
                        InternalDragFormats.Question,
                        FlatQuestionDragDataSerializer.Serialize(document.FlatQuestions.CreateDragData(question))));
                    allowedEffects |= DragDropEffects.Copy;
                    break;

                default:
                    return;
            }

            await DragDrop.DoDragDropAsync(pointerPress, transfer, allowedEffects);
        }
        catch (ArgumentException)
        {
            // The item was detached while the pointer was moving. There is nothing left to drag.
        }
        catch (Exception exception)
        {
            document.ErrorMessage = $"{UiStrings.DragHierarchyItemFailed}: {exception.Message}";
        }
        finally
        {
            _isHierarchyDragging = false;
        }
    }

    private void HierarchyDragHandle_PointerReleased(object? sender, PointerReleasedEventArgs e) =>
        CancelPendingHierarchyDrag(e.Pointer);

    private void HierarchyDragHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        var pointer = e.Pointer;
        var expectedCapture = sender as IInputElement;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_isHierarchyDragging && !ReferenceEquals(pointer.Captured, expectedCapture))
                {
                    ClearPendingHierarchyDrag();
                }
            },
            DispatcherPriority.Input);
    }

    private void CancelPendingHierarchyDrag(IPointer pointer)
    {
        ClearPendingHierarchyDrag();
        pointer.Capture(null);
    }

    private void ClearPendingHierarchyDrag()
    {
        _pendingHierarchyPointerPress = null;
        _pendingHierarchyItem = null;
    }

    private void HierarchyDropTarget_DragOver(object? sender, DragEventArgs e)
    {
        var isValid = DataContext is QDocument document
            && (TryGetThemeDragData(e, out _) && TryGetThemeTarget(document, sender, out _)
                || TryGetQuestionDragData(e, out _) && TryGetQuestionTarget(document, sender, out _));

        e.DragEffects = isValid
            ? TryGetThemeDragData(e, out _) ? DragDropEffects.Move : GetRequestedQuestionEffect(e)
            : DragDropEffects.None;
        SetHierarchyDropIndicator(sender, isValid);
        e.Handled = true;
    }

    private void HierarchyDropTarget_DragLeave(object? sender, DragEventArgs e)
    {
        SetHierarchyDropIndicator(sender, isActive: false);
        e.Handled = true;
    }

    private void HierarchyDropTarget_Drop(object? sender, DragEventArgs e)
    {
        SetHierarchyDropIndicator(sender, isActive: false);

        if (DataContext is not QDocument document)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        try
        {
            ApplyHierarchyDrop(document, sender, e);
        }
        catch (Exception exception)
        {
            document.ErrorMessage = $"{UiStrings.DragHierarchyItemFailed}: {exception.Message}";
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private static void ApplyHierarchyDrop(QDocument document, object? sender, DragEventArgs e)
    {
        if (TryGetThemeDragData(e, out var themeDragData)
            && TryGetThemeTarget(document, sender, out var themeTarget))
        {
            var result = document.ThemeMoves.Apply(
                themeDragData,
                themeTarget,
                document.Settings.ChangePriceOnMove);

            e.DragEffects = result is ThemeMoveResult.Applied or ThemeMoveResult.NoChange
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (TryGetQuestionDragData(e, out var questionDragData)
            && TryGetQuestionTarget(document, sender, out var questionTarget))
        {
            var requestedEffect = GetRequestedQuestionEffect(e);
            var result = document.FlatQuestions.Apply(
                questionDragData,
                questionTarget,
                requestedEffect == DragDropEffects.Copy
                    ? FlatQuestionDropMode.Copy
                    : FlatQuestionDropMode.Move,
                document.Settings.ChangePriceOnMove);

            if (result is FlatQuestionDropResult.Applied or FlatQuestionDropResult.NoChange)
            {
                ExpandSelectedQuestion(document);
                e.DragEffects = requestedEffect;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }

            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private static bool TryGetThemeDragData(DragEventArgs e, out ThemeDragData dragData)
    {
        var serialized = e.DataTransfer.TryGetValue(InternalDragFormats.Theme);
        dragData = ThemeDragDataSerializer.TryDeserialize(serialized)!;
        return dragData != null;
    }

    private static bool TryGetQuestionDragData(DragEventArgs e, out FlatQuestionDragData dragData)
    {
        var serialized = e.DataTransfer.TryGetValue(InternalDragFormats.Question);
        dragData = FlatQuestionDragDataSerializer.TryDeserialize(serialized)!;
        return dragData != null;
    }

    private static bool TryGetThemeTarget(
        QDocument document,
        object? sender,
        out ThemeLocation target)
    {
        target = default;

        if (sender is not Border border)
        {
            return false;
        }

        RoundViewModel? round;
        int themeIndex;

        if (border.DataContext is RoundViewModel targetRound)
        {
            round = targetRound;
            themeIndex = 0;
        }
        else if (border.DataContext is ThemeViewModel targetTheme)
        {
            round = targetTheme.OwnerRound;
            themeIndex = round?.Themes.IndexOf(targetTheme) + 1 ?? -1;
        }
        else
        {
            return false;
        }

        if (round == null || !ReferenceEquals(round.OwnerPackage, document.Package) || themeIndex < 0)
        {
            return false;
        }

        target = new ThemeLocation(document.Package.Rounds.IndexOf(round), themeIndex);
        return target.RoundIndex >= 0;
    }

    private static bool TryGetQuestionTarget(
        QDocument document,
        object? sender,
        out FlatQuestionLocation target)
    {
        target = default;

        if (sender is not Border border)
        {
            return false;
        }

        ThemeViewModel? theme;
        int questionIndex;

        if (border.DataContext is ThemeViewModel targetTheme)
        {
            theme = targetTheme;
            questionIndex = 0;
        }
        else if (border.DataContext is QuestionViewModel targetQuestion)
        {
            theme = targetQuestion.OwnerTheme;
            questionIndex = theme?.Questions.IndexOf(targetQuestion) + 1 ?? -1;
        }
        else
        {
            return false;
        }

        var round = theme?.OwnerRound;

        if (theme == null
            || round == null
            || !ReferenceEquals(round.OwnerPackage, document.Package)
            || questionIndex < 0)
        {
            return false;
        }

        target = new FlatQuestionLocation(
            document.Package.Rounds.IndexOf(round),
            round.Themes.IndexOf(theme),
            questionIndex);
        return target.RoundIndex >= 0 && target.ThemeIndex >= 0;
    }

    private static DragDropEffects GetRequestedQuestionEffect(DragEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;

    private static void SetHierarchyDropIndicator(object? sender, bool isActive)
    {
        if (sender is not Border border)
        {
            return;
        }

        if (isActive && !border.Classes.Contains("drag-over"))
        {
            border.Classes.Add("drag-over");
        }
        else if (!isActive)
        {
            border.Classes.Remove("drag-over");
        }
    }

    private static void ExpandSelectedQuestion(QDocument document)
    {
        if (document.ActiveNode is not QuestionViewModel { OwnerTheme: { } theme })
        {
            return;
        }

        theme.IsExpanded = true;

        if (theme.OwnerRound != null)
        {
            theme.OwnerRound.IsExpanded = true;
        }
    }

    private static void Select(QDocument document, IItemViewModel item)
    {
        if (ReferenceEquals(document.ActiveNode, item))
        {
            return;
        }

        if (document.ActiveNode != null)
        {
            document.ActiveNode.IsSelected = false;
        }

        item.IsSelected = true;
        document.ActiveNode = item;
    }
}

/// <summary>
/// Maps the retained WPF sidebar indices to the Avalonia tool and media tab layouts.
/// </summary>
public sealed class DocumentSidebarIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sideIndex = value is int index ? index : 0;

        return parameter switch
        {
            "TreeTools" => sideIndex == 6 ? 1 : 0,
            "FlatTools" => sideIndex == 6 ? 2 : sideIndex is >= 2 and <= 5 ? 1 : 0,
            "Media" => sideIndex is >= 2 and <= 5 ? sideIndex - 2 : 0,
            _ => 0,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
