using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(SIQuester.Avalonia.Tests.TestAppBuilder))]

namespace SIQuester.Avalonia.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

internal sealed class TestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://SIQuester.Avalonia.Tests"))
        {
            Source = new Uri("avares://SIQuester.Avalonia/Styles/DesignTokens.axaml"),
        });
    }
}
