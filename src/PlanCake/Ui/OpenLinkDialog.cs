using GetText.WindowsForms;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

/// <summary>
/// File → Open from link: asks for the link of a Markdown file to download and open. The link is
/// checked before the dialog closes, so a link that is not http(s) keeps the dialog open.
/// </summary>
internal partial class OpenLinkDialog: Form {
    /// <param name="initialUrl">The link the box starts with (one found on the clipboard), if any.</param>
    public OpenLinkDialog(string? initialUrl = null) {
        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        Text = _("Open from link");

        urlTextBox.Text = initialUrl ?? String.Empty;
        urlTextBox.SelectAll();
        ActiveControl = urlTextBox;
    }

    /// <summary>The link the user entered, trimmed.</summary>
    public string Url => urlTextBox.Text.Trim();

    /// <summary>Why <paramref name="text"/> cannot be opened, or <see langword="null"/> when it can.</summary>
    internal static string? Validate(string? text) {
        if (String.IsNullOrWhiteSpace(text)) {
            return _("Please enter a link.");
        }

        return UrlHelper.IsValidHttpUrl(text, out var _)
            ? null
            : _("Please enter a valid link starting with http:// or https://.");
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        base.OnFormClosing(e);

        if (DialogResult != DialogResult.OK) {
            return;
        }

        if (Validate(urlTextBox.Text) is { } error) {
            Log.Debug("Link rejected: {Url}", urlTextBox.Text.Trim());
            DialogHelper.Show(error, _("Invalid link"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            urlTextBox.Focus();
        }
    }
}
