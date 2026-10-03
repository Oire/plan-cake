using System.ComponentModel;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Oire.PlanCake.Utils;
using Serilog;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Ui;

/// <summary>A message the page sent through <c>chrome.webview.postMessage</c>.</summary>
/// <param name="type">The message's <c>type</c> property.</param>
/// <param name="message">The whole message, <c>type</c> included.</param>
/// <param name="files">
/// The paths of the files the page passed along with the message
/// (<c>postMessageWithAdditionalObjects</c>, used for files dropped on the page).
/// </param>
internal sealed class PageMessageEventArgs(string type, JsonElement message, IReadOnlyList<string> files): EventArgs {
    public string Type { get; } = type;
    public JsonElement Message { get; } = message;
    public IReadOnlyList<string> Files { get; } = files;
}

/// <summary>
/// Hosts the WebView2 that shows the rendered document, and carries JSON messages between the
/// host and the page. It never decides to stop the application: a failure to start the browser
/// comes out of <see cref="InitializeAsync"/> as an exception for the caller to handle.
/// </summary>
internal sealed class DocumentView: UserControl {
    /// <summary>
    /// Virtual host name the <c>web</c> folder is served from. <c>.plancake</c> is not a real
    /// top-level domain, so the name can never collide with a site on the internet.
    /// </summary>
    public const string HostName = "app.plancake";

    public static readonly Uri BaseUri = new($"https://{HostName}/");

    /// <summary>The smallest zoom factor, 50%.</summary>
    public const double MinZoom = 0.5;

    /// <summary>The largest zoom factor, 300%.</summary>
    public const double MaxZoom = 3.0;

    private readonly TabWebView _webView;

    /// <summary>
    /// The one navigation the view may make: the page <see cref="Navigate"/> asked for. Every
    /// other navigation (a link, a form, a <c>meta refresh</c> in a plan's raw HTML) is canceled.
    /// </summary>
    private readonly NavigationGate _navigation = new();

    /// <summary>
    /// Raised on the UI thread for every message the page posts. Do not open a dialog, a
    /// message box or a menu directly in a handler: defer it with <c>BeginInvoke</c>, because a
    /// nested message loop inside a WebView2 event re-enters the browser.
    /// </summary>
    public event EventHandler<PageMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Raised for an accelerator key (a key with Ctrl or Alt, a function key, Esc) pressed while
    /// the document has focus. Such a key never reaches the host's message loop or
    /// <see cref="Control.ProcessCmdKey"/>: the browser reports it, and the WinForms control
    /// turns the report into a <c>KeyDown</c>. Set <see cref="KeyEventArgs.Handled"/> to keep the
    /// key from the page. The browser process waits while handlers run, so a handler must return
    /// at once and defer the actual work with <c>BeginInvoke</c>.
    /// </summary>
    public event KeyEventHandler? AcceleratorKeyDown;

    /// <summary>
    /// Raised when an accelerator key (see <see cref="AcceleratorKeyDown"/>) is released while the
    /// document has focus; Alt released alone is how the window tells a bare Alt, which enters
    /// the menu bar. The same rules as for <see cref="AcceleratorKeyDown"/> apply.
    /// </summary>
    public event KeyEventHandler? AcceleratorKeyUp;

    public DocumentView() {
        _webView = new TabWebView {
            Name = "webView",
            Dock = DockStyle.Fill,
        };
        _webView.KeyDown += OnWebViewKeyDown;
        _webView.KeyUp += OnWebViewKeyUp;
        Controls.Add(_webView);
    }

    /// <summary>True once <see cref="InitializeAsync"/> has completed.</summary>
    public bool IsInitialized => _webView.CoreWebView2 is not null;

    /// <summary>
    /// Starts the browser: its user data folder in <see cref="App.WebView2DataFolder"/> (the
    /// install folder is not writable), the <c>web</c> folder mapped to <see cref="BaseUri"/>, and the
    /// browser's own context menus, accelerator keys, status bar and (in Release) dev tools off.
    /// </summary>
    public Task InitializeAsync() => InitializeAsync(App.WebView2DataFolder);

