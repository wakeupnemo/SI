namespace SIQuester.ViewModel;

/// <summary>
/// Represents one virtualizable theme row in the flat list layout.
/// </summary>
public sealed record FlatDetailRow(
    RoundViewModel Round,
    ThemeViewModel? Theme,
    bool StartsRound)
{
    /// <summary>
    /// Gets whether this row contains a theme. Empty rounds retain a header-only row.
    /// </summary>
    public bool HasTheme => Theme != null;
}
