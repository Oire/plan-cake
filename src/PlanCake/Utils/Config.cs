using System.Reflection;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils.Enums;
using Serilog;
using SharpConfig;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Utils;

/// <summary>
/// Static configuration container backed by an INI file under <see cref="App.DataFolder"/>
/// (<c>PlanCake.cfg</c>, sections <c>[General]</c>, <c>[Notes]</c> and <c>[Advanced]</c>; Technical
/// details → "Settings" in the plan). Call <see cref="Load"/> once at startup, <see cref="Reload"/>
/// to read it again while running, and <see cref="Save"/> whenever a section changes. The section properties are safe to read from any thread once
/// <see cref="Load"/> has returned.
/// </summary>
/// <remarks>
/// No method throws and none shows a dialog: configuration is a convenience, not a
/// precondition for running. A missing file is written out with the defaults; an unreadable
/// one is logged, and the defaults are used for the session at startup, or the settings
/// already in memory are kept on a later read. A value that cannot be read (a
/// hand-edited typo, an unknown enum name, markers that cannot delimit a note, a document
/// language PlanCake does not offer) falls back to its default alone, keeping the other settings.
/// </remarks>
internal static class Config {
    static Config() {
        // Settings hold note markers, which may contain the INI comment characters (# and ;) or
        // look like SharpConfig's arrays ({a,b}): a value is always the whole rest of its line.
        Configuration.IgnoreInlineComments = true;
        Configuration.SupressArrayParsing = true;
    }

    /// <summary>
    /// True when <paramref name="value"/> can be stored as a setting and read back unchanged.
    /// SharpConfig strips double quotes around a value, so a value cannot start or end with one.
    /// </summary>
    public static bool CanStore(string value) {
        ArgumentNullException.ThrowIfNull(value);

        return !value.StartsWith('"') && !value.EndsWith('"');
    }

    /// <summary>
    /// Test hook: redirects the config file away from <c>%APPDATA%</c> so a test run does not
    /// clobber the developer's real settings. Set it before calling <see cref="Load"/>.
    /// </summary>
    internal static string? OverrideFilePath { get; set; }

    private static string FilePath => OverrideFilePath ?? App.ConfigPath;

    public static SectionGeneral General { get; private set; } = new();

    public static SectionNotes Notes { get; private set; } = new();

    public static SectionAdvanced Advanced { get; private set; } = new();

    /// <summary>
    /// The last-write time of the file when this process last read or wrote it, so that
    /// <see cref="ReloadIfChanged"/> can tell another window's save.
    /// </summary>
    private static DateTime _knownStamp;

    #region Config section classes

    /// <summary>App-level settings: languages, confirmations, the window, updates.</summary>
    public class SectionGeneral {
        /// <summary>
        /// The interface language: a culture name such as <c>"fr"</c>, or
        /// <see cref="App.SystemLanguageName"/> for the Windows language.
        /// </summary>
        public string Language { get; set; } = App.SystemLanguageName;

        /// <summary>
        /// The <c>lang</c> a newly opened document is read in (one of
        /// <see cref="LanguageList.SupportedCodes"/>). Never follows the interface language.
        /// </summary>
        public string DefaultDocumentLanguage { get; set; } = LanguageList.English;

        /// <summary>Ask before a note is deleted. Delete all notes always asks.</summary>
        public bool ConfirmNoteDelete { get; set; } = true;

        /// <summary>Ask before a task-list check box rewrites the file on disk.</summary>
        public bool ConfirmTaskToggle { get; set; } = true;

        /// <summary>What happens when the open file is changed outside PlanCake.</summary>
        public ExternalChangeAction ExternalChangeAction { get; set; } = ExternalChangeAction.AutoReload;

        /// <summary>Whether the notes list beside the document is shown when a window opens.</summary>
        public bool ShowNotesList { get; set; } = true;

        /// <summary>Check for updates once when PlanCake starts, silently unless there is one.</summary>
        public bool CheckForUpdatesOnStartup { get; set; } = true;

        /// <summary>
        /// How often to check for updates in the background while PlanCake runs;
        /// <see cref="UpdateCheckInterval.Never"/> turns it off. Independent of
        /// <see cref="CheckForUpdatesOnStartup"/>.
        /// </summary>
        public UpdateCheckInterval UpdateCheckInterval { get; set; } = UpdateCheckInterval.Weekly;
    }

