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
        infoButtonsLayout = new TableLayoutPanel();
        copyInfoButton = new Button();
        licensesButton = new Button();
        copyInfoStatusLabel = new Label();
        okButton = new Button();
        copyInfoStatusTimer = new System.Windows.Forms.Timer(components);
        buttonLayout = DialogButtons.CreateRow(okButton);
        mainLayout.SuspendLayout();
        infoButtonsLayout.SuspendLayout();
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
        mainLayout.Controls.Add(infoButtonsLayout, 0, 5);
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
        // infoButtonsLayout: Copy info and Licenses side by side, centered under the link.
        //
        infoButtonsLayout.Anchor = AnchorStyles.None;
        infoButtonsLayout.AutoSize = true;
        infoButtonsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        infoButtonsLayout.ColumnCount = 2;
        infoButtonsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        infoButtonsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        infoButtonsLayout.Controls.Add(copyInfoButton, 0, 0);
        infoButtonsLayout.Controls.Add(licensesButton, 1, 0);
        infoButtonsLayout.Name = "infoButtonsLayout";
        infoButtonsLayout.RowCount = 1;
        infoButtonsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoButtonsLayout.TabIndex = 5;
        //
        // copyInfoButton
        //
        copyInfoButton.AutoSize = true;
        copyInfoButton.MinimumSize = new Size(100, 0);
        copyInfoButton.Name = "copyInfoButton";
        copyInfoButton.TabIndex = 0;
        copyInfoButton.Text = "&Copy info";
        //
        // licensesButton: PlanCake's license and the third-party notices, in Notepad.
        //
        licensesButton.AutoSize = true;
        licensesButton.MinimumSize = new Size(100, 0);
        licensesButton.Name = "licensesButton";
        licensesButton.TabIndex = 1;
        licensesButton.Text = "&Licenses";
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
        infoButtonsLayout.ResumeLayout(false);
        infoButtonsLayout.PerformLayout();
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
    private TableLayoutPanel infoButtonsLayout;
    private Button copyInfoButton;
    private Button licensesButton;
    private Label copyInfoStatusLabel;
    private Button okButton;
    private TableLayoutPanel buttonLayout;
    private System.Windows.Forms.Timer copyInfoStatusTimer;
}
