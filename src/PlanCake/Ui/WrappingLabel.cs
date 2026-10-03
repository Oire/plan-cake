namespace Oire.PlanCake.Ui;

/// <summary>
/// A label whose text wraps between words in every language. A stock <see cref="Label"/> breaks
/// Russian, Ukrainian or Hebrew words in the middle when Windows runs with UTF-8 as its code page
/// for programs that are not Unicode ("Beta: Use Unicode UTF-8 for worldwide language support"):
/// GDI then takes every letter outside ASCII for a double-byte character and allows a line break
/// between any two of them, unless it is told not to (<see cref="TextFormatFlags.NoFullWidthCharacterBreak"/>),
/// which a stock label never does. This one measures and draws its text with that flag. Put it
/// where a label wraps: in a <see cref="TableLayoutPanel"/> column, anchored left and right with
/// <see cref="Control.AutoSize"/> on, so the column gives it its width.
/// </summary>
internal sealed class WrappingLabel: Label {
    public override Size GetPreferredSize(Size proposedSize) {
        var padding = Padding.Size;
        var width = proposedSize.Width is <= 1 or Int32.MaxValue
            ? Int32.MaxValue
            : Math.Max(1, proposedSize.Width - padding.Width);
        var text = Text.Length == 0
            ? new Size(0, Font.Height)
            : TextRenderer.MeasureText(Text, Font, new Size(width, Int32.MaxValue), Flags);

        return new Size(
            Math.Max(text.Width + padding.Width, MinimumSize.Width),
            Math.Max(text.Height + padding.Height, MinimumSize.Height)
        );
    }

    protected override void OnPaint(PaintEventArgs e) {
        ArgumentNullException.ThrowIfNull(e);

        var bounds = new Rectangle(
            Padding.Left,
            Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - Padding.Vertical)
        );
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : SystemColors.GrayText, Flags);
    }

    /// <summary>What a stock label passes to GDI for this text, plus the flag that keeps words whole.</summary>
    private TextFormatFlags Flags {
        get {
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoFullWidthCharacterBreak;

            flags |= RtlTranslateContent(TextAlign) switch {
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter
                    => TextFormatFlags.HorizontalCenter,
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                    => TextFormatFlags.Right,
                _ => TextFormatFlags.Left,
            };

            flags |= TextAlign switch {
                ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight
                    => TextFormatFlags.VerticalCenter,
                ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                    => TextFormatFlags.Bottom,
                _ => TextFormatFlags.Top,
            };

            if (RightToLeft == RightToLeft.Yes) {
                flags |= TextFormatFlags.RightToLeft;
            }

            if (!UseMnemonic) {
                flags |= TextFormatFlags.NoPrefix;
            } else if (!ShowKeyboardCues) {
                flags |= TextFormatFlags.HidePrefix;
            }

            return flags;
        }
    }
}
