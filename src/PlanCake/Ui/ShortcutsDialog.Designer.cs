namespace Oire.PlanCake.Ui;

partial class ShortcutsDialog {
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
        shortcutsLabel = new Label();
        shortcutsList = new Oire.WinForms.NativeControls.NativeListView();
        closeButton = new Button();
        mainLayout.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(shortcutsLabel, 0, 0);
        mainLayout.Controls.Add(shortcutsList, 0, 1);
        mainLayout.Controls.Add(closeButton, 0, 2);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Name = "mainLayout";
        mainLayout.Padding = new Padding(12);
        mainLayout.RowCount = 3;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // shortcutsLabel
        //
        shortcutsLabel.AutoSize = true;
        shortcutsLabel.Margin = new Padding(3, 3, 3, 3);
        shortcutsLabel.Name = "shortcutsLabel";
        shortcutsLabel.TabIndex = 0;
        shortcutsLabel.Text = "Keyboard shortcuts";
        shortcutsLabel.UseMnemonic = false;
        //
        // shortcutsList
        //
        shortcutsList.Dock = DockStyle.Fill;
        shortcutsList.Name = "shortcutsList";
        shortcutsList.TabIndex = 1;
        //
        // closeButton
        //
        closeButton.Anchor = AnchorStyles.Right;
        closeButton.AutoSize = true;
        closeButton.DialogResult = DialogResult.Cancel;
        closeButton.MinimumSize = new Size(80, 0);
        closeButton.Name = "closeButton";
        closeButton.TabIndex = 2;
        closeButton.Text = "Close";
        //
        // ShortcutsDialog
        //
        AccessibleRole = AccessibleRole.Dialog;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = closeButton;
        ClientSize = new Size(640, 480);
        Controls.Add(mainLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(400, 300);
        Name = "ShortcutsDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private Label shortcutsLabel;
    private Oire.WinForms.NativeControls.NativeListView shortcutsList;
    private Button closeButton;
}
