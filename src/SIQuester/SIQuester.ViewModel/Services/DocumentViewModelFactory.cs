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
        IMediaMaterializationService mediaMaterializationService)
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
        _mediaMaterializationService)
    {
        FileName = fileName ?? document.Package.Name
    };
}
