using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class QualityControlEditingTests
{
    [Test]
    public async Task EnableDisableQualityControl_TracksUndoRedoAndSaveReload()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = TestHelper.CreateSimpleTestPackage();
        using var qDocument = factory.CreateViewModelFor(document, "Контроль качества 例");
        var package = qDocument.Package;
        var filePath = Path.Combine(Path.GetTempPath(), $"SIQuester quality {Guid.NewGuid():N} 例.siq");
        qDocument.Path = filePath;

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(package.HasQualityControl, Is.False);
                Assert.That(package.EnableQualityControl.CanBeExecuted, Is.True);
                Assert.That(package.DisableQualityControl.CanBeExecuted, Is.False);
            });

            await package.EnableQualityControl.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(package.HasQualityControl, Is.True);
                Assert.That(document.Package.HasQualityControl, Is.True);
                Assert.That(package.EnableQualityControl.CanBeExecuted, Is.False);
                Assert.That(package.DisableQualityControl.CanBeExecuted, Is.True);
                Assert.That(qDocument.Changed, Is.True);
                Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.True);
            });

            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(package.HasQualityControl, Is.False);
            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(package.HasQualityControl, Is.True);

            await qDocument.Save.ExecuteAsync(null);

            await using (var stream = File.OpenRead(filePath))
            using (var reloaded = SIDocument.Load(stream))
            {
                Assert.That(reloaded.Package.HasQualityControl, Is.True);
            }

            package.DisableQualityControl.Execute(null);
            Assert.That(package.HasQualityControl, Is.False);
            qDocument.OperationsManager.Undo.Execute(null);
            Assert.That(package.HasQualityControl, Is.True);
            qDocument.OperationsManager.Redo.Execute(null);
            Assert.That(package.HasQualityControl, Is.False);

            await qDocument.Save.ExecuteAsync(null);

            await using var disabledStream = File.OpenRead(filePath);
            using var disabledReload = SIDocument.Load(disabledStream);
            Assert.That(disabledReload.Package.HasQualityControl, Is.False);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task EnableQualityControl_WithExternalContentUsesNeutralDialogAndLeavesFlagDisabled()
    {
        var platform = TestHelper.EnsurePlatformManager();
        platform.Messages.Clear();
        platform.LegacyExclamationMessages.Clear();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = TestHelper.CreateSimpleTestPackage();
        document.Package.Rounds[0].Themes[0].Questions[0].Script!.Steps[0]
            .Parameters[StepParameterNames.Content].ContentValue!.Add(new ContentItem
            {
                Type = ContentTypes.Image,
                IsRef = false,
                Value = "https://example.test/картинка 例.png",
            });
        using var qDocument = factory.CreateViewModelFor(document, "Invalid quality");

        await qDocument.Package.EnableQualityControl.ExecuteAsync(null);

        Assert.That(platform.Messages, Has.Count.EqualTo(1));

        Assert.Multiple(() =>
        {
            Assert.That(qDocument.Package.HasQualityControl, Is.False);
            Assert.That(qDocument.Package.EnableQualityControl.CanBeExecuted, Is.True);
            Assert.That(qDocument.Package.DisableQualityControl.CanBeExecuted, Is.False);
            Assert.That(qDocument.IsSideOpened, Is.True);
            Assert.That(qDocument.SideIndex, Is.EqualTo(6));
            Assert.That(platform.Messages.Single(), Does.Contain("https://example.test/картинка 例.png"));
            Assert.That(platform.LegacyExclamationMessages, Is.Empty,
                "The Avalonia-safe command must not use the legacy PlatformManager dialog path");
            Assert.That(qDocument.OperationsManager.Undo.CanBeExecuted, Is.False,
                "Rejected validation must not create an undoable model mutation");
        });
    }
}
