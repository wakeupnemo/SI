using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;

namespace SIQuester.Avalonia.Views;

public partial class FlatDocumentView : UserControl
{
    private const double DragThreshold = 4.0;

    private static readonly DataFormat<string> QuestionDragFormat =
        DataFormat.CreateStringApplicationFormat("SIQuester.FlatQuestion.v1");

    private PointerPressedEventArgs? _pendingPointerPress;
    private QuestionViewModel? _pendingQuestion;
    private Point _dragStart;
    private bool _isDragging;

    public FlatDocumentView()
    {
        InitializeComponent();
        PointerMoved += FlatDocumentView_PointerMoved;
        PointerReleased += FlatDocumentView_PointerReleased;
    }

    private void Question_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not QDocument document
            || sender is not Border { DataContext: QuestionViewModel question }
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        document.ActiveNode = question;
        _pendingPointerPress = e;
        _pendingQuestion = question;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private async void FlatDocumentView_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging
            || _pendingPointerPress == null
            || _pendingQuestion == null
            || DataContext is not QDocument document)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(this);

        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            CancelPendingDrag(e.Pointer);
            return;
        }

        var position = currentPoint.Position;

        if (Math.Abs(position.X - _dragStart.X) <= DragThreshold
            && Math.Abs(position.Y - _dragStart.Y) <= DragThreshold)
        {
            return;
        }

        var pointerPress = _pendingPointerPress;
        var question = _pendingQuestion;
        _pendingPointerPress = null;
        _pendingQuestion = null;
        _isDragging = true;
        e.Pointer.Capture(null);

        FlatQuestionDragData dragData;

        try
        {
            dragData = document.FlatQuestions.CreateDragData(question);
        }
        catch (ArgumentException)
        {
            _isDragging = false;
            return;
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(
            QuestionDragFormat,
            FlatQuestionDragDataSerializer.Serialize(dragData)));

        try
        {
            await DragDrop.DoDragDropAsync(pointerPress, transfer, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            ((IDisposable)transfer).Dispose();
            _isDragging = false;
        }
    }

    private void FlatDocumentView_PointerReleased(object? sender, PointerReleasedEventArgs e) =>
        CancelPendingDrag(e.Pointer);

    private void Question_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDragging)
        {
            _pendingPointerPress = null;
            _pendingQuestion = null;
        }
    }

    private void CancelPendingDrag(IPointer pointer)
    {
        _pendingPointerPress = null;
        _pendingQuestion = null;
        pointer.Capture(null);
    }

    private void DropTarget_DragOver(object? sender, DragEventArgs e)
    {
        var isValid = TryGetDragData(e, out _)
            && TryGetTargetLocation(sender, out _);

        e.DragEffects = isValid ? GetRequestedEffect(e) : DragDropEffects.None;
        SetDropIndicator(sender, isValid);
        e.Handled = true;
    }

    private void DropTarget_DragLeave(object? sender, DragEventArgs e)
    {
        SetDropIndicator(sender, isActive: false);
        e.Handled = true;
    }

    private void QuestionDropTarget_Drop(object? sender, DragEventArgs e) => ApplyDrop(sender, e);

    private void ThemeEndDropTarget_Drop(object? sender, DragEventArgs e) => ApplyDrop(sender, e);

    private void ApplyDrop(object? sender, DragEventArgs e)
    {
        SetDropIndicator(sender, isActive: false);

        if (DataContext is not QDocument document
            || !TryGetDragData(e, out var dragData)
            || !TryGetTargetLocation(sender, out var target))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var requestedEffect = GetRequestedEffect(e);
        var result = document.FlatQuestions.Apply(
            dragData,
            target,
            requestedEffect == DragDropEffects.Copy
                ? FlatQuestionDropMode.Copy
                : FlatQuestionDropMode.Move,
            document.Settings.ChangePriceOnMove);

        e.DragEffects = result is FlatQuestionDropResult.Applied or FlatQuestionDropResult.NoChange
            ? requestedEffect
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static DragDropEffects GetRequestedEffect(DragEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;

    private static bool TryGetDragData(DragEventArgs e, out FlatQuestionDragData dragData)
    {
        var serialized = e.DataTransfer.TryGetValue(QuestionDragFormat);
        dragData = FlatQuestionDragDataSerializer.TryDeserialize(serialized)!;
        return dragData != null;
    }

    private bool TryGetTargetLocation(object? sender, out FlatQuestionLocation target)
    {
        target = default;

        if (DataContext is not QDocument document || sender is not Border border)
        {
            return false;
        }

        ThemeViewModel? theme;
        int questionIndex;

        if (border.DataContext is QuestionViewModel question)
        {
            theme = question.OwnerTheme;
            questionIndex = theme?.Questions.IndexOf(question) ?? -1;
        }
        else if (border.DataContext is ThemeViewModel targetTheme)
        {
            theme = targetTheme;
            questionIndex = targetTheme.Questions.Count;
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

    private static void SetDropIndicator(object? sender, bool isActive)
    {
        if (sender is Border border)
        {
            border.Opacity = isActive ? 1.0 : 0.25;
        }
    }
}
