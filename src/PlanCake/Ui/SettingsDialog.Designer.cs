namespace Oire.PlanCake.Ui;

partial class SettingsDialog {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent() {
        mainLayout = new TableLayoutPanel();
        tabControl = new TabControl();

        generalTab = new TabPage();
        generalLayout = new TableLayoutPanel();
        languageLabel = new Label();
        languageComboBox = new ComboBox();
        documentLanguageLabel = new Label();
        documentLanguageComboBox = new ComboBox();
        externalChangeLabel = new Label();
        externalChangeComboBox = new ComboBox();
        confirmNoteDeleteCheckBox = new CheckBox();
        confirmTaskToggleCheckBox = new CheckBox();
        showNotesListCheckBox = new CheckBox();
        checkUpdatesOnStartupCheckBox = new CheckBox();
        updateIntervalLabel = new Label();
        updateIntervalComboBox = new ComboBox();

        notesTab = new TabPage();
        notesLayout = new TableLayoutPanel();
        openingMarkerLabel = new Label();
        openingMarkerTextBox = new TextBox();
        closingMarkerLabel = new Label();
        closingMarkerTextBox = new TextBox();
        markersErrorLabel = new Label();
        blockEnterLabel = new Label();
        blockEnterComboBox = new ComboBox();
        noteEnterLabel = new Label();
        noteEnterComboBox = new ComboBox();

        advancedTab = new TabPage();
        advancedLayout = new TableLayoutPanel();
        convertToUtf8CheckBox = new CheckBox();

        okButton = new Button();
        cancelButton = new Button();

