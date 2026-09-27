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
        splitContainer = new SplitContainer();
        documentView = new DocumentView();
        notesLayout = new TableLayoutPanel();
        notesLabel = new Label();
        notesList = new Oire.WinForms.NativeControls.NativeListView();
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel();
        mainLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        notesLayout.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // mainLayout
        //
        mainLayout.ColumnCount = 1;
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayout.Controls.Add(splitContainer, 0, 0);
        mainLayout.Controls.Add(statusStrip, 0, 1);
        mainLayout.Dock = DockStyle.Fill;
        mainLayout.Margin = new Padding(0);
        mainLayout.Name = "mainLayout";
        mainLayout.RowCount = 2;
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.TabIndex = 0;
        //
        // splitContainer
        //
        splitContainer.Dock = DockStyle.Fill;
        splitContainer.Margin = new Padding(0);
        splitContainer.Name = "splitContainer";
        splitContainer.Orientation = Orientation.Vertical;
        splitContainer.Size = new Size(1000, 678);
        splitContainer.Panel1.Controls.Add(documentView);
        splitContainer.Panel2.Controls.Add(notesLayout);
        splitContainer.SplitterDistance = 680;
        splitContainer.Panel1MinSize = 200;
        splitContainer.Panel2MinSize = 150;
        splitContainer.FixedPanel = FixedPanel.Panel2;
        splitContainer.TabIndex = 0;
        splitContainer.TabStop = false;
        //
        // documentView
        //
        documentView.Dock = DockStyle.Fill;
        documentView.Margin = new Padding(0);
        documentView.Name = "documentView";
        documentView.TabIndex = 0;
        //
        // notesLayout
        //
        notesLayout.ColumnCount = 1;
        notesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        notesLayout.Controls.Add(notesLabel, 0, 0);
        notesLayout.Controls.Add(notesList, 0, 1);
        notesLayout.Dock = DockStyle.Fill;
        notesLayout.Margin = new Padding(0);
        notesLayout.Name = "notesLayout";
        notesLayout.RowCount = 2;
        notesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        notesLayout.TabIndex = 0;
        //
        // notesLabel
        //
        notesLabel.AutoSize = true;
        notesLabel.Margin = new Padding(3, 3, 3, 3);
        notesLabel.Name = "notesLabel";
        notesLabel.TabIndex = 0;
        notesLabel.Text = "Notes";
        notesLabel.UseMnemonic = false;
        //
        // notesList
        //
        notesList.Dock = DockStyle.Fill;
        notesList.Margin = new Padding(0);
        notesList.Name = "notesList";
        notesList.TabIndex = 1;
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
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        notesLayout.ResumeLayout(false);
        notesLayout.PerformLayout();
        mainLayout.ResumeLayout(false);
        mainLayout.PerformLayout();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel mainLayout;
    private SplitContainer splitContainer;
    private DocumentView documentView;
    private TableLayoutPanel notesLayout;
    private Label notesLabel;
    private Oire.WinForms.NativeControls.NativeListView notesList;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel statusLabel;
}
