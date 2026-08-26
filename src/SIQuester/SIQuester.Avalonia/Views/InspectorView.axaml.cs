using Avalonia;
using Avalonia.Controls;
using SIQuester.ViewModel;

namespace SIQuester.Avalonia.Views;

public partial class InspectorView : UserControl
{
    public static readonly StyledProperty<IItemViewModel?> SelectedItemProperty =
        AvaloniaProperty.Register<InspectorView, IItemViewModel?>(nameof(SelectedItem));

    public IItemViewModel? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public InspectorView() => InitializeComponent();
}
