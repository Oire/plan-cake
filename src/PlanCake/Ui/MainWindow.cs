using System.Text.Json;
using GetText.WindowsForms;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

public partial class MainWindow: Form {
    // JAWS spike (Task 2): the page loaded at startup and the lines of its third paragraph,
    // which F12 asks the page to focus. Both go away when the real page arrives in Task 6.
    private const string SpikePage = "spike.html";
    private const string SpikeThirdParagraphLines = "12-12";

    private static readonly KeysConverter _keysConverter = new();

    private readonly StatusAnnouncer _announcer;

    public MainWindow() {
        InitializeComponent();

        // Walks the control tree and translates every text property through the gettext
        // catalog. Designer-set strings are therefore written in English and translated here;
        // strings built at run time go through _() instead.
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);

        _announcer = new StatusAnnouncer(statusStrip, statusLabel);
        documentView.MessageReceived += OnPageMessage;
        documentView.AcceleratorKeyDown += OnDocumentAcceleratorKeyDown;
    }

    /// <summary>
    /// True when the document view could not be started. The window closes itself in that case;
    /// <c>Program</c> reads this afterwards to choose the exit code.
    /// </summary>
    internal bool StartupFailed { get; private set; }

    protected override async void OnLoad(EventArgs e) {
        base.OnLoad(e);

        try {
            await documentView.InitializeAsync();
            documentView.Navigate(SpikePage);
        } catch (Exception ex) {
            Log.Error(ex, "Unable to start the document view");
            StartupFailed = true;
            DialogHelper.Show(
                _("Unable to start the document view: {0}", ex.Message),
                _("Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            Close();
        }
    }

    /// <summary>
    /// Host shortcuts pressed anywhere but in the document. Keys pressed in the document never
    /// get here (see <see cref="OnDocumentAcceleratorKeyDown"/>); both paths end in
    /// <see cref="TryRunShortcut"/>, so the one table in <see cref="HostCommands"/> decides.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        TryRunShortcut(keyData) || base.ProcessCmdKey(ref msg, keyData);

    /// <summary>
    /// Host shortcuts pressed in the document. The WebView2 control reports them as a
    /// <c>KeyDown</c> from inside a browser event, bypassing the message loop, the native menu's
    /// accelerator table and <see cref="ProcessCmdKey"/>.
    /// </summary>
    private void OnDocumentAcceleratorKeyDown(object? sender, KeyEventArgs e) {
        if (TryRunShortcut(e.KeyData)) {
            e.Handled = true;
        }
    }

    private bool TryRunShortcut(Keys keyData) {
        if (!HostCommands.TryGetCommand(keyData, out var command)) {
            return false;
        }

        // The key may have come from inside a WebView2 event: run the command later, so that a
        // dialog it opens does not start a nested message loop inside that event.
        BeginInvoke(() => RunCommand(command, keyData));
        return true;
    }

    private void RunCommand(HostCommand command, Keys keyData) {
        Log.Information("Host command {Command} from {Keys}", command, keyData);

        if (command == HostCommand.SpikeFocusThirdParagraph) {
            if (documentView.IsInitialized) {
                documentView.PostMessage(new { type = "focusLines", lines = SpikeThirdParagraphLines });
            }

            return;
        }

        // JAWS spike: the commands themselves come in later tasks. For now, report that the key
        // got through to the host at all.
        _announcer.Announce(_("Shortcut {0}: {1}", _keysConverter.ConvertToString(keyData) ?? "", command));
    }

    private void OnPageMessage(object? sender, PageMessageEventArgs e) {
        switch (e.Type) {
            case "ready":
                Log.Information("Page ready");
                documentView.FocusDocument();
                break;
            case "spikeEvent":
                AnnounceSpikeEvent(e.Message);
                break;
            default:
                Log.Warning("Unknown page message {Type}", e.Type);
                break;
        }
    }

    private void AnnounceSpikeEvent(JsonElement message) {
        var eventName = GetString(message, "event");
        var kind = GetString(message, "kind");
        var lines = GetString(message, "lines");
        var pointerType = GetString(message, "pointerType");
        var detail = message.TryGetProperty("detail", out var detailProperty) ? detailProperty.ToString() : "";

        Log.Information(
            "Spike page event {Event} on {Kind} lines={Lines} pointerType={PointerType} detail={Detail}",
            eventName, kind, lines, pointerType, detail
        );

        var text = String.IsNullOrEmpty(lines)
            ? _("{0} on {1}", eventName, kind)
            : _("{0} on {1}, lines {2}", eventName, kind, lines);

        // Checklist item 6 compares what a JAWS Enter and a mouse click report.
        if (eventName == "click") {
            text = _("{0}; pointer type \"{1}\", detail {2}", text, pointerType, detail);
        }

        _announcer.Announce(text);
    }

    private static string GetString(JsonElement message, string property) =>
        message.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    protected override void OnFormClosed(FormClosedEventArgs e) {
        documentView.MessageReceived -= OnPageMessage;
        documentView.AcceleratorKeyDown -= OnDocumentAcceleratorKeyDown;
        base.OnFormClosed(e);
    }
}
