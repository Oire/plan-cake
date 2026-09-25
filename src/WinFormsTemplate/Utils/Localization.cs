using System.Globalization;
using GetText;
using App = Oire.WinFormsTemplate.Utils.Constants.App;

namespace Oire.WinFormsTemplate.Utils;

/// <summary>
/// gettext front end. Import it as <c>using static Oire.WinFormsTemplate.Utils.Localization;</c>
/// and wrap every user-visible literal in <see cref="_(string)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Catalogs are <c>.po</c> sources under <c>locale/</c>, compiled to <c>.mo</c> by
/// <c>locale/scripts/Compile-Translations.ps1</c>. <c>.resx</c> is not used: the WinForms
/// designer generates one on occasion, and it should be deleted when it does.
/// </para>
/// <para>
/// <see cref="SetLanguage"/> must be called once at startup, before any window is created.
/// </para>
/// </remarks>
internal static class Localization {
    private static readonly Lock _lock = new();
    private static ICatalog? _stringsCatalog;
    private static string? _languageOverride;

    public static ICatalog Catalog {
        get {
            lock (_lock) {
                return _stringsCatalog ??= new Catalog(App.Name, App.LocalesFolder, GetCurrentCulture());
            }
        }
    }

    /// <summary>
    /// Raised at the end of <see cref="SetLanguage"/>, once the catalog has been reset and the
    /// thread cultures updated. Subscribers re-evaluate anything whose text was captured once
    /// rather than looked up on demand — menus, above all.
    /// </summary>
    /// <remarks>
    /// This is a static event, so a subscriber that forgets to unsubscribe stays alive for the
    /// life of the process. Unsubscribe in <c>Dispose</c>.
    /// </remarks>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Switches the UI language and resets the catalog. Call once at startup with
    /// <c>Config.General.Language</c>, and again whenever the user changes it in Settings.
    /// </summary>
    /// <param name="language">A culture name such as <c>"fr-FR"</c>, or
    /// <see cref="App.SystemLanguageName"/> to follow the OS.</param>
    public static void SetLanguage(string? language) {
        _languageOverride = language;

        lock (_lock) {
            _stringsCatalog = null;
        }

        var culture = GetCurrentCulture();
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Resolves the culture whose catalog should be loaded: the override if one is set,
    /// otherwise the OS UI culture, falling back to the neutral parent and finally to en-US
    /// when no catalog directory matches.
    /// </summary>
    public static CultureInfo GetCurrentCulture() {
        var culture = String.IsNullOrWhiteSpace(_languageOverride) || _languageOverride == App.SystemLanguageName
            ? CultureInfo.InstalledUICulture
            : new CultureInfo(_languageOverride);

        if (Directory.Exists(Path.Combine(App.LocalesFolder, culture.Name))) {
            return culture;
        }

        // No fr-CA catalog? Try fr. No fr either? English, which is compiled into the
        // source literals and so always available.
        return Directory.Exists(Path.Combine(App.LocalesFolder, culture.Parent.Name)) && !String.IsNullOrEmpty(culture.Parent.Name)
            ? culture.Parent
            : new CultureInfo("en-US");
    }

    public static string _(string text) => Catalog.GetString(text);

    public static string _(string text, params object[] args) => Catalog.GetString(text, args);

    public static string _n(string text, string pluralText, long n) => Catalog.GetPluralString(text, pluralText, n);

    public static string _n(string text, string pluralText, long n, params object[] args) =>
        Catalog.GetPluralString(text, pluralText, n, args);

    public static string _p(string context, string text) => Catalog.GetParticularString(context, text);

    public static string _p(string context, string text, params object[] args) =>
        Catalog.GetParticularString(context, text, args);

    public static string _pn(string context, string text, string pluralText, long n) =>
        Catalog.GetParticularPluralString(context, text, pluralText, n);

    public static string _pn(string context, string text, string pluralText, long n, params object[] args) =>
        Catalog.GetParticularPluralString(context, text, pluralText, n, args);
}