    /// <inheritdoc cref="InitializeAsync()"/>
    /// <param name="userDataFolder">The browser's user data folder; the tests give it one of its own.</param>
    internal async Task InitializeAsync(string userDataFolder) {
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder
        );
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(
            HostName,
            App.WebFolder,
            CoreWebView2HostResourceAccessKind.DenyCors
        );

        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsWebMessageEnabled = true;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif

        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;

        Log.Information(
            "WebView2 started: browser={Version} userData={Folder}",
            environment.BrowserVersionString, userDataFolder
        );
    }

    /// <summary>
    /// Loads a page from the <c>web</c> folder, such as <c>index.html</c>. This is the only
    /// navigation the view allows; links in the document are handed to the host instead.
    /// </summary>
    public void Navigate(string page) {
        EnsureInitialized();
        var uri = new Uri(BaseUri, page).AbsoluteUri;
        _navigation.Allow(uri);
        _webView.CoreWebView2.Navigate(uri);
    }

    /// <summary>
    /// Sends <paramref name="message"/> to the page as JSON (see
    /// <see cref="PageMessages.Serialize"/>).
    /// </summary>
    public void PostMessage(object message) {
        EnsureInitialized();
        _webView.CoreWebView2.PostWebMessageAsJson(PageMessages.Serialize(message));
    }

    /// <summary>The document's zoom factor, 1.0 being 100%.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ZoomFactor {
        get => _webView.ZoomFactor;
        set => _webView.ZoomFactor = Math.Clamp(value, MinZoom, MaxZoom);
    }

    /// <summary>
    /// The zoom factor one step (10%) in <paramref name="direction"/> from <paramref name="current"/>
    /// (1 in, -1 out), on the 10% grid and within <see cref="MinZoom"/>–<see cref="MaxZoom"/>;
    /// 0 resets it to 100%.
    /// </summary>
    internal static double StepZoom(double current, int direction) {
        if (direction == 0) {
            return 1.0;
        }

        var tenths = Math.Round(current * 10) + Math.Sign(direction);

        return Math.Clamp(tenths / 10, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Where a context menu for a page element opens, in client coordinates of the view: below
    /// the element's left edge, or at its top when its bottom is out of sight, kept inside the view.
    /// </summary>
    /// <param name="cssRect">The element's client rectangle as the page reports it, in CSS pixels.</param>
    /// <param name="scale">
    /// The page's <c>devicePixelRatio</c>, which already includes the zoom factor: CSS pixels times
    /// it are the view's pixels.
    /// </param>
    /// <param name="clientSize">The size of the view.</param>
    internal static Point MenuAnchor(RectangleF cssRect, double scale, Size clientSize) {
        if (!Double.IsFinite(scale) || scale <= 0) {
            scale = 1;
        }

        var left = cssRect.Left * scale;
        var top = cssRect.Top * scale;
        var bottom = cssRect.Bottom * scale;
        var y = bottom >= 0 && bottom < clientSize.Height ? bottom : top;

        return new Point(
            (int)Math.Round(Math.Clamp(left, 0, Math.Max(0, clientSize.Width - 1))),
            (int)Math.Round(Math.Clamp(y, 0, Math.Max(0, clientSize.Height - 1)))
        );
    }

    /// <summary>
    /// Moves keyboard focus into the document, where the screen reader can read it. Does nothing
    /// when the document already has it: focusing the control again takes the focus from the
    /// browser's own window and hands it back, and a screen reader that sees the document lose and
    /// regain the focus while the page replaces its content keeps its old place in the virtual
    /// buffer instead of following the page's focus (<c>docs/jaws-spike.md</c>, "A followed link
    /// landed at the end of the new file").
    /// </summary>
    public void FocusDocument() {
        if (!_webView.ContainsFocus) {
            _webView.Focus();
        }
    }

    /// <summary>
    /// Moves keyboard focus into the document the way Tab (<paramref name="forward"/>) or
    /// Shift+Tab would from the control before or after it: to the page's first or last
    /// focusable element, or to the page itself when it has none. Used by the window for Tab and
    /// Shift+Tab out of the notes list, which the list cannot pass on beyond its own panel.
    /// </summary>
    public void EnterByTab(bool forward) {
        _webView.EnterByTab(forward);

        // Should the directed select not take the focus, the document still gets it.
        FocusDocument();
    }

    private void EnsureInitialized() {
        if (!IsInitialized) {
            throw new InvalidOperationException("The document view has not been initialized.");
        }
    }

    private void OnWebViewKeyDown(object? sender, KeyEventArgs e) => AcceleratorKeyDown?.Invoke(this, e);

    private void OnWebViewKeyUp(object? sender, KeyEventArgs e) => AcceleratorKeyUp?.Invoke(this, e);

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
        if (ParsePageMessage(e.Source, e.WebMessageAsJson) is not { } parsed) {
            return;
        }

        MessageReceived?.Invoke(this, new PageMessageEventArgs(parsed.Type, parsed.Message, FilesOf(e)));
    }

    /// <summary>
    /// True when a message from <paramref name="source"/> comes from the app's own pages, the only
    /// ones that may talk to the host.
    /// </summary>
    internal static bool IsTrustedSource(string? source) =>
        source is not null && source.StartsWith(BaseUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A page message as the host reads it: from the app's own pages
    /// (<see cref="IsTrustedSource"/>), a JSON object with a string <c>type</c>.
    /// </summary>
    /// <param name="source">The URI of the document that sent it.</param>
    /// <param name="json">The message, as JSON.</param>
    /// <returns>
    /// Its type and the whole message; <see langword="null"/> (and a warning in the log) for
    /// anything else.
    /// </returns>
    internal static (string Type, JsonElement Message)? ParsePageMessage(string? source, string? json) {
        if (!IsTrustedSource(source)) {
            Log.Warning("Page message from an unexpected source ignored: {Source}", source);
            return null;
        }

        JsonElement message;

        try {
            using var document = JsonDocument.Parse(json ?? String.Empty);
            message = document.RootElement.Clone();
        } catch (JsonException ex) {
            Log.Warning(ex, "Page message is not valid JSON");
            return null;
        }

        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("type", out var typeProperty)
            || typeProperty.ValueKind != JsonValueKind.String) {
            Log.Warning("Page message without a type ignored: {Json}", json);
            return null;
        }

        return (typeProperty.GetString()!, message);
    }

    private static List<string> FilesOf(CoreWebView2WebMessageReceivedEventArgs e) {
        var files = new List<string>();

        if (e.AdditionalObjects is { } objects) {
            foreach (var item in objects) {
                if (item is CoreWebView2File file && !String.IsNullOrEmpty(file.Path)) {
                    files.Add(file.Path);
                }
            }
        }

        return files;
    }

    // After the page itself has loaded, nothing may take the view anywhere: app.js hands links
    // to the host and scrolls to in-page anchors itself, so any navigation that still starts
    // comes from something the plan's raw HTML did.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
        if (_navigation.TryPass(e.Uri)) {
            return;
        }

        Log.Information("Navigation blocked: {Uri}", UrlHelper.ForLog(e.Uri));
        e.Cancel = true;
    }

    private void OnFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
        Log.Information("Frame navigation blocked: {Uri}", UrlHelper.ForLog(e.Uri));
        e.Cancel = true;
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) {
        Log.Information("New window blocked: {Uri}", UrlHelper.ForLog(e.Uri));
        e.Handled = true;
    }

    /// <summary>
    /// The WebView2 control, with a way in for Tab from outside. <see cref="WebView2"/> takes the
    /// direction of a directed <c>Select</c> as the page element to focus (first or last) once it
    /// gets the focus; selecting it directly sets that direction before anything focuses it, where
    /// a <c>SelectNextControl</c> through the enclosing <see cref="DocumentView"/> would focus
    /// it first with no direction and so return to the element focused last.
    /// </summary>
    private sealed class TabWebView: WebView2 {
        public void EnterByTab(bool forward) => Select(directed: true, forward);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            if (_webView.CoreWebView2 is { } core) {
                core.WebMessageReceived -= OnWebMessageReceived;
                core.NavigationStarting -= OnNavigationStarting;
                core.FrameNavigationStarting -= OnFrameNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
            }

            _webView.KeyDown -= OnWebViewKeyDown;
            _webView.KeyUp -= OnWebViewKeyUp;
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }
}
