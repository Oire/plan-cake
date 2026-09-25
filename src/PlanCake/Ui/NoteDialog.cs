using GetText.WindowsForms;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

/// <summary>What the note dialog is for.</summary>
internal enum NoteDialogMode {
    Add,
    Edit,
}

/// <summary>What a key pressed in the note text box does.</summary>
internal enum NoteKeyAction {
    /// <summary>The text box handles the key as usual.</summary>
    None,

    /// <summary>The note is saved (the dialog closes with OK).</summary>
    Save,

    /// <summary>A line break is inserted.</summary>
    NewLine,
}

/// <summary>
/// Asks for the text of a note to add or edit, showing which block it goes on. The text is
/// checked before the dialog closes, so a text that cannot be written keeps the dialog open.
/// </summary>
internal partial class NoteDialog: Form {
    private readonly Func<string, string?> _validate;
    private readonly NoteEnterAction _enterAction;

    /// <param name="mode">Adding a note or editing one: sets the title.</param>
    /// <param name="excerpt">The start of the block the note is on, shown after "Note on:".</param>
    /// <param name="text">The text the box starts with: the note being edited, or a kept draft.</param>
    /// <param name="validate">Why a text cannot be written, or <see langword="null"/> when it can.</param>
    /// <param name="enterAction">What Enter does in the text box; Ctrl+Enter does the other.</param>
    public NoteDialog(
        NoteDialogMode mode,
        string excerpt,
        string text,
        Func<string, string?> validate,
        NoteEnterAction enterAction
    ) {
        ArgumentNullException.ThrowIfNull(excerpt);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(validate);

        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        Text = mode == NoteDialogMode.Edit ? _("Edit note") : _("Add note");

        _validate = validate;
        _enterAction = enterAction;

        excerptLabel.Text = excerpt;
        noteTextBox.Text = text;
        noteTextBox.SelectionStart = noteTextBox.TextLength;
        noteTextBox.TextChanged += OnNoteTextChanged;
        UpdateOkButton();
        ActiveControl = noteTextBox;
    }

    /// <summary>The text the user typed, as typed.</summary>
    public string NoteText => noteTextBox.Text;

    /// <summary>What <paramref name="keyData"/> does in the note text box with <paramref name="enterAction"/>.</summary>
    internal static NoteKeyAction KeyAction(Keys keyData, NoteEnterAction enterAction) => keyData switch {
        Keys.Enter => enterAction == NoteEnterAction.Save ? NoteKeyAction.Save : NoteKeyAction.NewLine,
        Keys.Control | Keys.Enter => enterAction == NoteEnterAction.Save ? NoteKeyAction.NewLine : NoteKeyAction.Save,
        _ => NoteKeyAction.None,
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        if (noteTextBox.Focused) {
            switch (KeyAction(keyData, _enterAction)) {
                case NoteKeyAction.Save:
                    if (okButton.Enabled) {
                        okButton.PerformClick();
                    }

                    return true;
                case NoteKeyAction.NewLine:
                    noteTextBox.SelectedText = Environment.NewLine;
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        base.OnFormClosing(e);

        if (DialogResult != DialogResult.OK) {
            return;
        }

        if (_validate(noteTextBox.Text) is { } error) {
            Log.Debug("Note text rejected: {Error}", error);
            DialogHelper.Show(error, _("The note cannot be saved"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            noteTextBox.Focus();
        }
    }

    private void OnNoteTextChanged(object? sender, EventArgs e) => UpdateOkButton();

    private void UpdateOkButton() => okButton.Enabled = !String.IsNullOrWhiteSpace(noteTextBox.Text);

    protected override void OnFormClosed(FormClosedEventArgs e) {
        noteTextBox.TextChanged -= OnNoteTextChanged;
        base.OnFormClosed(e);
    }
}
