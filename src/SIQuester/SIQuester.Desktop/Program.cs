using Avalonia;
using NLog;
using SIQuester.ViewModel.Services;
using System.Diagnostics;

namespace SIQuester.Desktop;

internal static class Program
{
    internal const string WebKitDisableCompositingMode = "WEBKIT_DISABLE_COMPOSITING_MODE";

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            WriteLastResortFailure(exception, "SIQuester process terminated unexpectedly");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // Packaged Linux builds use the native launcher to set this before the
        // apphost starts. Keep this fallback for direct executable launches.
        ConfigureLinuxWebKitEnvironment();

        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .UseWaylandWithFallback()
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

    internal static void WriteLastResortFailure(Exception exception, string message)
    {
        try
        {
            LogManager.GetCurrentClassLogger().Fatal(exception, message);
            LogManager.Flush(TimeSpan.FromSeconds(2));

            var paths = new PlatformAppPaths();
            paths.EnsureDirectories();
            File.AppendAllText(
                Path.Combine(paths.LogDirectory, "siquester-last-resort.log"),
                $"{DateTimeOffset.UtcNow:O}|FATAL|{message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch (Exception loggingException)
        {
            Trace.TraceError($"SIQuester last-resort logging failed: {loggingException}");
            Trace.TraceError(exception.ToString());
        }
    }
}