        mainLayout.SuspendLayout();
        tabControl.SuspendLayout();
        generalTab.SuspendLayout();
        generalLayout.SuspendLayout();
        notesTab.SuspendLayout();
        notesLayout.SuspendLayout();
        advancedTab.SuspendLayout();
        advancedLayout.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout: the tabs across both columns, then OK and Cancel.
        //
        mainLayout.ColumnCount = 2;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        mainLayout.RowCount = 2;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.Controls.Add(tabControl, 0, 0);
        mainLayout.SetColumnSpan(tabControl, 2);
        mainLayout.Controls.Add(okButton, 0, 1);
        mainLayout.Controls.Add(cancelButton, 1, 1);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Name = "mainLayout";
        mainLayout.Padding = new Padding(12);
        mainLayout.TabIndex = 0;
        //
        // tabControl. A tab's text takes no mnemonic (it would show as "&General"): Ctrl+Tab and
        // Ctrl+Shift+Tab move between the tabs.
        //
        tabControl.Dock = DockStyle.Fill;
        tabControl.Name = "tabControl";
        tabControl.TabPages.AddRange(new TabPage[] { generalTab, notesTab, advancedTab });
        tabControl.TabIndex = 0;
        //
        // generalTab
        //
        generalTab.Controls.Add(generalLayout);
        generalTab.Name = "generalTab";
        generalTab.Padding = new Padding(8);
        generalTab.Text = "General";
        generalTab.UseVisualStyleBackColor = true;
        //
        // generalLayout: a label and its box on a row; the check boxes span both columns.
        //
        generalLayout.ColumnCount = 2;
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        generalLayout.RowCount = 9;
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        generalLayout.Controls.Add(languageLabel, 0, 0);
        generalLayout.Controls.Add(languageComboBox, 1, 0);
        generalLayout.Controls.Add(documentLanguageLabel, 0, 1);
        generalLayout.Controls.Add(documentLanguageComboBox, 1, 1);
        generalLayout.Controls.Add(externalChangeLabel, 0, 2);
        generalLayout.Controls.Add(externalChangeComboBox, 1, 2);
        generalLayout.Controls.Add(confirmNoteDeleteCheckBox, 0, 3);
        generalLayout.SetColumnSpan(confirmNoteDeleteCheckBox, 2);
        generalLayout.Controls.Add(confirmTaskToggleCheckBox, 0, 4);
        generalLayout.SetColumnSpan(confirmTaskToggleCheckBox, 2);
        generalLayout.Controls.Add(showNotesListCheckBox, 0, 5);
        generalLayout.SetColumnSpan(showNotesListCheckBox, 2);
        generalLayout.Controls.Add(checkUpdatesOnStartupCheckBox, 0, 6);
        generalLayout.SetColumnSpan(checkUpdatesOnStartupCheckBox, 2);
        generalLayout.Controls.Add(updateIntervalLabel, 0, 7);
        generalLayout.Controls.Add(updateIntervalComboBox, 1, 7);
        generalLayout.Dock = DockStyle.Fill;
        generalLayout.Name = "generalLayout";
        generalLayout.TabIndex = 0;
        //
        // languageLabel
        //
        languageLabel.Anchor = AnchorStyles.Left;
        languageLabel.AutoSize = true;
        languageLabel.Margin = new Padding(3, 6, 8, 3);
        languageLabel.Name = "languageLabel";
        languageLabel.TabIndex = 0;
        languageLabel.Text = "&Interface language:";
        //
        // languageComboBox
        //
        languageComboBox.Dock = DockStyle.Fill;
        languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        languageComboBox.Name = "languageComboBox";
        languageComboBox.TabIndex = 1;
        //
        // documentLanguageLabel
        //
        documentLanguageLabel.Anchor = AnchorStyles.Left;
        documentLanguageLabel.AutoSize = true;
        documentLanguageLabel.Margin = new Padding(3, 6, 8, 3);
        documentLanguageLabel.Name = "documentLanguageLabel";
        documentLanguageLabel.TabIndex = 2;
        documentLanguageLabel.Text = "&Document language:";
        //
        // documentLanguageComboBox
        //
        documentLanguageComboBox.Dock = DockStyle.Fill;
        documentLanguageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        documentLanguageComboBox.Name = "documentLanguageComboBox";
        documentLanguageComboBox.TabIndex = 3;
        //
        // externalChangeLabel
        //
        externalChangeLabel.Anchor = AnchorStyles.Left;
        externalChangeLabel.AutoSize = true;
        externalChangeLabel.Margin = new Padding(3, 6, 8, 3);
        externalChangeLabel.Name = "externalChangeLabel";
        externalChangeLabel.TabIndex = 4;
        externalChangeLabel.Text = "&When the file changes on disk:";
        //
        // externalChangeComboBox
        //
        externalChangeComboBox.Dock = DockStyle.Fill;
        externalChangeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        externalChangeComboBox.Name = "externalChangeComboBox";
        externalChangeComboBox.TabIndex = 5;
        //
        // confirmNoteDeleteCheckBox
        //
        confirmNoteDeleteCheckBox.Anchor = AnchorStyles.Left;
        confirmNoteDeleteCheckBox.AutoSize = true;
        confirmNoteDeleteCheckBox.Margin = new Padding(3, 8, 3, 3);
        confirmNoteDeleteCheckBox.Name = "confirmNoteDeleteCheckBox";
        confirmNoteDeleteCheckBox.TabIndex = 6;
        confirmNoteDeleteCheckBox.Text = "Ask before deleting a &note";
        //
        // confirmTaskToggleCheckBox
        //
        confirmTaskToggleCheckBox.Anchor = AnchorStyles.Left;
        confirmTaskToggleCheckBox.AutoSize = true;
        confirmTaskToggleCheckBox.Margin = new Padding(3, 8, 3, 3);
        confirmTaskToggleCheckBox.Name = "confirmTaskToggleCheckBox";
        confirmTaskToggleCheckBox.TabIndex = 7;
        confirmTaskToggleCheckBox.Text = "Ask before checking or unchecking a &task";
        //
        // showNotesListCheckBox
        //
        showNotesListCheckBox.Anchor = AnchorStyles.Left;
        showNotesListCheckBox.AutoSize = true;
        showNotesListCheckBox.Margin = new Padding(3, 8, 3, 3);
        showNotesListCheckBox.Name = "showNotesListCheckBox";
        showNotesListCheckBox.TabIndex = 8;
        showNotesListCheckBox.Text = "Show the notes &list";
        //
        // checkUpdatesOnStartupCheckBox
        //
        checkUpdatesOnStartupCheckBox.Anchor = AnchorStyles.Left;
        checkUpdatesOnStartupCheckBox.AutoSize = true;
        checkUpdatesOnStartupCheckBox.Margin = new Padding(3, 8, 3, 3);
        checkUpdatesOnStartupCheckBox.Name = "checkUpdatesOnStartupCheckBox";
        checkUpdatesOnStartupCheckBox.TabIndex = 9;
        checkUpdatesOnStartupCheckBox.Text = "Check for &updates on startup";
        //
        // updateIntervalLabel
        //
        updateIntervalLabel.Anchor = AnchorStyles.Left;
        updateIntervalLabel.AutoSize = true;
        updateIntervalLabel.Margin = new Padding(3, 6, 8, 3);
        updateIntervalLabel.Name = "updateIntervalLabel";
        updateIntervalLabel.TabIndex = 10;
        updateIntervalLabel.Text = "Check for updates in the &background:";
        //
        // updateIntervalComboBox
        //
        updateIntervalComboBox.Dock = DockStyle.Fill;
        updateIntervalComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        updateIntervalComboBox.Name = "updateIntervalComboBox";
        updateIntervalComboBox.TabIndex = 11;
        //
        // notesTab
        //
        notesTab.Controls.Add(notesLayout);
        notesTab.Name = "notesTab";
        notesTab.Padding = new Padding(8);
        notesTab.Text = "Notes";
        notesTab.UseVisualStyleBackColor = true;
        //
        // notesLayout: the markers, the reason they cannot be used (if any), then the Enter keys.
        //
        notesLayout.ColumnCount = 2;
        notesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        notesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        notesLayout.RowCount = 6;
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        notesLayout.Controls.Add(openingMarkerLabel, 0, 0);
        notesLayout.Controls.Add(openingMarkerTextBox, 1, 0);
        notesLayout.Controls.Add(closingMarkerLabel, 0, 1);
        notesLayout.Controls.Add(closingMarkerTextBox, 1, 1);
        notesLayout.Controls.Add(markersErrorLabel, 0, 2);
        notesLayout.SetColumnSpan(markersErrorLabel, 2);
        notesLayout.Controls.Add(blockEnterLabel, 0, 3);
        notesLayout.Controls.Add(blockEnterComboBox, 1, 3);
        notesLayout.Controls.Add(noteEnterLabel, 0, 4);
        notesLayout.Controls.Add(noteEnterComboBox, 1, 4);
        notesLayout.Dock = DockStyle.Fill;
        notesLayout.Name = "notesLayout";
        notesLayout.TabIndex = 0;
        //
        // openingMarkerLabel
        //
        openingMarkerLabel.Anchor = AnchorStyles.Left;
        openingMarkerLabel.AutoSize = true;
        openingMarkerLabel.Margin = new Padding(3, 6, 8, 3);
        openingMarkerLabel.Name = "openingMarkerLabel";
        openingMarkerLabel.TabIndex = 0;
        openingMarkerLabel.Text = "O&pening marker:";
        //
        // openingMarkerTextBox
        //
        openingMarkerTextBox.Dock = DockStyle.Fill;
        openingMarkerTextBox.Name = "openingMarkerTextBox";
        openingMarkerTextBox.TabIndex = 1;
        //
        // closingMarkerLabel
        //
        closingMarkerLabel.Anchor = AnchorStyles.Left;
        closingMarkerLabel.AutoSize = true;
        closingMarkerLabel.Margin = new Padding(3, 6, 8, 3);
        closingMarkerLabel.MaximumSize = new Size(220, 0);
        closingMarkerLabel.Name = "closingMarkerLabel";
        closingMarkerLabel.TabIndex = 2;
        closingMarkerLabel.Text = "Clo&sing marker (leave empty for a single marker that runs to the end of the line):";
        //
        // closingMarkerTextBox
        //
        closingMarkerTextBox.Dock = DockStyle.Fill;
        closingMarkerTextBox.Name = "closingMarkerTextBox";
        closingMarkerTextBox.TabIndex = 3;
        //
        // markersErrorLabel: why the markers cannot be used, next to them; empty while they can.
        //
        markersErrorLabel.Anchor = AnchorStyles.Left;
        markersErrorLabel.AutoSize = true;
        markersErrorLabel.Margin = new Padding(3, 4, 3, 3);
        markersErrorLabel.Name = "markersErrorLabel";
        markersErrorLabel.TabIndex = 4;
        markersErrorLabel.UseMnemonic = false;
        //
        // blockEnterLabel
        //
        blockEnterLabel.Anchor = AnchorStyles.Left;
        blockEnterLabel.AutoSize = true;
        blockEnterLabel.Margin = new Padding(3, 6, 8, 3);
        blockEnterLabel.Name = "blockEnterLabel";
        blockEnterLabel.TabIndex = 5;
        blockEnterLabel.Text = "&Enter or a click on a block:";
        //
        // blockEnterComboBox
        //
        blockEnterComboBox.Dock = DockStyle.Fill;
        blockEnterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        blockEnterComboBox.Name = "blockEnterComboBox";
        blockEnterComboBox.TabIndex = 6;
        //
        // noteEnterLabel
        //
        noteEnterLabel.Anchor = AnchorStyles.Left;
        noteEnterLabel.AutoSize = true;
        noteEnterLabel.Margin = new Padding(3, 6, 8, 3);
        noteEnterLabel.Name = "noteEnterLabel";
        noteEnterLabel.TabIndex = 7;
        noteEnterLabel.Text = "Enter in the note dialo&g:";
        //
        // noteEnterComboBox
        //
        noteEnterComboBox.Dock = DockStyle.Fill;
        noteEnterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        noteEnterComboBox.Name = "noteEnterComboBox";
        noteEnterComboBox.TabIndex = 8;
        //
        // advancedTab
        //
        advancedTab.Controls.Add(advancedLayout);
        advancedTab.Name = "advancedTab";
        advancedTab.Padding = new Padding(8);
        advancedTab.Text = "Advanced";
        advancedTab.UseVisualStyleBackColor = true;
        //
        // advancedLayout
        //
        advancedLayout.ColumnCount = 1;
        advancedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        advancedLayout.RowCount = 2;
        advancedLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        advancedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        advancedLayout.Controls.Add(convertToUtf8CheckBox, 0, 0);
        advancedLayout.Dock = DockStyle.Fill;
        advancedLayout.Name = "advancedLayout";
        advancedLayout.TabIndex = 0;
        //
        // convertToUtf8CheckBox
        //
        convertToUtf8CheckBox.Anchor = AnchorStyles.Left;
        convertToUtf8CheckBox.AutoSize = true;
        convertToUtf8CheckBox.Margin = new Padding(3, 8, 3, 3);
        convertToUtf8CheckBox.MaximumSize = new Size(440, 0);
        convertToUtf8CheckBox.Name = "convertToUtf8CheckBox";
        convertToUtf8CheckBox.TabIndex = 0;
        convertToUtf8CheckBox.Text =
            "Con&vert files that are not UTF-8 to UTF-8 (without BOM) when opening them. The file on disk is rewritten.";
        //
        // okButton
        //
        okButton.Anchor = AnchorStyles.Right;
        okButton.AutoSize = true;
        okButton.DialogResult = DialogResult.OK;
        okButton.Margin = new Padding(0, 8, 6, 0);
        okButton.Name = "okButton";
        okButton.TabIndex = 1;
        okButton.Text = "&OK";
        //
        // cancelButton
        //
        cancelButton.Anchor = AnchorStyles.Left;
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Margin = new Padding(6, 8, 0, 0);
        cancelButton.Name = "cancelButton";
        cancelButton.TabIndex = 2;
        cancelButton.Text = "&Cancel";
        //
        // SettingsDialog
        //
        AcceptButton = okButton;
        AccessibleRole = AccessibleRole.Dialog;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(520, 390);
        Controls.Add(mainLayout);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "SettingsDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Settings";
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        tabControl.ResumeLayout(false);
        generalTab.ResumeLayout(false);
        generalLayout.ResumeLayout(false);
        generalLayout.PerformLayout();
        notesTab.ResumeLayout(false);
        notesLayout.ResumeLayout(false);
        notesLayout.PerformLayout();
        advancedTab.ResumeLayout(false);
        advancedLayout.ResumeLayout(false);
        advancedLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private TabControl tabControl;

    private TabPage generalTab;
    private TableLayoutPanel generalLayout;
    private Label languageLabel;
    private ComboBox languageComboBox;
    private Label documentLanguageLabel;
    private ComboBox documentLanguageComboBox;
    private Label externalChangeLabel;
    private ComboBox externalChangeComboBox;
    private CheckBox confirmNoteDeleteCheckBox;
    private CheckBox confirmTaskToggleCheckBox;
    private CheckBox showNotesListCheckBox;
    private CheckBox checkUpdatesOnStartupCheckBox;
    private Label updateIntervalLabel;
    private ComboBox updateIntervalComboBox;

    private TabPage notesTab;
    private TableLayoutPanel notesLayout;
    private Label openingMarkerLabel;
    private TextBox openingMarkerTextBox;
    private Label closingMarkerLabel;
    private TextBox closingMarkerTextBox;
    private Label markersErrorLabel;
    private Label blockEnterLabel;
    private ComboBox blockEnterComboBox;
    private Label noteEnterLabel;
    private ComboBox noteEnterComboBox;

    private TabPage advancedTab;
    private TableLayoutPanel advancedLayout;
    private CheckBox convertToUtf8CheckBox;

    private Button okButton;
    private Button cancelButton;
}
