using SIQuester.Model;
using System.ComponentModel;

namespace SIQuester.ViewModel.Services;

/// <summary>Describes an alias action presented by a SPARD template editor.</summary>
public sealed record SpardAliasDescriptor(string Key, string DisplayName, string Color);

/// <summary>
/// Synchronizes one legacy template view model with the UI-independent structural SPARD editor.
/// </summary>
public sealed class SpardTemplateEditorSession : IDisposable
{
    private bool _updatingTemplate;
    private bool _disposed;

    /// <summary>Gets the existing template view model.</summary>
    public SpardTemplateViewModel Template { get; }

    /// <summary>Gets the structural editor for this template.</summary>
    public SpardEditorController Editor { get; }

    /// <summary>Gets stable presentation data for the template's alias actions.</summary>
    public IReadOnlyList<SpardAliasDescriptor> Aliases { get; }

    /// <summary>Creates and attaches a session for <paramref name="template"/>.</summary>
    public SpardTemplateEditorSession(SpardTemplateViewModel template)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Aliases = template.Aliases
            .Select(pair => new SpardAliasDescriptor(pair.Key, pair.Value.VisibleName, pair.Value.Color))
            .ToArray();
        Editor = new SpardEditorController(ResolveAliasDisplayName);
        Editor.Load(template.Transform);

        Template.PropertyChanged += Template_PropertyChanged;
        Template.AliasInserted += Template_AliasInserted;
        Template.OptionalInserted += Template_OptionalInserted;
        Editor.TextChanged += Editor_TextChanged;
        Editor.Committed += Editor_Committed;
    }

    private string ResolveAliasDisplayName(string aliasName) =>
        Template.Aliases.TryGetValue(aliasName, out EditAlias? alias)
            ? alias.VisibleName
            : aliasName;

    private void Template_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_updatingTemplate && e.PropertyName == nameof(SpardTemplateViewModel.Transform))
        {
            Editor.Load(Template.Transform);
        }
    }

    private void Template_AliasInserted(string aliasName)
    {
        if (!string.IsNullOrWhiteSpace(aliasName))
        {
            Editor.InsertAlias(aliasName);
        }
    }

    private void Template_OptionalInserted() => Editor.InsertOptional();

    private void Editor_TextChanged(object? sender, EventArgs e) => ApplyEditorText();

    private void Editor_Committed(object? sender, SpardTextCommittedEventArgs e) => ApplyEditorText(e.Text);

    private void ApplyEditorText(string? text = null)
    {
        var serializedText = text ?? Editor.SerializedText;

        if (Template.Transform == serializedText)
        {
            return;
        }

        _updatingTemplate = true;

        try
        {
            Template.Transform = serializedText;
        }
        finally
        {
            _updatingTemplate = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Template.PropertyChanged -= Template_PropertyChanged;
        Template.AliasInserted -= Template_AliasInserted;
        Template.OptionalInserted -= Template_OptionalInserted;
        Editor.TextChanged -= Editor_TextChanged;
        Editor.Committed -= Editor_Committed;
    }
}
