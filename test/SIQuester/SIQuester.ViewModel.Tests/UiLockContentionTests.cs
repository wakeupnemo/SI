using Microsoft.Extensions.DependencyInjection;
using SIPackages;
using SIPackages.Core;
using SIQuester.Model;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;
using SIQuester.ViewModel.Tests.Mocks;
using Utils.Commands;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class UiLockContentionTests
{
    private static readonly byte[] ImageBytes = [1, 2, 3, 4, 5];

    [Test]
    public async Task Validation_YieldsWhileRecoveryOwnsDocumentLock()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Validation lock");
        var imagePath = AddPendingImage(document);
        var previousCheckFileSize = AppSettings.Default.CheckFileSize;
        AppSettings.Default.CheckFileSize = true;

        try
        {
            await AssertYieldsWhileDocumentLockHeldAsync(
                document,
                async () => await document.CheckLinksAsync());
        }
        finally
        {
            AppSettings.Default.CheckFileSize = previousCheckFileSize;
            document.Dispose();
            File.Delete(imagePath);
        }
    }

    [Test]
    public async Task QualityControl_YieldsWhileRecoveryOwnsDocumentLockWithoutWpfMaterialization()
    {
        var platform = TestHelper.EnsurePlatformManager();
        var materializationCount = platform.PrepareMediaCallCount;
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Quality lock");
        var imagePath = AddPendingImage(document);

        try
        {
            var qualityAccepted = await AssertYieldsWhileDocumentLockHeldAsync(
                document,
                () => document.CheckPackageQualityAsync());

            Assert.Multiple(() =>
            {
                Assert.That(qualityAccepted, Is.True);
                Assert.That(platform.PrepareMediaCallCount, Is.EqualTo(materializationCount),
                    "the Avalonia quality path must inspect package streams without WPF media materialization");
            });
        }
        finally
        {
            document.Dispose();
            File.Delete(imagePath);
        }
    }

    [Test]
    public async Task CopyWithMedia_YieldsWhileRecoveryOwnsDocumentLockWithoutWpfMaterialization()
    {
        var platform = TestHelper.EnsurePlatformManager();
        var materializationCount = platform.PrepareMediaCallCount;
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var package = TestHelper.CreateSimpleTestPackage();
        using var document = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(package, "Copy lock");
        var imagePath = AddPendingImage(document);
        document.ActiveNode = document.Package.Rounds[0].Themes[0].Questions[0];

        try
        {
            await AssertYieldsWhileDocumentLockHeldAsync(
                document,
                () => document.Copy_ExecutedAsync(null));

            Assert.That(platform.PrepareMediaCallCount, Is.EqualTo(materializationCount),
                "the Avalonia clipboard path must embed package bytes without WPF media materialization");
        }
        finally
        {
            document.Dispose();
            File.Delete(imagePath);
        }
    }

    [Test]
    public async Task PasteWithMedia_YieldsWhileRecoveryOwnsDocumentLock()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        using var sourcePackage = TestHelper.CreateSimpleTestPackage();
        using var targetPackage = TestHelper.CreateSimpleTestPackage();
        using var source = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(sourcePackage, "Paste source");
        using var target = serviceProvider.GetRequiredService<IDocumentViewModelFactory>()
            .CreateViewModelFor(targetPackage, "Paste target");
        var imagePath = AddPendingImage(source);
        source.ActiveNode = source.Package.Rounds[0].Themes[0].Questions[0];
        target.ActiveNode = target.Package.Rounds[0].Themes[0];

        try
        {
            await ((IAsyncCommand)source.Copy).ExecuteAsync(null);

            await AssertYieldsWhileDocumentLockHeldAsync(
                target,
                () => target.Paste_ExecutedAsync(null));

            Assert.Multiple(() =>
            {
                Assert.That(target.Package.Rounds[0].Themes[0].Questions, Has.Count.EqualTo(2));
                Assert.That(target.Images.Files.Select(item => item.Name), Does.Contain("lock image.png"));
            });
        }
        finally
        {
            source.Dispose();
            target.Dispose();
            File.Delete(imagePath);
        }
    }

    private static string AddPendingImage(QDocument document)
    {
        var path = Path.Combine(Path.GetTempPath(), $"siquester-ui-lock-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, ImageBytes);
        document.Images.AddFile(path, "lock image.png");
        document.Package.Rounds[0].Themes[0].Questions[0].Model.Script!.Steps[0]
            .Parameters[StepParameterNames.Content].ContentValue!.Add(new ContentItem
            {
                Type = ContentTypes.Image,
                IsRef = true,
                Value = "lock image.png",
            });
        return path;
    }

    private static async Task AssertYieldsWhileDocumentLockHeldAsync(
        QDocument document,
        Func<Task> operation)
    {
        await AssertYieldsWhileDocumentLockHeldAsync(
            document,
            async () =>
            {
                await operation();
                return true;
            });
    }

    private static async Task<T> AssertYieldsWhileDocumentLockHeldAsync<T>(
        QDocument document,
        Func<Task<T>> operation)
    {
        var lockEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockHolder = Task.Run(async () => await document.Lock.WithLockAsync(async () =>
        {
            lockEntered.SetResult();
            await releaseLock.Task;
        }));

        await lockEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var operationTask = Task.Run(async () =>
        {
            var task = operation();
            invocationReturned.SetResult();
            return await task;
        });

        await Task.Delay(100);
        var returnedWithoutBlocking = invocationReturned.Task.IsCompleted;
        releaseLock.SetResult();

        var result = await operationTask.WaitAsync(TimeSpan.FromSeconds(5));
        await lockHolder.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(returnedWithoutBlocking, Is.True,
            "the UI command must return an incomplete task instead of synchronously waiting for document persistence");
        return result;
    }
}
