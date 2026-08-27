using Microsoft.Extensions.DependencyInjection;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Tests.Helpers;

namespace SIQuester.ViewModel.Tests;

[TestFixture]
internal sealed class DocumentSearchTests
{
    [Test]
    public async Task Search_RapidQueriesPublishesOnlyLatestResultsThroughDispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(uiDispatcher: dispatcher);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = TestHelper.CreateSimpleTestPackage();
        document.Package.Rounds[0].Themes[0].Questions[0].Right.Add("latest-query-例");
        using var qDocument = factory.CreateViewModelFor(document, "Rapid search");
        Exception? reportedError = null;
        qDocument.Error += (exception, _) => reportedError = exception;

        qDocument.SearchText = "obsolete-query";
        var obsoleteTask = qDocument.CurrentSearchTask;
        qDocument.SearchText = "latest-query-例";
        var latestTask = qDocument.CurrentSearchTask;

        await Task.WhenAll(obsoleteTask, latestTask);

        Assert.Multiple(() =>
        {
            Assert.That(reportedError, Is.Null, "Superseded search cancellation must not be reported as an error");
            Assert.That(qDocument.SearchResults?.Query, Is.EqualTo("latest-query-例"));
            Assert.That(qDocument.SearchResults?.Results, Has.Count.EqualTo(1));
            Assert.That(qDocument.SearchResults?.Results.Single(),
                Is.SameAs(qDocument.Package.Rounds[0].Themes[0].Questions[0]));
            Assert.That(qDocument.SearchFailed, Is.False);
            Assert.That(qDocument.ClearSearchText.CanBeExecuted, Is.True);
            Assert.That(qDocument.NextSearchResult.CanBeExecuted, Is.True);
            Assert.That(qDocument.PreviousSearchResult.CanBeExecuted, Is.True);
            Assert.That(dispatcher.InvocationCount, Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public async Task Search_OrdersHierarchyAndClearResetsPublishedState()
    {
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        using var document = TestHelper.CreateSimpleTestPackage();
        const string query = "shared-search-marker";
        document.Package.Name = $"Package {query}";
        document.Package.Rounds[0].Name = $"Round {query}";
        document.Package.Rounds[0].Themes[0].Name = $"Theme {query}";
        document.Package.Rounds[0].Themes[0].Questions[0].Wrong.Add(query);
        using var qDocument = factory.CreateViewModelFor(document, "Ordered search");

        qDocument.SearchText = query;
        await qDocument.CurrentSearchTask;

        Assert.That(qDocument.SearchResults?.Results, Is.EqualTo(new IItemViewModel[]
        {
            qDocument.Package,
            qDocument.Package.Rounds[0],
            qDocument.Package.Rounds[0].Themes[0],
            qDocument.Package.Rounds[0].Themes[0].Questions[0],
        }));

        qDocument.ClearSearchText.Execute(null);
        await qDocument.CurrentSearchTask;

        Assert.Multiple(() =>
        {
            Assert.That(qDocument.SearchText, Is.Empty);
            Assert.That(qDocument.SearchResults, Is.Null);
            Assert.That(qDocument.SearchFailed, Is.False);
            Assert.That(qDocument.ClearSearchText.CanBeExecuted, Is.False);
            Assert.That(qDocument.NextSearchResult.CanBeExecuted, Is.False);
            Assert.That(qDocument.PreviousSearchResult.CanBeExecuted, Is.False);
        });
    }

    [Test]
    public async Task Search_DisposeDuringDebounceStopsPublicationAndLeavesOtherDocumentIndependent()
    {
        var dispatcher = new RecordingUiDispatcher();
        using var serviceProvider = (ServiceProvider)TestHelper.CreateServiceProvider(uiDispatcher: dispatcher);
        var factory = serviceProvider.GetRequiredService<IDocumentViewModelFactory>();
        var closingDocument = factory.CreateViewModelFor(TestHelper.CreateSimpleTestPackage(), "Closing search");
        using var survivingDocumentModel = TestHelper.CreateSimpleTestPackage();
        survivingDocumentModel.Package.Rounds[0].Themes[0].Questions[0].Right.Add("surviving-query");
        using var survivingDocument = factory.CreateViewModelFor(survivingDocumentModel, "Surviving search");
        Exception? closingError = null;
        closingDocument.Error += (exception, _) => closingError = exception;

        closingDocument.SearchText = "closing-query";
        var closingTask = closingDocument.CurrentSearchTask;
        survivingDocument.SearchText = "surviving-query";
        var survivingTask = survivingDocument.CurrentSearchTask;
        closingDocument.Dispose();

        await Task.WhenAll(closingTask, survivingTask);

        Assert.Multiple(() =>
        {
            Assert.That(closingError, Is.Null);
            Assert.That(closingDocument.SearchResults, Is.Null,
                "A disposed document must not publish a late search result");
            Assert.That(survivingDocument.SearchResults?.Query, Is.EqualTo("surviving-query"));
            Assert.That(survivingDocument.SearchResults?.Results, Has.Count.EqualTo(1));
        });
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        internal int InvocationCount { get; private set; }

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            action();
            return ValueTask.CompletedTask;
        }
    }
}
