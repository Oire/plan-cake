using GetText.WindowsForms;
using Oire.PlanCake.Utils;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

/// <summary>
/// Shown while a file that takes a while to read and render opens: the file's name, a progress bar
/// that only says the work goes on (Markdig reports no progress), and Cancel, which Escape
/// presses too. It closes itself, with <see cref="DialogResult.OK"/>, once the work is done,
/// however it ended; the caller reads the outcome from the task. Cancel leaves the window as it
/// was, and the work's result is dropped when it comes.
/// </summary>
internal partial class OpeningDialog: Form {
    private readonly Task _work;

    /// <param name="fileName">The name of the file being opened.</param>
    /// <param name="work">Reads and renders the file; the dialog waits for it.</param>
    public OpeningDialog(string fileName, Task work) {
        ArgumentNullException.ThrowIfNull(work);

        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        Text = _("Opening a file");

        _work = work;
        messageLabel.Text = _("Opening {0}…", fileName);
        ActiveControl = cancelButton;
    }

    protected override async void OnShown(EventArgs e) {
        base.OnShown(e);

        // WhenAny never throws: how the work ended is the caller's to read.
        await Task.WhenAny(_work);

        if (!IsDisposed && Visible && DialogResult == DialogResult.None) {
            DialogResult = DialogResult.OK;
        }
    }
}
