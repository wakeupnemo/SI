namespace SIQuester.ViewModel.Contracts;

/// <summary>
/// Controls application lifetime without exposing a desktop UI framework.
/// </summary>
public interface IApplicationLifetimeService
{
    void RequestExit();
}
