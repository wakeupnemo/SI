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
        Assert.That(App.GetApplicationVersion(), Does.StartWith("0.3.2"));
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
    public void PersistentAvaloniaSinkSuppressesOnlyKnownBenignIbusShutdownDiagnostic()
    {
        const string benignMessage = "Error while destroying the context: "
            + "org.freedesktop.DBus.Error.UnknownMethod: Method Destroy is not implemented";

        Assert.Multiple(() =>
        {
            Assert.That(
                AvaloniaPersistentLogSink.IsBenignIbusShutdownDiagnostic(
                    "IME",
                    "IBusX11TextInputMethod",
                    benignMessage),
                Is.True);
            Assert.That(
                AvaloniaPersistentLogSink.IsBenignIbusShutdownDiagnostic(
                    "IME",
                    "IBusX11TextInputMethod",
                    "Unexpected input-method failure"),
                Is.False);
            Assert.That(
                AvaloniaPersistentLogSink.IsBenignIbusShutdownDiagnostic(
                    "Rendering",
                    "IBusX11TextInputMethod",
                    benignMessage),
                Is.False);
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
