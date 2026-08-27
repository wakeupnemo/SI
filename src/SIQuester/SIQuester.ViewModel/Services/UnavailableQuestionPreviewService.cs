using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel.Services;

/// <summary>
/// Provides a safe default when a host has not registered a native preview backend.
/// </summary>
internal sealed class UnavailableQuestionPreviewService : IQuestionPreviewService
{
    public QuestionPreviewHostDescriptor GetHostDescriptor() =>
        QuestionPreviewHostDescriptor.Unavailable(QuestionPreviewAvailability.BackendUnavailable);
}
