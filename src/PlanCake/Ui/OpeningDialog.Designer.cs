namespace Oire.PlanCake.Ui;

partial class OpeningDialog {
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
        messageLabel = new Label();
        progressBar = new ProgressBar();
        cancelButton = new Button();
        mainLayout.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(messageLabel, 0, 0);
        mainLayout.Controls.Add(progressBar, 0, 1);
        mainLayout.Controls.Add(cancelButton, 0, 2);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Name = "mainLayout";
        mainLayout.Padding = new Padding(12);
        mainLayout.RowCount = 3;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // messageLabel
        //
        messageLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        messageLabel.AutoEllipsis = true;
        messageLabel.Name = "messageLabel";
        messageLabel.Size = new Size(376, 20);
        messageLabel.TabIndex = 0;
        messageLabel.UseMnemonic = false;
        //
        // progressBar
        //
        progressBar.Dock = DockStyle.Fill;
        progressBar.MarqueeAnimationSpeed = 30;
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(376, 20);
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.TabIndex = 1;
        //
        // cancelButton
        //
        cancelButton.Anchor = AnchorStyles.Right;
        cancelButton.AutoSize = true;
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Name = "cancelButton";
        cancelButton.TabIndex = 2;
        cancelButton.Text = "&Cancel";
        //
        // OpeningDialog
        //
        AccessibleRole = AccessibleRole.Dialog;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = cancelButton;
        ClientSize = new Size(400, 120);
        Controls.Add(mainLayout);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "OpeningDialog";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Opening a file";
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private Label messageLabel;
    private ProgressBar progressBar;
    private Button cancelButton;
}
