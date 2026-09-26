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
internal partial class SettingsDialog: Form {
    /// <summary>The interface languages offered, the system default first.</summary>
    private IReadOnlyList<LanguageOption> _interfaceLanguages = [];

    public SettingsDialog() {
        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        Text = _("Settings");

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

        return error switch {
            NoteMarkersError.None => null,
            NoteMarkersError.EmptyOpening => _("The opening marker cannot be empty."),
            NoteMarkersError.SurroundingWhitespace => _("A marker cannot start or end with a space."),
            NoteMarkersError.LineBreak => _("A marker cannot contain a line break."),
            NoteMarkersError.ClosingSameAsOpening => _("The closing marker must differ from the opening marker."),
            _ => throw new ArgumentOutOfRangeException(nameof(opening), error, null),
        };
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

        var ctrlEnter = HostCommands.KeyText(Keys.Control | Keys.Enter);
        noteEnterComboBox.Items.AddRange([
            new Choice<NoteEnterAction>(NoteEnterAction.Save, _("Saves the note ({0} starts a new line)", ctrlEnter)),
            new Choice<NoteEnterAction>(NoteEnterAction.NewLine, _("Starts a new line ({0} saves the note)", ctrlEnter)),
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

    private void OnMarkerTextChanged(object? sender, EventArgs e) => ShowMarkersError();

    /// <summary>
    /// Shows why the markers cannot be used next to them, and gives it to both boxes as their
    /// description, so a screen reader says it with the box's label.
    /// </summary>
    private string? ShowMarkersError() {
        var error = DescribeMarkersError(openingMarkerTextBox.Text, closingMarkerTextBox.Text);
        markersErrorLabel.Text = error ?? String.Empty;
        openingMarkerTextBox.AccessibleDescription = error;
        closingMarkerTextBox.AccessibleDescription = error;

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
        base.OnFormClosed(e);
    }

    /// <summary>An item of a combo box: a setting's value and the text shown for it.</summary>
    private sealed record Choice<T>(T Value, string Text) {
        public override string ToString() => Text;
    }
}
