namespace Oire.PlanCake.Ui;

partial class MainWindow {
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
        mainLayout = new TableLayoutPanel();
        documentView = new DocumentView();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        mainLayout.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(documentView, 0, 0);
        mainLayout.Controls.Add(statusStrip, 0, 1);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Margin = new Padding(0);
        mainLayout.Name = "mainLayout";
        mainLayout.RowCount = 2;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // documentView
        //
        documentView.Dock = DockStyle.Fill;
        documentView.Margin = new Padding(0);
        documentView.Name = "documentView";
        documentView.TabIndex = 0;
        //
        // statusStrip
        //
        statusStrip.Dock = DockStyle.Fill;
        statusStrip.Items.AddRange(new ToolStripItem[] { statusLabel });
        statusStrip.Name = "statusStrip";
        statusStrip.TabIndex = 1;
        //
        // statusLabel
        //
        statusLabel.Name = "statusLabel";
        statusLabel.Spring = true;
        statusLabel.Text = "Ready";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // MainWindow
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 700);
        Controls.Add(mainLayout);
        Name = "MainWindow";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PlanCake";
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private DocumentView documentView;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel statusLabel;
}
