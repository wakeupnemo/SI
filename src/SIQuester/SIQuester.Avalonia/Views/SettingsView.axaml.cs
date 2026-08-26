using Avalonia.Controls;
using Avalonia.Data.Converters;
using SIQuester.Avalonia.Localization;
using SIQuester.Model;
using System.Globalization;

namespace SIQuester.Avalonia.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

public sealed class DesktopThemeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DesktopThemePreference.Light => UiStrings.ThemeLight,
        DesktopThemePreference.Dark => UiStrings.ThemeDark,
        _ => UiStrings.ThemeSystem,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
