using NUnit.Framework;
using Avalonia.Logging;
using SIQuester.Desktop;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ProgramEnvironmentTests
{
    [Test]
    public void ApplicationVersionMatchesCrossPlatformRelease()
    {
        Assert.That(App.GetApplicationVersion(), Does.StartWith("0.2.0"));
    }

    [Test]
    public void PersistentAvaloniaSinkKeepsReleaseLogsFocusedOnErrorsAndFailures()
    {
        var sink = new AvaloniaPersistentLogSink();

        Assert.Multiple(() =>
        {
            Assert.That(sink.IsEnabled(LogEventLevel.Information, "test"), Is.False);
            Assert.That(sink.IsEnabled(LogEventLevel.Warning, "test"), Is.False);
            Assert.That(sink.IsEnabled(LogEventLevel.Error, "test"), Is.True);
            Assert.That(sink.IsEnabled(LogEventLevel.Fatal, "test"), Is.True);
        });
    }

    [Test]
    public void ConfigureLinuxWebKitEnvironment_SetsCompatibilityValueWhenUnset()
    {
        var originalValue = Environment.GetEnvironmentVariable(Program.WebKitDisableCompositingMode);

        try
        {
            Environment.SetEnvironmentVariable(Program.WebKitDisableCompositingMode, null);

            Program.ConfigureLinuxWebKitEnvironment();

            Assert.That(
                Environment.GetEnvironmentVariable(Program.WebKitDisableCompositingMode),
                Is.EqualTo(OperatingSystem.IsLinux() ? "1" : null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Program.WebKitDisableCompositingMode, originalValue);
        }
    }

    [Test]
    public void ConfigureLinuxWebKitEnvironment_PreservesExplicitValue()
    {
        var originalValue = Environment.GetEnvironmentVariable(Program.WebKitDisableCompositingMode);

        try
        {
            Environment.SetEnvironmentVariable(Program.WebKitDisableCompositingMode, "0");

            Program.ConfigureLinuxWebKitEnvironment();

            Assert.That(Environment.GetEnvironmentVariable(Program.WebKitDisableCompositingMode), Is.EqualTo("0"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Program.WebKitDisableCompositingMode, originalValue);
        }
    }
}