    /// <summary>How notes are written and how the keyboard adds them.</summary>
    public class SectionNotes {
        /// <summary>The marker a note starts with.</summary>
        public string OpeningMarker { get; set; } = NoteMarkers.Default.Opening;

        /// <summary>The marker a note ends with; empty for single-token notes that end with their line.</summary>
        public string ClosingMarker { get; set; } = NoteMarkers.Default.Closing;

        /// <summary>What Enter on a block of the document does.</summary>
        public BlockEnterAction BlockEnterAction { get; set; } = BlockEnterAction.AddNote;

        /// <summary>What Enter does in the note dialog; Ctrl+Enter does the other.</summary>
        public NoteEnterAction NoteEnterAction { get; set; } = NoteEnterAction.Save;

        /// <summary>The configured markers as the note parser and store take them.</summary>
        public NoteMarkers ToMarkers() => new(OpeningMarker, ClosingMarker);
    }

    /// <summary>Settings most people never need.</summary>
    public class SectionAdvanced {
        /// <summary>
        /// Rewrite a file that is not UTF-8 as UTF-8 without BOM when it is opened, instead of
        /// opening it read-only.
        /// </summary>
        public bool ConvertToUtf8 { get; set; }
    }

    #endregion

    /// <summary>
    /// Startup: reads the configuration file, or creates it with the defaults if it does not
    /// exist yet. A file that cannot be read leaves the defaults for the session.
    /// </summary>
    public static void Load() {
        if (!TryRead()) {
            // Malformed INI, a locked file, a permissions problem. Carry on with the defaults
            // rather than refusing to start.
            ResetToDefaults();
        }
    }

    /// <summary>
    /// A running window reads the file again (another window may have saved since). A file that
    /// cannot be read keeps the settings already in memory: falling back to the defaults here
    /// would switch the note markers of a window at work, and a save would then write the
    /// defaults over the user's settings.
    /// </summary>
    /// <returns><c>true</c> when the file was read (or, missing, written out with the defaults).</returns>
    public static bool Reload() => TryRead();

    /// <summary>
    /// Reads the file into the sections, or writes the defaults out when it does not exist. The
    /// sections, and the stamp <see cref="ReloadIfChanged"/> compares with, change only when the
    /// read succeeds, so a failed read is tried again at the next check.
    /// </summary>
    private static bool TryRead() {
        // Taken before reading: a save by another window after it makes the next check read again.
        var stamp = FileStamp();

        try {
            var cfg = LoadWithRetries();
            var general = ReadSection<SectionGeneral>(cfg, nameof(General));
            var notes = ReadSection<SectionNotes>(cfg, nameof(Notes));
            var advanced = ReadSection<SectionAdvanced>(cfg, nameof(Advanced));
            Normalize(general, notes);

            General = general;
            Notes = notes;
            Advanced = advanced;
            _knownStamp = stamp;

            return true;
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) {
            // First run, or the file was deleted. Not an error — write the defaults out so the
            // user has a file to edit.
            ResetToDefaults();
            _knownStamp = stamp;
            Save();

            return true;
        } catch (Exception ex) {
            Log.Warning(ex, "Config: unable to read {Path}", FilePath);

            return false;
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

            var cfg = new Configuration {
                Section.FromObject(nameof(General), General),
                Section.FromObject(nameof(Notes), Notes),
                Section.FromObject(nameof(Advanced), Advanced),
            };

            // Every window is a process of its own and reads the file again (Settings, View →
            // Interface language): written in place, a half-written file could be read and its
            // defaults then saved over the user's settings. A move within the folder is atomic.
            var temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";

            try {
                cfg.SaveToFile(temp);
                File.Move(temp, FilePath, overwrite: true);
                _knownStamp = FileStamp();
            } finally {
                if (File.Exists(temp)) {
                    File.Delete(temp);
                }
            }

            return true;
        } catch (Exception ex) {
            Log.Error(ex, "Config: unable to save {Path}", FilePath);

            return false;
        }
    }

