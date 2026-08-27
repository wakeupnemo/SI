using SIQuester.ViewModel.Contracts;

namespace SIQuester.ViewModel.Services;

internal sealed class UnavailableMediaPreviewService : IMediaPreviewService
{
    public IMediaPreviewSession CreateSession(MediaPreviewSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Session();
    }

    private sealed class Session : IMediaPreviewSession
    {
        public Uri? Source => null;

        public QuestionPreviewAvailability Availability => QuestionPreviewAvailability.BackendUnavailable;

        public QuestionPreviewBackendRequirement BackendRequirement => QuestionPreviewBackendRequirement.None;

        public void Dispose() { }
    }
}
