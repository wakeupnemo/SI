using Avalonia;

namespace SIQuester.Desktop;

internal static class Program
{
    internal const string WebKitDisableCompositingMode = "WEBKIT_DISABLE_COMPOSITING_MODE";

    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        ConfigureLinuxWebKitEnvironment();

        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    internal static void ConfigureLinuxWebKitEnvironment()
    {
        if (OperatingSystem.IsLinux()
            && Environment.GetEnvironmentVariable(WebKitDisableCompositingMode) == null)
        {
            Environment.SetEnvironmentVariable(WebKitDisableCompositingMode, "1");
        }
    }
}