    /// <summary>
    /// Reads the file again when it changed since this process last read or wrote it: every
    /// window is a process of its own, and another one may have saved its settings since.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the file was read again; <c>false</c> when it did not change, or could not
    /// be read (the settings in memory are kept, and the next call tries again).
    /// </returns>
    public static bool ReloadIfChanged() => FileStamp() != _knownStamp && Reload();

    /// <summary>The file's last-write time, or <see cref="DateTime.MinValue"/> when it cannot be read.</summary>
    private static DateTime FileStamp() {
        try {
            return File.GetLastWriteTimeUtc(FilePath);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                         or NotSupportedException) {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// View → Interface language: reads the file again (another window may have saved other
    /// settings since this one read it), sets the interface language, and saves. When the file
    /// cannot be read, the language is set for this session only and the file is left alone:
    /// saving the settings in memory could overwrite what it holds.
    /// </summary>
    /// <returns><c>true</c> when the file was written.</returns>
    public static bool SaveLanguage(string language) {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var read = Reload();
        General.Language = language;

        return read && Save();
    }

    /// <summary>How many times a file another window is replacing is read again before giving up.</summary>
    private const int LoadRetries = 3;

    /// <summary>
    /// Reads the file, trying again a little later while another window is replacing it; a
    /// missing file is not retried.
    /// </summary>
    private static Configuration LoadWithRetries() {
        for (var attempt = 0; ; attempt++) {
            try {
                return Configuration.LoadFromFile(FilePath);
            } catch (IOException ex) when (ex is not (FileNotFoundException or DirectoryNotFoundException)
                                           && attempt < LoadRetries) {
                Thread.Sleep(50);
            }
        }
    }

    private static void ResetToDefaults() {
        General = new SectionGeneral();
        Notes = new SectionNotes();
        Advanced = new SectionAdvanced();
    }

    /// <summary>
    /// A section's settings, each read on its own: one that is missing or cannot be read keeps
    /// its default, so a single bad value does not cost the user every other setting.
    /// </summary>
    private static T ReadSection<T>(Configuration cfg, string name) where T : new() {
        var section = new T();

        if (!cfg.Contains(name)) {
            return section;
        }

        var stored = cfg[name];

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (!property.CanWrite || !stored.Contains(property.Name)) {
                continue;
            }

            try {
                var value = stored[property.Name].GetValue(property.PropertyType);

                if (property.PropertyType.IsEnum && (value is null || !Enum.IsDefined(property.PropertyType, value))) {
                    throw new FormatException($"{stored[property.Name].RawValue} is not a {property.PropertyType.Name}.");
                }

                property.SetValue(section, value ?? property.GetValue(section));
            } catch (Exception ex) {
                Log.Warning(
                    ex, "Config: [{Section}] {Setting} = {Value} cannot be read; using the default",
                    name, property.Name, stored[property.Name].RawValue
                );
            }
        }

        return section;
    }

    /// <summary>Replaces values that were read but cannot be used with their defaults.</summary>
    private static void Normalize(SectionGeneral general, SectionNotes notes) {
        if (String.Equals(general.Language?.Trim(), App.SystemLanguageName, StringComparison.OrdinalIgnoreCase)) {
            general.Language = App.SystemLanguageName;
        } else if (!LanguageList.IsCulture(general.Language)) {
            Log.Warning("Config: interface language {Language} is unknown; using the default", general.Language);
            general.Language = App.SystemLanguageName;
        }

        var documentLanguage = LanguageList.SupportedCodes.FirstOrDefault(code =>
            String.Equals(code, general.DefaultDocumentLanguage?.Trim(), StringComparison.OrdinalIgnoreCase)
        );

        if (documentLanguage is null) {
            Log.Warning(
                "Config: document language {Language} is not offered; using the default",
                general.DefaultDocumentLanguage
            );
        }

        general.DefaultDocumentLanguage = documentLanguage ?? LanguageList.English;

        notes.OpeningMarker ??= String.Empty;
        notes.ClosingMarker ??= String.Empty;

        if (notes.ToMarkers().Validate() is var error and not NoteMarkersError.None) {
            Log.Warning(
                "Config: note markers {Opening} … {Closing} are unusable ({Error}); using the defaults",
                notes.OpeningMarker, notes.ClosingMarker, error
            );
            notes.OpeningMarker = NoteMarkers.Default.Opening;
            notes.ClosingMarker = NoteMarkers.Default.Closing;
        }
    }
}
