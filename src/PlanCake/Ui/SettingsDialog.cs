using GetText.WindowsForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

/// <summary>
/// File → Settings: every setting of Technical details → "Settings" in the plan, on three tabs
/// (General, Notes, Advanced). OK writes them to <see cref="Config"/> and saves the file; the
/// window then applies them without a restart. Cancel and Escape leave <see cref="Config"/> as it was.
/// </summary>
internal sealed partial class SettingsDialog: Form {
    /// <summary>The interface languages offered, the system default first.</summary>
    private IReadOnlyList<LanguageOption> _interfaceLanguages = [];

    /// <summary>
    /// The red of the markers error: 4.5:1 or more against the dialog and its tabs. Not used in
    /// high contrast.
    /// </summary>
    internal static readonly Color ErrorColor = Color.FromArgb(0xC4, 0x2B, 0x1C);

    /// <summary>Says a new reason the markers cannot be used once the typing pauses.</summary>
    private readonly System.Windows.Forms.Timer _markersErrorTimer;

    /// <summary>The reason last spoken, so the same one is not said again on every key press.</summary>
    private string? _spokenMarkersError;

    public SettingsDialog() {
        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        TextDirection.KeepLeftToRight(openingMarkerTextBox, closingMarkerTextBox);
        Text = _("Settings");

        components ??= new System.ComponentModel.Container();
        _markersErrorTimer = new System.Windows.Forms.Timer(components) { Interval = 700 };
        _markersErrorTimer.Tick += OnMarkersErrorTimerTick;

        // An error stands out by its "Error:" prefix first; the red is extra, and high contrast
        // keeps the theme's own text color.
        if (!SystemInformation.HighContrast) {
            markersErrorLabel.ForeColor = ErrorColor;
        }

        FillChoices();
        LoadSettings();

        openingMarkerTextBox.TextChanged += OnMarkerTextChanged;
        closingMarkerTextBox.TextChanged += OnMarkerTextChanged;
        tabControl.Selected += OnTabSelected;
        ShowMarkersError();

        // The first field of the first tab, not the tab strip.
        ActiveControl = languageComboBox;
    }

    /// <summary>True when OK was pressed but the settings file could not be written.</summary>
    public bool SaveFailed { get; private set; }

    /// <summary>
    /// Why the markers cannot delimit a note (or be stored in the settings file), in the
    /// interface language; <see langword="null"/> when they can.
    /// </summary>
    internal static string? DescribeMarkersError(string opening, string closing) {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(closing);

        var error = new NoteMarkers(opening, closing).Validate();

        if (error == NoteMarkersError.None && !(Config.CanStore(opening) && Config.CanStore(closing))) {
            return _("A marker cannot start or end with a double quote.");
        }

        return LocalizedText.MarkersError(error);
    }

    private void FillChoices() {
        _interfaceLanguages = LanguageList.InterfaceLanguages(_("System default"));
        languageComboBox.Items.AddRange([.. _interfaceLanguages.Select(option => new Choice<string>(option.Code, option.Name))]);
        documentLanguageComboBox.Items.AddRange([
            .. LanguageList.DocumentLanguages().Select(option => new Choice<string>(option.Code, option.Name)),
        ]);

        externalChangeComboBox.Items.AddRange([
            new Choice<ExternalChangeAction>(ExternalChangeAction.AutoReload, _("Reload it")),
            new Choice<ExternalChangeAction>(ExternalChangeAction.Ask, _("Ask before reloading it")),
        ]);

        blockEnterComboBox.Items.AddRange([
            new Choice<BlockEnterAction>(BlockEnterAction.AddNote, _("Adds a note")),
            new Choice<BlockEnterAction>(BlockEnterAction.ContextMenu, _("Opens the context menu")),
        ]);

        // The items stay short enough to show whole in the closed box; the label says Ctrl+Enter.
        var ctrlEnter = HostCommands.KeyText(Keys.Control | Keys.Enter);
        noteEnterLabel.Text = _("Enter in the note dialo&g ({0} does the other):", ctrlEnter);
        noteEnterComboBox.Items.AddRange([
            new Choice<NoteEnterAction>(NoteEnterAction.Save, _("Saves the note")),
            new Choice<NoteEnterAction>(NoteEnterAction.NewLine, _("Starts a new line")),
        ]);

        updateIntervalComboBox.Items.AddRange([
            .. UpdateIntervalOptions().Select(option => new Choice<UpdateCheckInterval>(option.Interval, option.Text)),
        ]);
    }

    /// <summary>
    /// The background update check intervals the dialog offers, in the order of
    /// <see cref="UpdateCheckInterval"/> (most frequent first, never last), in the interface language.
    /// </summary>
    internal static IReadOnlyList<(UpdateCheckInterval Interval, string Text)> UpdateIntervalOptions() => [
        (UpdateCheckInterval.Daily, _("Once a day")),
        (UpdateCheckInterval.EveryThreeDays, _("Every 3 days")),
        (UpdateCheckInterval.Weekly, _("Once a week")),
        (UpdateCheckInterval.Monthly, _("Once a month")),
        (UpdateCheckInterval.Never, _("Never")),
    ];

