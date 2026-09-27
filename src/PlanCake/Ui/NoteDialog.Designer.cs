namespace Oire.PlanCake.Ui;

partial class NoteDialog {
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
        noteOnLabel = new Label();
        excerptLabel = new Label();
        noteLabel = new Label();
        noteTextBox = new TextBox();
        okButton = new Button();
        cancelButton = new Button();
        mainLayout.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 3;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainLayout.Controls.Add(noteOnLabel, 0, 0);
        mainLayout.Controls.Add(excerptLabel, 1, 0);
        mainLayout.SetColumnSpan(excerptLabel, 2);
        mainLayout.Controls.Add(noteLabel, 0, 1);
        mainLayout.SetColumnSpan(noteLabel, 3);
        mainLayout.Controls.Add(noteTextBox, 0, 2);
        mainLayout.SetColumnSpan(noteTextBox, 3);
        mainLayout.Controls.Add(okButton, 0, 3);
        mainLayout.Controls.Add(cancelButton, 2, 3);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Name = "mainLayout";
        mainLayout.Padding = new Padding(12);
        mainLayout.RowCount = 4;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // noteOnLabel
        //
        noteOnLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        noteOnLabel.AutoSize = true;
        noteOnLabel.Name = "noteOnLabel";
        noteOnLabel.TabIndex = 0;
        noteOnLabel.Text = "Note on:";
        //
        // excerptLabel
        //
        excerptLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        excerptLabel.AutoSize = true;
        excerptLabel.Name = "excerptLabel";
        excerptLabel.TabIndex = 1;
        excerptLabel.UseMnemonic = false;
        //
        // noteLabel
        //
        noteLabel.Anchor = AnchorStyles.Left;
        noteLabel.AutoSize = true;
        noteLabel.Name = "noteLabel";
        noteLabel.TabIndex = 2;
        noteLabel.Text = "&Note:";
        //
        // noteTextBox
        //
        noteTextBox.AcceptsReturn = true;
        noteTextBox.Dock = DockStyle.Fill;
        noteTextBox.Multiline = true;
        noteTextBox.Name = "noteTextBox";
        noteTextBox.ScrollBars = ScrollBars.Vertical;
        noteTextBox.TabIndex = 3;
        noteTextBox.WordWrap = true;
        //
        // okButton
        //
        okButton.Anchor = AnchorStyles.Left;
        okButton.AutoSize = true;
        okButton.DialogResult = DialogResult.OK;
        okButton.Name = "okButton";
        okButton.TabIndex = 4;
        okButton.Text = "&OK";
        //
        // cancelButton
        //
        cancelButton.Anchor = AnchorStyles.Right;
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.TabIndex = 5;
        cancelButton.Text = "&Cancel";
        //
        // NoteDialog
        //
        AcceptButton = okButton;
        AccessibleRole = AccessibleRole.Dialog;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(520, 260);
        Controls.Add(mainLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(360, 220);
        Name = "NoteDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Add note";
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private Label noteOnLabel;
    private Label excerptLabel;
    private Label noteLabel;
    private TextBox noteTextBox;
    private Button okButton;
    private Button cancelButton;
}
