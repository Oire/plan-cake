namespace Oire.PlanCake.Ui;

/// <summary>
/// The row of buttons at the bottom of every dialog, laid out one way everywhere: together at the
/// end of the line (bottom right; bottom left in a right-to-left interface, where the table
/// mirrors itself), the button that commits first and Cancel or Close last, in tab order too,
/// each at least <see cref="MinimumWidth"/> wide.
/// </summary>
internal static class DialogButtons {
    /// <summary>The narrowest a dialog button gets, in logical pixels (scaled with the dialog).</summary>
    public const int MinimumWidth = 88;

    /// <summary>The name of the row, which the tests look for in every dialog.</summary>
    public const string RowName = "buttonLayout";

    /// <summary>
    /// A table with a spacer column that takes the free width, then one column per button, in the
    /// order given. The caller places the table in its dialog's last row, across every column.
    /// </summary>
    public static TableLayoutPanel CreateRow(params Button[] buttons) {
        ArgumentNullException.ThrowIfNull(buttons);

        var row = new TableLayoutPanel {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = buttons.Length + 1,
            Margin = new Padding(0, 8, 0, 0),
            Name = RowName,
            RowCount = 1,
        };

        row.SuspendLayout();
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        for (var index = 0; index < buttons.Length; index++) {
            var button = buttons[index];
            button.Anchor = AnchorStyles.None;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Margin = new Padding(3, 0, 3, 0);
            button.MinimumSize = new Size(MinimumWidth, 0);
            button.TabIndex = index;

            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(button, index + 1, 0);
        }

        row.ResumeLayout(false);

        return row;
    }
}