    private void LoadSettings() {
        var general = Config.General;
        var notes = Config.Notes;

        Select(languageComboBox, LanguageList.Find(_interfaceLanguages, general.Language).Code);
        Select(documentLanguageComboBox, general.DefaultDocumentLanguage);
        Select(externalChangeComboBox, general.ExternalChangeAction);
        confirmNoteDeleteCheckBox.Checked = general.ConfirmNoteDelete;
        confirmTaskToggleCheckBox.Checked = general.ConfirmTaskToggle;
        showNotesListCheckBox.Checked = general.ShowNotesList;
        checkUpdatesOnStartupCheckBox.Checked = general.CheckForUpdatesOnStartup;
        Select(updateIntervalComboBox, general.UpdateCheckInterval);

        openingMarkerTextBox.Text = notes.OpeningMarker;
        closingMarkerTextBox.Text = notes.ClosingMarker;
        Select(blockEnterComboBox, notes.BlockEnterAction);
        Select(noteEnterComboBox, notes.NoteEnterAction);

        convertToUtf8CheckBox.Checked = Config.Advanced.ConvertToUtf8;
    }

    /// <summary>Selects the item holding <paramref name="value"/>, else the first one.</summary>
    private static void Select<T>(ComboBox comboBox, T value) {
        var index = comboBox.Items.Cast<Choice<T>>().ToList()
            .FindIndex(choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
        comboBox.SelectedIndex = Math.Max(index, 0);
    }

    private static T Selected<T>(ComboBox comboBox) => ((Choice<T>)comboBox.SelectedItem!).Value;

    /// <summary>What the label under the markers says for <paramref name="error"/>: empty when there is none.</summary>
    internal static string MarkersErrorText(string? error) => error is null ? String.Empty : _("Error: {0}", error);

    private void OnMarkerTextChanged(object? sender, EventArgs e) {
        ShowMarkersError();
        _markersErrorTimer.Stop();
        _markersErrorTimer.Start();
    }

    /// <summary>
    /// Says the reason the markers cannot be used when it is new, once the typing pauses: the label
    /// is silent, and JAWS reads a box's description in neither of its modes (docs/jaws-spike.md).
    /// </summary>
    private void OnMarkersErrorTimerTick(object? sender, EventArgs e) {
        _markersErrorTimer.Stop();
        var error = DescribeMarkersError(openingMarkerTextBox.Text, closingMarkerTextBox.Text);

        if (error is not null && error != _spokenMarkersError) {
            StatusAnnouncer.Speak(ActiveControl ?? this, MarkersErrorText(error));
        }

        _spokenMarkersError = error;
    }

    /// <summary>Shows why the markers cannot be used in the label under them, and returns it.</summary>
    private string? ShowMarkersError() {
        var error = DescribeMarkersError(openingMarkerTextBox.Text, closingMarkerTextBox.Text);
        markersErrorLabel.Text = MarkersErrorText(error);

        return error;
    }

    /// <summary>
    /// Ctrl+Tab and Ctrl+Shift+Tab land on the first field of the tab they switch to, not on the
    /// tab strip (as in SIC). The arrow keys on the strip keep moving between tabs. Deferred:
    /// the tab control takes the focus itself after this event.
    /// </summary>
    private void OnTabSelected(object? sender, TabControlEventArgs e) {
        if (e.TabPage is not { } page || (ModifierKeys & Keys.Control) == 0) {
            return;
        }

        BeginInvoke(() => page.SelectNextControl(null, forward: true, tabStopOnly: true, nested: true, wrap: true));
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        base.OnFormClosing(e);

        if (DialogResult != DialogResult.OK) {
            return;
        }

        if (ShowMarkersError() is { } error) {
            Log.Debug("Settings: markers rejected: {Error}", error);
            DialogHelper.Show(error, _("The markers cannot be used"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            tabControl.SelectedTab = notesTab;
            openingMarkerTextBox.Focus();

            return;
        }

        SaveSettings();
    }

    private void SaveSettings() {
        var general = Config.General;
        var notes = Config.Notes;

        general.Language = Selected<string>(languageComboBox);
        general.DefaultDocumentLanguage = Selected<string>(documentLanguageComboBox);
        general.ExternalChangeAction = Selected<ExternalChangeAction>(externalChangeComboBox);
        general.ConfirmNoteDelete = confirmNoteDeleteCheckBox.Checked;
        general.ConfirmTaskToggle = confirmTaskToggleCheckBox.Checked;
        general.ShowNotesList = showNotesListCheckBox.Checked;
        general.CheckForUpdatesOnStartup = checkUpdatesOnStartupCheckBox.Checked;
        general.UpdateCheckInterval = Selected<UpdateCheckInterval>(updateIntervalComboBox);

        notes.OpeningMarker = openingMarkerTextBox.Text;
        notes.ClosingMarker = closingMarkerTextBox.Text;
        notes.BlockEnterAction = Selected<BlockEnterAction>(blockEnterComboBox);
        notes.NoteEnterAction = Selected<NoteEnterAction>(noteEnterComboBox);

        Config.Advanced.ConvertToUtf8 = convertToUtf8CheckBox.Checked;

        SaveFailed = !Config.Save();
        Log.Information("Settings saved: failed={Failed}", SaveFailed);
    }

    protected override void OnFormClosed(FormClosedEventArgs e) {
        openingMarkerTextBox.TextChanged -= OnMarkerTextChanged;
        closingMarkerTextBox.TextChanged -= OnMarkerTextChanged;
        tabControl.Selected -= OnTabSelected;
        _markersErrorTimer.Stop();
        _markersErrorTimer.Tick -= OnMarkersErrorTimerTick;
        base.OnFormClosed(e);
    }

    /// <summary>An item of a combo box: a setting's value and the text shown for it.</summary>
    private sealed record Choice<T>(T Value, string Text) {
        public override string ToString() => Text;
    }
}
