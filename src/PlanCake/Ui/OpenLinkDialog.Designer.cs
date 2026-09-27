namespace Oire.PlanCake.Ui;

partial class OpenLinkDialog {
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
        urlLabel = new Label();
        urlTextBox = new TextBox();
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
        mainLayout.Controls.Add(urlLabel, 0, 0);
        mainLayout.Controls.Add(urlTextBox, 1, 0);
        mainLayout.SetColumnSpan(urlTextBox, 2);
        mainLayout.Controls.Add(okButton, 0, 1);
        mainLayout.Controls.Add(cancelButton, 2, 1);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Name = "mainLayout";
        mainLayout.Padding = new Padding(12);
        mainLayout.RowCount = 2;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // urlLabel
        //
        urlLabel.Anchor = AnchorStyles.Left;
        urlLabel.AutoSize = true;
        urlLabel.Name = "urlLabel";
        urlLabel.TabIndex = 0;
        urlLabel.Text = "&Link:";
        //
        // urlTextBox
        //
        urlTextBox.Dock = DockStyle.Fill;
        urlTextBox.Name = "urlTextBox";
        urlTextBox.TabIndex = 1;
        //
        // okButton
        //
        okButton.Anchor = AnchorStyles.Left;
        okButton.AutoSize = true;
        okButton.DialogResult = DialogResult.OK;
        okButton.Name = "okButton";
        okButton.TabIndex = 2;
        okButton.Text = "&OK";
        //
        // cancelButton
        //
        cancelButton.Anchor = AnchorStyles.Right;
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.TabIndex = 3;
        cancelButton.Text = "&Cancel";
        //
        // OpenLinkDialog
        //
        AcceptButton = okButton;
        AccessibleRole = AccessibleRole.Dialog;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(520, 100);
        Controls.Add(mainLayout);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "OpenLinkDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Open from link";
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private Label urlLabel;
    private TextBox urlTextBox;
    private Button okButton;
    private Button cancelButton;
}
