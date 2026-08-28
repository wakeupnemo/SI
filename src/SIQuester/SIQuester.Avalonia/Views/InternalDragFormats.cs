using Avalonia.Input;

namespace SIQuester.Avalonia.Views;

/// <summary>
/// Defines versioned in-process drag formats shared by the tree and flat document views.
/// </summary>
internal static class InternalDragFormats
{
    internal static readonly DataFormat<string> Question =
        DataFormat.CreateStringApplicationFormat("SIQuester.FlatQuestion.v1");

    internal static readonly DataFormat<string> Theme =
        DataFormat.CreateStringApplicationFormat("SIQuester.Theme.v1");
}
