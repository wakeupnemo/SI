using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Data.Converters;
using Avalonia.Input;
using SIQuester.ViewModel;
using System.Globalization;

namespace SIQuester.Avalonia.Views;

public partial class DocumentEditorView : UserControl
{
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

    private void Navigator_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is QDocument document
            && sender is TreeView { SelectedItem: IItemViewModel selectedItem })
        {
            document.ActiveNode = selectedItem;
        }
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
