using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Ui;

/// <summary>A message the page sent through <c>chrome.webview.postMessage</c>.</summary>
/// <param name="type">The message's <c>type</c> property.</param>
/// <param name="message">The whole message, <c>type</c> included.</param>
internal sealed class PageMessageEventArgs(string type, JsonElement message): EventArgs {
    public string Type { get; } = type;
    public JsonElement Message { get; } = message;
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

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebView2 _webView;

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

    public DocumentView() {
        _webView = new WebView2 {
            Name = "webView",
            Dock = DockStyle.Fill,
        };
        _webView.KeyDown += OnWebViewKeyDown;
        Controls.Add(_webView);
    }

    /// <summary>True once <see cref="InitializeAsync"/> has completed.</summary>
    public bool IsInitialized => _webView.CoreWebView2 is not null;

    /// <summary>
    /// Starts the browser: a user data folder under <see cref="App.DataFolder"/> (the install
    /// folder is not writable), the <c>web</c> folder mapped to <see cref="BaseUri"/>, and the
    /// browser's own context menus, accelerator keys, status bar and (in Release) dev tools off.
    /// </summary>
    public async Task InitializeAsync() {
        var userDataFolder = Path.Combine(App.DataFolder, "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder
        );
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(
            HostName,
            Path.Combine(AppContext.BaseDirectory, "web"),
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
        core.NewWindowRequested += OnNewWindowRequested;

        Log.Information(
            "WebView2 started: browser={Version} userData={Folder}",
            environment.BrowserVersionString, userDataFolder
        );
    }

    /// <summary>Loads a page from the <c>web</c> folder, such as <c>index.html</c>.</summary>
    public void Navigate(string page) {
        EnsureInitialized();
        _webView.CoreWebView2.Navigate(new Uri(BaseUri, page).AbsoluteUri);
    }

    /// <summary>Sends <paramref name="message"/> to the page as JSON (camelCase properties).</summary>
    public void PostMessage(object message) {
        EnsureInitialized();
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, _jsonOptions));
    }

    /// <summary>Moves keyboard focus into the document, where the screen reader can read it.</summary>
    public void FocusDocument() => _webView.Focus();

    private void EnsureInitialized() {
        if (!IsInitialized) {
            throw new InvalidOperationException("The document view has not been initialized.");
        }
    }

    private void OnWebViewKeyDown(object? sender, KeyEventArgs e) => AcceleratorKeyDown?.Invoke(this, e);

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
        // Only the app's own pages may talk to the host.
        if (!e.Source.StartsWith(BaseUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) {
            Log.Warning("Page message from an unexpected source ignored: {Source}", e.Source);
            return;
        }

        JsonElement message;

        try {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            message = document.RootElement.Clone();
        } catch (JsonException ex) {
            Log.Warning(ex, "Page message is not valid JSON");
            return;
        }

        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("type", out var typeProperty)
            || typeProperty.ValueKind != JsonValueKind.String) {
            Log.Warning("Page message without a type ignored: {Json}", e.WebMessageAsJson);
            return;
        }

        MessageReceived?.Invoke(this, new PageMessageEventArgs(typeProperty.GetString()!, message));
    }

    // The full lockdown (links handed to the host, in-page anchors) comes with the real page;
    // until then nothing may take the view away from the app's own pages.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
        if (!e.Uri.StartsWith(BaseUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) {
            Log.Information("Navigation away from the app blocked: {Uri}", e.Uri);
            e.Cancel = true;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) {
        Log.Information("New window blocked: {Uri}", e.Uri);
        e.Handled = true;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            if (_webView.CoreWebView2 is { } core) {
                core.WebMessageReceived -= OnWebMessageReceived;
                core.NavigationStarting -= OnNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
            }

            _webView.KeyDown -= OnWebViewKeyDown;
            _webView.Dispose();
        }

        base.Dispose(disposing);
    }
}
