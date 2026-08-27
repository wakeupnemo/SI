using Microsoft.Extensions.Logging;
using SIPackages;
using SIQuester.ViewModel.Contracts;
using SIQuester.ViewModel.Contracts.Host;
using SIStatisticsService.Contract;

namespace SIQuester.ViewModel.Services;

/// <inheritdoc />
internal class DocumentViewModelFactory : IDocumentViewModelFactory
{
    private readonly StorageContextViewModel _storageContextViewModel;
    private readonly IPackageTemplatesRepository _packageTemplatesRepository;
    private readonly IClipboardService _clipboardService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISIStatisticsServiceClient _statisticsClient;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly IDocumentPersistenceService _documentPersistenceService;
    private readonly IDocumentRecoveryService _documentRecoveryService;
    private readonly IMediaMaterializationService _mediaMaterializationService;
    private readonly IAppPaths _appPaths;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IQuestionPreviewService _questionPreviewService;
    private readonly IMediaPreviewService _mediaPreviewService;

    public DocumentViewModelFactory(
        StorageContextViewModel storageContextViewModel,
        IPackageTemplatesRepository packageTemplatesRepository,
        IClipboardService clipboardService,
        ILoggerFactory loggerFactory,
        ISIStatisticsServiceClient statisticsClient,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IDocumentPersistenceService documentPersistenceService,
        IDocumentRecoveryService documentRecoveryService,
        IMediaMaterializationService mediaMaterializationService,
        IAppPaths appPaths,
        IUiDispatcher uiDispatcher,
        IQuestionPreviewService questionPreviewService,
        IMediaPreviewService mediaPreviewService)
    {
        _storageContextViewModel = storageContextViewModel;
        _packageTemplatesRepository = packageTemplatesRepository;
        _clipboardService = clipboardService;
        _loggerFactory = loggerFactory;
        _statisticsClient = statisticsClient;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _documentPersistenceService = documentPersistenceService;
        _documentRecoveryService = documentRecoveryService;
        _mediaMaterializationService = mediaMaterializationService;
        _appPaths = appPaths;
        _uiDispatcher = uiDispatcher;
        _questionPreviewService = questionPreviewService;
        _mediaPreviewService = mediaPreviewService;
    }

    public QDocument CreateViewModelFor(SIDocument document, string? fileName = null) => new(
        document,
        _storageContextViewModel,
        _packageTemplatesRepository,
        this,
        _clipboardService,
        _loggerFactory,
        _statisticsClient,
        _filePickerService,
        _dialogService,
        _documentPersistenceService,
        _documentRecoveryService,
        _mediaMaterializationService,
        _appPaths,
        _uiDispatcher,
        _questionPreviewService,
        _mediaPreviewService)
    {
        FileName = fileName ?? document.Package.Name
    };
}
