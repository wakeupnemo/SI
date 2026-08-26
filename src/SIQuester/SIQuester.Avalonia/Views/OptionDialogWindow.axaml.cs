using Avalonia.Automation;
using Avalonia.Controls;
using SIQuester.ViewModel.Contracts;

namespace SIQuester.Avalonia.Views;

public partial class OptionDialogWindow : Window
{
    private string? _result;

    public OptionDialogWindow()
    {
        InitializeComponent();
    }

    public OptionDialogWindow(string message, IReadOnlyList<DialogOption> options) : this()
    {
        MessageText.Text = message;

        foreach (var option in options)
        {
            var button = new Button
            {
                Content = option.Label,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            };
            button.Classes.Add("command");
            AutomationProperties.SetName(button, option.Label);
            button.Click += (_, _) => CloseWith(option.Id);
            OptionsPanel.Children.Add(button);
        }
    }

    public async ValueTask<string?> ShowOptionAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result;
    }

    private void CloseWith(string result)
    {
        _result = result;
        Close();
    }
}
