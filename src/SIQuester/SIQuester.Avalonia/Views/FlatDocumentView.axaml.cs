using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel;
using SIQuester.ViewModel.Serializers;
using SIQuester.ViewModel.Services;
using System.ComponentModel;

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
    private CancellationTokenSource? _externalDropCancellation;
    private QDocument? _subscribedDocument;
    private bool _showQuestionDetails;

    public static readonly DirectProperty<FlatDocumentView, bool> ShowQuestionDetailsProperty =
        AvaloniaProperty.RegisterDirect<FlatDocumentView, bool>(
            nameof(ShowQuestionDetails),
            view => view.ShowQuestionDetails);

    /// <summary>
    /// Gets whether question text and keyboard-equivalent actions are shown by the current scale.
    /// </summary>
    public bool ShowQuestionDetails
    {
        get => _showQuestionDetails;
        private set => SetAndRaise(ShowQuestionDetailsProperty, ref _showQuestionDetails, value);
    }

    public FlatDocumentView()
    {
        InitializeComponent();
        PointerMoved += FlatDocumentView_PointerMoved;
        PointerReleased += FlatDocumentView_PointerReleased;
        DataContextChanged += FlatDocumentView_DataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _externalDropCancellation ??= new CancellationTokenSource();
        SubscribeToDocument(DataContext as QDocument);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _externalDropCancellation?.Cancel();
        _externalDropCancellation?.Dispose();
        _externalDropCancellation = null;
        SubscribeToDocument(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void FlatDocumentView_DataContextChanged(object? sender, EventArgs e)
    {
        if (VisualRoot != null)
        {
            SubscribeToDocument(DataContext as QDocument);
        }
    }

    private void SubscribeToDocument(QDocument? document)
    {
        if (ReferenceEquals(_subscribedDocument, document))
        {
            return;
        }

        if (_subscribedDocument != null)
        {
            _subscribedDocument.PropertyChanged -= Document_PropertyChanged;
        }

        _subscribedDocument = document;

        if (_subscribedDocument != null)
        {
            _subscribedDocument.PropertyChanged += Document_PropertyChanged;
        }

        ShowQuestionDetails = document?.IsFlatQuestionScale == true;
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QDocument.IsFlatQuestionScale))
        {
            ShowQuestionDetails = _subscribedDocument?.IsFlatQuestionScale == true;
        }
    }

    private void Item_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not QDocument document || sender is not Control control)
        {
            return;
        }

        var item = control.DataContext as IItemViewModel ?? document.Package;
        Select(document, item);
        control.Focus();
        e.Handled = true;
    }

    private void Question_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not QDocument document
            || sender is not Border { DataContext: QuestionViewModel question }
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Select(document, question);
        ((Border)sender).Focus();
        _pendingPointerPress = e;
        _pendingQuestion = question;
        _dragStart = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void MoveBackward_Click(object? sender, RoutedEventArgs e) =>
        ExecuteQuestionCommand(sender, document => document.MoveFlatQuestionBackward);

    private void MoveForward_Click(object? sender, RoutedEventArgs e) =>
        ExecuteQuestionCommand(sender, document => document.MoveFlatQuestionForward);

    private void Duplicate_Click(object? sender, RoutedEventArgs e) =>
        ExecuteQuestionCommand(sender, document => document.DuplicateFlatQuestion);

    private void Question_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QDocument document || sender is not Border { DataContext: QuestionViewModel question })
        {
            return;
        }

        var command = e.Key == Key.D
                && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            ? document.DuplicateFlatQuestion
            : e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                ? GetMoveCommand(document, e.Key)
                : null;

        if (command == null)
        {
            return;
        }

        command.Execute(question);
        e.Handled = true;
    }

    private static System.Windows.Input.ICommand? GetMoveCommand(QDocument document, Key key)
    {
        if (document.IsFlatTableLayout)
        {
            return key switch
            {
                Key.Left => document.MoveFlatQuestionBackward,
                Key.Right => document.MoveFlatQuestionForward,
                _ => null,
            };
        }

        return key switch
        {
            Key.Up => document.MoveFlatQuestionBackward,
            Key.Down => document.MoveFlatQuestionForward,
            _ => null,
        };
    }

    private void ExecuteQuestionCommand(
        object? sender,
        Func<QDocument, System.Windows.Input.ICommand> commandSelector)
    {
        if (DataContext is QDocument document && sender is Button { DataContext: QuestionViewModel question })
        {
            commandSelector(document).Execute(question);
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
        if (!TryGetDragData(e, out _))
        {
            SetDropIndicator(sender, isActive: false);
            return;
        }

        var isValid = TryGetTargetLocation(sender, out _);

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

        if (!TryGetDragData(e, out var dragData))
        {
            return;
        }

        if (DataContext is not QDocument document
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

    private void ExternalFiles_DragOver(object? sender, DragEventArgs e)
    {
        if (TryGetDragData(e, out _))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var files = GetStorageFiles(e);
        var targetQuestion = FindTargetQuestion(e.Source);
        var canImport = DataContext is QDocument document
            && files.Any(file => document.CanImportExternalFile(
                file.Name,
                Path.GetExtension(file.Name),
                targetQuestion));

        e.DragEffects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ExternalFiles_Drop(object? sender, DragEventArgs e)
    {
        if (TryGetDragData(e, out _))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var files = GetStorageFiles(e);
        var targetQuestion = FindTargetQuestion(e.Source);
        var cancellationToken = _externalDropCancellation?.Token ?? CancellationToken.None;
        var imported = false;
        e.Handled = true;

        if (DataContext is not QDocument document || files.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await document.ImportExternalFileAsync(
                    ToPickedFile(file),
                    targetQuestion,
                    cancellationToken);
                imported |= result == ExternalFileImportResult.Imported;
            }

            e.DragEffects = imported ? DragDropEffects.Copy : DragDropEffects.None;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private static IReadOnlyList<IStorageFile> GetStorageFiles(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray() ?? Array.Empty<IStorageFile>();

    private static QuestionViewModel? FindTargetQuestion(object? eventSource) =>
        (eventSource as Visual)?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(control => control.DataContext)
            .OfType<QuestionViewModel>()
            .FirstOrDefault();

    private static PickedFile ToPickedFile(IStorageFile file) => new(
        file.TryGetLocalPath(),
        file.Name,
        Path.GetExtension(file.Name),
        async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await file.OpenReadAsync();
        });

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
