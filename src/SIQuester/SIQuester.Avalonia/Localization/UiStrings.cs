using System.Globalization;
using System.Resources;

namespace SIQuester.Avalonia.Localization;

/// <summary>
/// Provides localized strings for the Avalonia frontend. Language changes apply after restart.
/// </summary>
public static class UiStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "SIQuester.Avalonia.Localization.UiResources",
        typeof(UiStrings).Assembly);

    public static string Get(string name) => ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing UI resource: {name}");

    public static string AppTitle => Get(nameof(AppTitle));
    public static string New => Get(nameof(New));
    public static string Open => Get(nameof(Open));
    public static string Save => Get(nameof(Save));
    public static string SaveAs => Get(nameof(SaveAs));
    public static string Undo => Get(nameof(Undo));
    public static string Redo => Get(nameof(Redo));
    public static string Close => Get(nameof(Close));
    public static string Exit => Get(nameof(Exit));
    public static string Create => Get(nameof(Create));
    public static string Cancel => Get(nameof(Cancel));
    public static string Discard => Get(nameof(Discard));
    public static string Yes => Get(nameof(Yes));
    public static string No => Get(nameof(No));
    public static string OK => Get(nameof(OK));
    public static string File => Get(nameof(File));
    public static string Edit => Get(nameof(Edit));
    public static string View => Get(nameof(View));
    public static string Navigator => Get(nameof(Navigator));
    public static string Editor => Get(nameof(Editor));
    public static string Inspector => Get(nameof(Inspector));
    public static string Media => Get(nameof(Media));
    public static string StatusReady => Get(nameof(StatusReady));
    public static string EmptyTitle => Get(nameof(EmptyTitle));
    public static string EmptyDescription => Get(nameof(EmptyDescription));
    public static string PackageName => Get(nameof(PackageName));
    public static string PackageAuthor => Get(nameof(PackageAuthor));
    public static string Template => Get(nameof(Template));
    public static string QualityControl => Get(nameof(QualityControl));
    public static string Name => Get(nameof(Name));
    public static string Package => Get(nameof(Package));
    public static string Round => Get(nameof(Round));
    public static string Theme => Get(nameof(Theme));
    public static string Question => Get(nameof(Question));
    public static string QuestionText => Get(nameof(QuestionText));
    public static string Price => Get(nameof(Price));
    public static string RightAnswer => Get(nameof(RightAnswer));
    public static string WrongAnswer => Get(nameof(WrongAnswer));
    public static string Comments => Get(nameof(Comments));
    public static string AddRound => Get(nameof(AddRound));
    public static string AddTheme => Get(nameof(AddTheme));
    public static string AddQuestion => Get(nameof(AddQuestion));
    public static string Delete => Get(nameof(Delete));
    public static string Images => Get(nameof(Images));
    public static string Audio => Get(nameof(Audio));
    public static string Video => Get(nameof(Video));
    public static string Html => Get(nameof(Html));
    public static string NoSelection => Get(nameof(NoSelection));
    public static string Saving => Get(nameof(Saving));
    public static string Loading => Get(nameof(Loading));
    public static string Options => Get(nameof(Options));
    public static string ThemeMode => Get(nameof(ThemeMode));
    public static string ThemeSystem => Get(nameof(ThemeSystem));
    public static string ThemeLight => Get(nameof(ThemeLight));
    public static string ThemeDark => Get(nameof(ThemeDark));
    public static string Language => Get(nameof(Language));
    public static string LanguageRestart => Get(nameof(LanguageRestart));
    public static string SettingsSavingError => Get(nameof(SettingsSavingError));
    public static string Reset => Get(nameof(Reset));
}
