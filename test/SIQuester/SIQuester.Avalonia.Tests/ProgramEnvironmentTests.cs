using NUnit.Framework;
using SIQuester.Desktop;

namespace SIQuester.Avalonia.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class ProgramEnvironmentTests
{
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
