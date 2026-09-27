namespace Oire.PlanCake.Utils.Constants;

/// <summary>
/// Application-wide identity and path constants.
/// </summary>
/// <remarks>
/// Named <c>App</c> rather than <c>Application</c> on purpose: with
/// <c>UseWindowsForms</c> and implicit usings, an <c>Application</c> in this
/// namespace shadows <see cref="System.Windows.Forms.Application"/> inside every
/// file that imports it, and the resulting errors are confusing. Import it as
/// <c>using App = Oire.PlanCake.Utils.Constants.App;</c> where needed.
/// </remarks>
public static class App {
    /// <summary>
    /// Product name. Drives the data folder, the config file name and the gettext catalog
    /// name (<c>locale/&lt;code&gt;/PlanCake.mo</c>); the translation scripts read it from here.
    /// </summary>
    public const string Name = "PlanCake";
    public const string ManufacturerNameShort = "Oire";
    public const string ManufacturerNameFull = "Oire Software";
    public const string ConfigFileExtension = "cfg";

    /// <summary>The source repository, linked from the About dialog.</summary>
    public const string RepoUrl = "https://github.com/Oire/plan-cake";

    /// <summary>The NetSparkle appcast the update checks read.</summary>
    public const string AppcastUrl = "https://plancake.oire.dev/appcast.xml";

    /// <summary>
    /// The Ed25519 public key the appcast and the downloads are verified with: the contents of
    /// <c>keys/NetSparkle_Ed25519.pub</c> (base64 of 32 bytes; see "Updates" in <c>CLAUDE.md</c>).
    /// The private key beside it is never committed.
    /// </summary>
    /// <remarks>
    /// <c>UpdateServiceTests</c> checks that it is base64 of 32 bytes.
    /// </remarks>
    public const string UpdatePublicKey = "3grlY7WRufCg+1vbmTzxPu8uSBDDo/BV887KX0pwDfM=";

    /// <summary>
    /// Subfolder of <see cref="DataFolder"/> that holds user-generated content.
    /// Config files stay at the root of <see cref="DataFolder"/>
    /// so that wiping user data never takes the settings with it.
    /// </summary>
    public const string DataSubfolder = "data";

    /// <summary>
    /// When <c>true</c>, user data lives next to the executable under <c>userdata/</c>
    /// instead of under <c>%APPDATA%</c>. Detected once at static init by probing for the
    /// folder — drop a <c>userdata/</c> directory next to the EXE to opt in.
    /// </summary>
    public static readonly bool IsPortable = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "userdata"));

    public static readonly string DataFolder = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "userdata")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ManufacturerNameShort,
            Name
        );

    public static readonly string ConfigPath = Path.Combine(DataFolder, $"{Name}.{ConfigFileExtension}");

    /// <summary>Value of <c>Config.General.Language</c> that means "follow the OS".</summary>
    public const string SystemLanguageName = "System";

    /// <summary>
    /// Where the gettext catalogs live at run time. Resolved against
    /// <see cref="AppContext.BaseDirectory"/> rather than the current directory: a shortcut
    /// or a file association can start the process anywhere.
    /// </summary>
    public static readonly string LocalesFolder = Path.Combine(AppContext.BaseDirectory, "locale");

    /// <summary>
    /// Where the user manual lives at run time, one <c>&lt;language&gt;\manual.html</c> per
    /// language; resolved against <see cref="AppContext.BaseDirectory"/> like <see cref="LocalesFolder"/>.
    /// </summary>
    public static readonly string HelpFolder = Path.Combine(AppContext.BaseDirectory, "help");

    /// <summary>
    /// Where the page the document view shows lives at run time (<c>index.html</c>, <c>app.js</c>,
    /// …); resolved against <see cref="AppContext.BaseDirectory"/> like <see cref="LocalesFolder"/>.
    /// </summary>
    public static readonly string WebFolder = Path.Combine(AppContext.BaseDirectory, "web");

    /// <summary>
    /// The WebView2 user data folder (the browser's cache and state), under <see cref="DataFolder"/>
    /// because the install folder is not writable.
    /// </summary>
    public static readonly string WebView2DataFolder = Path.Combine(DataFolder, "WebView2");
}
