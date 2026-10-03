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
        excerptLabel = new WrappingLabel();
        noteLabel = new Label();
        noteTextBox = new TextBox();
        okButton = new Button();
        cancelButton = new Button();
        buttonLayout = DialogButtons.CreateRow(okButton, cancelButton);
        mainLayout.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 2;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(noteOnLabel, 0, 0);
        mainLayout.Controls.Add(excerptLabel, 1, 0);
        mainLayout.Controls.Add(noteLabel, 0, 1);
        mainLayout.SetColumnSpan(noteLabel, 2);
        mainLayout.Controls.Add(noteTextBox, 0, 2);
        mainLayout.SetColumnSpan(noteTextBox, 2);
        mainLayout.Controls.Add(buttonLayout, 0, 3);
        mainLayout.SetColumnSpan(buttonLayout, 2);
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
        // buttonLayout: OK, then Cancel, together at the end of the row (DialogButtons).
        //
        buttonLayout.TabIndex = 4;
        //
        // okButton
        //
        okButton.DialogResult = DialogResult.OK;
        okButton.Name = "okButton";
        okButton.Text = "&OK";
        //
        // cancelButton
        //
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
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
    private WrappingLabel excerptLabel;
    private Label noteLabel;
    private TextBox noteTextBox;
    private Button okButton;
    private Button cancelButton;
    private TableLayoutPanel buttonLayout;
}
