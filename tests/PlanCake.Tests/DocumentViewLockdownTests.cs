using AwesomeAssertions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The lockdown of the document view, in a real WebView2: a plan's raw HTML can neither run
/// script that talks to the host, nor take the view anywhere, nor pass for a block or a check box
/// the page acts on. The view is built in a form that is never shown, so no window appears and
/// the focus stays where it is. Skipped where the WebView2 Runtime is not installed.
/// </summary>
[Trait("Category", "WebView2")]
public class DocumentViewLockdownTests: IDisposable {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A plan written to escape: ways raw HTML could run script, navigate, or imitate the markers
    /// the page acts on. The fakes are raw HTML blocks of their own, outside any real block.
    /// </summary>
    private const string HostilePlan = """
        # Plan

        A real paragraph.

        <script>chrome.webview.postMessage({ type: "openLink", href: "script.exe" });</script>

        <img src="x" onerror="chrome.webview.postMessage({ type: 'openLink', href: 'onerror.exe' })">

        <svg onload="chrome.webview.postMessage({ type: 'goBack' })"><rect width="1" height="1"/></svg>

        <meta http-equiv="refresh" content="0; url=https://example.invalid/refresh">

        <iframe src="https://example.invalid/frame"></iframe>

        <form action="https://example.invalid/form" method="get"><input name="q" value="1"><button>Go</button></form>

        <object data="https://example.invalid/object"></object>

        <embed src="https://example.invalid/embed">

        <div data-lines="3-3" id="fake-block">Pretends to be the paragraph on line 3.</div>

        <div><span data-note="0" id="fake-note">Pretends to be a note.</span></div>

        <input type="checkbox" data-plancake-task="true" id="fake-task">

        """;

    private readonly string _userData = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-WebView2-{Guid.NewGuid():N}");

    public void Dispose() {
        // The browser process lets go of its folder a moment after the view is closed.
        for (var attempt = 0; attempt < 20 && Directory.Exists(_userData); attempt++) {
            try {
                Directory.Delete(_userData, recursive: true);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                Thread.Sleep(250);
            }
        }

        GC.SuppressFinalize(this);
    }

    [WebView2Fact]
    public void HostileRawHtml_PostsNothingAndNavigatesNowhere() {
        var render = MarkdownRenderer.Render(
            HostilePlan,
            new RenderOptions(NoteMarkers.Default, RenderMode.Interactive, new RenderStrings("User note"))
        );
        var page = new Uri(DocumentView.BaseUri, "index.html").AbsoluteUri;
        var messages = new List<string>();
        var navigations = new List<(string Uri, bool Canceled)>();
        var frameNavigations = new List<(string Uri, bool Canceled)>();

        StaLoop.Run(_timeout, async () => {
            using var form = new Form();
            using var view = new DocumentView { Dock = DockStyle.Fill };
            form.Controls.Add(view);

            // The handles exist; the form is never shown.
            _ = form.Handle;
            _ = view.Handle;

            await view.InitializeAsync(_userData);

            // Added after the view's own handlers, so these see what the view decided.
            var core = view.Controls.OfType<WebView2>().Single().CoreWebView2;
            core.NavigationStarting += (_, e) => navigations.Add((e.Uri, e.Cancel));
            core.FrameNavigationStarting += (_, e) => frameNavigations.Add((e.Uri, e.Cancel));
            view.MessageReceived += (_, e) => messages.Add(e.Type);

            view.Navigate("index.html");
            await StaLoop.WaitFor(() => messages.Contains(PageMessages.Ready), _timeout);

            view.PostMessage(new RenderMessage(render.Html, 1, "en", "Plan", null));

            // Time for a refresh, a frame or a script to try something.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // A click on what the plan's markup pretends to be: a block, a note, a task check box.
            await core.ExecuteScriptAsync("""
                for (const id of ["fake-block", "fake-note", "fake-task"]) {
                    document.getElementById(id)?.click();
                }
                """);
            await Task.Delay(TimeSpan.FromSeconds(1));

            messages.Should().Equal([PageMessages.Ready], "nothing in the plan may talk to the host");
            navigations.Where(navigation => navigation.Uri != page)
                .Should().OnlyContain(navigation => navigation.Canceled, "nothing in the plan may take the view away");

            // The same click on the real paragraph does reach the host: the page works.
            await core.ExecuteScriptAsync("""document.querySelector("main p[data-lines]").click();""");
            await StaLoop.WaitFor(() => messages.Contains(PageMessages.Activate), _timeout);
        });

        // Whatever the plan starts (the meta refresh, the frame) is refused.
        navigations.Should().ContainSingle(navigation => !navigation.Canceled)
            .Which.Uri.Should().Be(page, "the only navigation is the page the host asked for");
        frameNavigations.Should().OnlyContain(navigation => navigation.Canceled);
    }
}

/// <summary>A fact that runs only where the WebView2 Runtime is installed.</summary>
internal sealed class WebView2FactAttribute: FactAttribute {
    public WebView2FactAttribute() {
        try {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        } catch (WebView2RuntimeNotFoundException) {
            Skip = "The WebView2 Runtime is not installed.";
        }
    }
}

/// <summary>
/// Runs asynchronous test code on a single-threaded apartment thread with a WinForms message
/// loop, which WebView2 needs for its events and continuations.
/// </summary>
internal static class StaLoop {
    public static void Run(TimeSpan timeout, Func<Task> action) => Sta.Run(() => {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var task = action();
        var deadline = DateTime.UtcNow + timeout + timeout;

        while (!task.IsCompleted) {
            if (DateTime.UtcNow > deadline) {
                throw new TimeoutException("The WebView2 test did not finish in time.");
            }

            Application.DoEvents();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
    });

    /// <summary>Waits, with the message loop running, until <paramref name="condition"/> holds.</summary>
    public static async Task WaitFor(Func<bool> condition, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;

        while (!condition()) {
            if (DateTime.UtcNow > deadline) {
                throw new TimeoutException("The page did not answer in time.");
            }

            await Task.Delay(50);
        }
    }
}
