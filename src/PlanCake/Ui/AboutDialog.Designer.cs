namespace Oire.PlanCake.Ui;

partial class AboutDialog {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent() {
        components = new System.ComponentModel.Container();
        mainLayout = new TableLayoutPanel();
        appNameLabel = new Label();
        descriptionLabel = new WrappingLabel();
        versionLabel = new Label();
        copyrightLabel = new Label();
        repoLink = new LinkLabel();
        copyInfoButton = new Button();
        copyInfoStatusLabel = new Label();
        okButton = new Button();
        copyInfoStatusTimer = new System.Windows.Forms.Timer(components);
        buttonLayout = DialogButtons.CreateRow(okButton);
        mainLayout.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout: one column of a fixed width, which the description wraps in; the dialog
        // takes the layout's height.
        //
        mainLayout.AutoSize = true;
        mainLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 408F));
        mainLayout.Controls.Add(appNameLabel, 0, 0);
        mainLayout.Controls.Add(descriptionLabel, 0, 1);
        mainLayout.Controls.Add(versionLabel, 0, 2);
        mainLayout.Controls.Add(copyrightLabel, 0, 3);
        mainLayout.Controls.Add(repoLink, 0, 4);
        mainLayout.Controls.Add(copyInfoButton, 0, 5);
        mainLayout.Controls.Add(copyInfoStatusLabel, 0, 6);
        mainLayout.Controls.Add(buttonLayout, 0, 7);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Name = "mainLayout";
        mainLayout.Padding = new Padding(16);
        mainLayout.RowCount = 8;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // appNameLabel
        //
        appNameLabel.Anchor = AnchorStyles.None;
        appNameLabel.AutoSize = true;
        appNameLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        appNameLabel.Name = "appNameLabel";
        appNameLabel.Padding = new Padding(0, 0, 0, 8);
        appNameLabel.TabIndex = 0;
        appNameLabel.Text = "PlanCake";
        appNameLabel.UseMnemonic = false;
        //
        // descriptionLabel
        //
        descriptionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        descriptionLabel.AutoSize = true;
        descriptionLabel.Name = "descriptionLabel";
        descriptionLabel.Padding = new Padding(0, 0, 0, 8);
        descriptionLabel.TabIndex = 1;
        descriptionLabel.Text = "Read Markdown files comfortably and leave notes right where they belong";
        descriptionLabel.TextAlign = ContentAlignment.TopCenter;
        descriptionLabel.UseMnemonic = false;
        //
        // versionLabel
        //
        versionLabel.Anchor = AnchorStyles.None;
        versionLabel.AutoSize = true;
        versionLabel.Name = "versionLabel";
        versionLabel.Padding = new Padding(0, 0, 0, 4);
        versionLabel.TabIndex = 2;
        versionLabel.UseMnemonic = false;
        //
        // copyrightLabel
        //
        copyrightLabel.Anchor = AnchorStyles.None;
        copyrightLabel.AutoSize = true;
        copyrightLabel.Name = "copyrightLabel";
        copyrightLabel.Padding = new Padding(0, 0, 0, 8);
        copyrightLabel.TabIndex = 3;
        copyrightLabel.UseMnemonic = false;
        //
        // repoLink
        //
        repoLink.Anchor = AnchorStyles.None;
        repoLink.AutoSize = true;
        repoLink.Name = "repoLink";
        repoLink.Padding = new Padding(0, 0, 0, 8);
        repoLink.TabIndex = 4;
        repoLink.TabStop = true;
        repoLink.Text = "PlanCake on GitHub";
        repoLink.UseMnemonic = false;
        //
        // copyInfoButton
        //
        copyInfoButton.Anchor = AnchorStyles.None;
        copyInfoButton.AutoSize = true;
        copyInfoButton.MinimumSize = new Size(100, 0);
        copyInfoButton.Name = "copyInfoButton";
        copyInfoButton.TabIndex = 5;
        copyInfoButton.Text = "&Copy info";
        //
        // copyInfoStatusLabel: one line tall even while empty (set in code), so that nothing moves
        // when it says "Copied".
        //
        copyInfoStatusLabel.Anchor = AnchorStyles.None;
        copyInfoStatusLabel.AutoSize = true;
        copyInfoStatusLabel.Name = "copyInfoStatusLabel";
        copyInfoStatusLabel.Padding = new Padding(0, 0, 0, 8);
        copyInfoStatusLabel.TabIndex = 6;
        copyInfoStatusLabel.UseMnemonic = false;
        //
        // buttonLayout: OK at the end of the row (DialogButtons).
        //
        buttonLayout.TabIndex = 7;
        //
        // okButton
        //
        okButton.DialogResult = DialogResult.OK;
        okButton.Name = "okButton";
        okButton.Text = "&OK";
        //
        // copyInfoStatusTimer
        //
        copyInfoStatusTimer.Interval = 2000;
        //
        // AboutDialog
        //
        AcceptButton = okButton;
        AccessibleRole = AccessibleRole.Dialog;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        CancelButton = okButton;
        Controls.Add(mainLayout);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "AboutDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private Label appNameLabel;
    private WrappingLabel descriptionLabel;
    private Label versionLabel;
    private Label copyrightLabel;
    private LinkLabel repoLink;
    private Button copyInfoButton;
    private Label copyInfoStatusLabel;
    private Button okButton;
    private TableLayoutPanel buttonLayout;
    private System.Windows.Forms.Timer copyInfoStatusTimer;
}
