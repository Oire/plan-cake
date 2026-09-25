using Serilog;
using SharpConfig;
using App = Oire.WinFormsTemplate.Utils.Constants.App;

namespace Oire.WinFormsTemplate.Utils;

/// <summary>
/// Static configuration container backed by an INI file under <see cref="App.DataFolder"/>.
/// Call <see cref="Load"/> once at startup and <see cref="Save"/> whenever a section changes.
/// The section properties are safe to read from any thread once <see cref="Load"/> has returned.
/// </summary>
/// <remarks>
/// Neither method throws and neither shows a dialog: configuration is a convenience, not a
/// precondition for running. A missing file is written out with the defaults; an unreadable
/// one is logged and the defaults are used for the session, so a hand-edited typo in the INI
/// cannot leave the user with an app that refuses to start.
/// </remarks>
public static class Config {
    /// <summary>
    /// Test hook: redirects the config file away from <c>%APPDATA%</c> so a test run does not
    /// clobber the developer's real settings. Set it before calling <see cref="Load"/>.
    /// </summary>
    internal static string? OverrideFilePath { get; set; }

    private static string FilePath => OverrideFilePath ?? App.ConfigPath;

    public static SectionGeneral General { get; private set; } = new();

    #region Config section classes

    /// <summary>
    /// App-level settings that apply wherever the user happens to be. Add further sections as
    /// sibling classes plus a matching property and a line in <see cref="Load"/> / <see cref="Save"/>.
    /// </summary>
    public class SectionGeneral {
        /// <summary>A culture name such as <c>"fr-FR"</c>, or <see cref="App.SystemLanguageName"/>.</summary>
        public string Language { get; set; } = App.SystemLanguageName;

        public bool ConfirmExit { get; set; } = true;
    }

    #endregion

    /// <summary>
    /// Reads the configuration file, or creates it with the defaults if it does not exist yet.
    /// </summary>
    public static void Load() {
        try {
            var cfg = Configuration.LoadFromFile(FilePath);
            General = cfg[nameof(General)].ToObject<SectionGeneral>();
        } catch (FileNotFoundException) {
            // First run. Not an error — write the defaults out so the user has a file to edit.
            General = new SectionGeneral();
            Save();
        } catch (DirectoryNotFoundException) {
            General = new SectionGeneral();
            Save();
        } catch (Exception ex) {
            // Malformed INI, a locked file, a permissions problem. Carry on with the defaults
            // rather than refusing to start, but say so in the log.
            Log.Warning(ex, "Config: unable to read {Path}, falling back to defaults", FilePath);
            General = new SectionGeneral();
        }
    }

    /// <summary>
    /// Writes the current sections back to disk, creating the data folder if needed.
    /// </summary>
    /// <returns><c>true</c> when the file was written; <c>false</c> when it could not be.</returns>
    public static bool Save() {
        try {
            var folder = Path.GetDirectoryName(FilePath);

            if (!String.IsNullOrEmpty(folder)) {
                Directory.CreateDirectory(folder);
            }

            var cfg = new Configuration();
            cfg.Add(Section.FromObject(nameof(General), General));
            cfg.SaveToFile(FilePath);

            return true;
        } catch (Exception ex) {
            Log.Error(ex, "Config: unable to save {Path}", FilePath);

            return false;
        }
    }
}
