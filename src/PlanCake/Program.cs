using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Oire.PlanCake.Cli;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Serilog;
using Serilog.Formatting.Compact;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;
using ExitCode = Oire.PlanCake.Utils.Constants.ExitCode;
using LogConstants = Oire.PlanCake.Utils.Constants.Logging;
using LogLevel = Serilog.Events.LogEventLevel;

namespace Oire.PlanCake;

internal static class Program {
    /// <summary>Microsoft's download page for the WebView2 Runtime (Evergreen bootstrapper).</summary>
    private const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    /// <param name="args">
    /// The file to open in the window, if any; or a subcommand (<c>list</c>, <c>check</c>,
    /// <c>clear</c>, <c>export</c>), <c>--help</c> or <c>--version</c>, which run headless
    /// (see <see cref="CliRunner"/>). Anything the parser rejects is reported on the command
    /// line too, never by opening the window.
    /// </param>
    [STAThread]
    private static int Main(string[] args) {
        ConfigureLogging();

        // Exceptions on a background thread or in a dropped Task do not reach the try/catch
        // below; without these two handlers they vanish and the app dies with no log entry.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            if (e.ExceptionObject is Exception ex) {
                Log.Fatal(ex, "Unhandled exception on a background thread");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) => {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try {
            Log.Information(
                "App startup: version={Version} portable={Portable} dataFolder={Folder}",
                typeof(Program).Assembly.GetName().Version, App.IsPortable, App.DataFolder
            );

            if (!CliRunner.OpensWindow(args, out var file)) {
                return RunCli(args);
            }

            // One window per file: the window already showing it comes to the front instead.
            if (file is not null && SingleInstance.TryActivate(file)) {
                Log.Information("App startup: {Path} is open in another window, which was activated", file);
                return ExitCode.Success;
            }

            // WinForms catches an exception from the message loop (an async void handler, a
            // BeginInvoke callback) itself: without a handler it shows its own dialog and
            // AppDomain.UnhandledException never fires, so nothing reached the log.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => OnUiThreadException(e.Exception);

            // To customize application configuration such as high DPI settings or the default
            // font, see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            Config.Load();
            Localization.SetLanguage(Config.General.Language);
            Log.Information("App startup: config loaded, language={Language}", Config.General.Language);

            if (!IsWebView2RuntimeAvailable()) {
                ReportMissingWebView2Runtime();
                return ExitCode.Error;
            }

            using var mainWindow = new MainWindow(file);
            Application.Run(mainWindow);

            return mainWindow.StartupFailed ? ExitCode.Error : ExitCode.Success;
        } catch (Exception ex) {
            Log.Fatal(ex, "App startup: unable to initialize the application");
            ReportStartupFailure(ex);

            return ExitCode.Error;
        } finally {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// An exception on the UI thread that nothing caught: logged, then the user decides whether
    /// the window goes on (the document on disk is never half written: every write is atomic)
    /// or PlanCake closes.
    /// </summary>
    private static void OnUiThreadException(Exception ex) {
        Log.Error(ex, "Unhandled exception on the UI thread");

        var goOn = DialogHelper.Confirm(
            _("Something went wrong: {0}", ex.Message)
                + Environment.NewLine + Environment.NewLine
                + _("The details are in the log. Keep PlanCake open?"),
            _("Error"),
            MessageBoxIcon.Error
        );

        if (!goOn) {
            Log.Information("The user closed PlanCake after an unhandled exception");
            Application.Exit();
        }
    }

    /// <summary>The address problems are reported at.</summary>
    internal const string IssuesUrl = App.RepoUrl + "/issues";

    /// <summary>
    /// The message shown when the window cannot start: what failed, where the logs are (the
    /// portable folder or the one under the user profile) and where to report it.
    /// </summary>
    internal static string StartupFailureMessage(Exception ex) =>
        _("PlanCake could not start: {0}", ex.Message)
            + Environment.NewLine + Environment.NewLine
            + _(
                "The details are in the logs in {0}. Please report the problem at {1}.",
                LogConstants.LogFolder,
                IssuesUrl
            );

    /// <summary>
    /// Says why the window could not start, and offers to open the log folder. The failure may
    /// come before visual styles are on, which a task dialog needs: a plain message box then.
    /// </summary>
    private static void ReportStartupFailure(Exception ex) {
        var message = StartupFailureMessage(ex);
        bool openLogs;

        try {
            openLogs = DialogHelper.Confirm(
                message + Environment.NewLine + Environment.NewLine + _("Open the log folder now?"),
                _("Error"),
                MessageBoxIcon.Error
            );
        } catch (Exception dialogException)
            when (dialogException is InvalidOperationException or EntryPointNotFoundException) {
            Log.Warning(dialogException, "App startup: no task dialog; showing a message box");
            DialogHelper.Show(message, _("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);

            return;
        }

        if (!openLogs) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo(LogConstants.LogFolder) { UseShellExecute = true })?.Dispose();
        } catch (Exception openException)
            when (openException is System.ComponentModel.Win32Exception or InvalidOperationException) {
            Log.Error(openException, "Unable to open the log folder {Folder}", LogConstants.LogFolder);
        }
    }

    /// <summary>
    /// A subcommand, <c>--help</c>, <c>--version</c> or a parse error: headless, with the output
    /// on the redirected standard output or the parent's console, never a dialog.
    /// </summary>
    private static int RunCli(string[] args) {
        Log.Information("App startup: command line {Command}", args.Length > 0 ? args[0] : String.Empty);

        using var console = ConsoleAttacher.Attach();

        try {
            Config.Load();
            Localization.SetLanguage(Config.General.Language);

            return new CliRunner(console.Output, console.Error).Run(args);
        } catch (Exception ex) {
            Log.Fatal(ex, "App startup: unable to run the command line");
            console.Error.Write(_("Error: {0}", ex.Message) + "\n");

            return ExitCode.Error;
        }
    }

    /// <summary>
    /// The document is shown in WebView2, whose runtime (the Edge engine) is a separate system
    /// component. The installer installs it when missing, but the portable zip cannot, so the
    /// app checks before creating any window.
    /// </summary>
    private static bool IsWebView2RuntimeAvailable() {
        try {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Log.Information("App startup: WebView2 Runtime {Version}", version);
            return !String.IsNullOrEmpty(version);
        } catch (WebView2RuntimeNotFoundException ex) {
            Log.Error(ex, "App startup: WebView2 Runtime not found");
            return false;
        }
    }

    private static void ReportMissingWebView2Runtime() {
        var message = _("PlanCake needs the Microsoft Edge WebView2 Runtime, which is not installed on this computer.")
            + Environment.NewLine + Environment.NewLine
            + _("You can download it from {0}. Open the download page now?", WebView2DownloadUrl);
        var confirmed = DialogHelper.Confirm(
            message,
            _("WebView2 Runtime not found"),
            MessageBoxIcon.Error
        );

        if (!confirmed) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo(WebView2DownloadUrl) { UseShellExecute = true })?.Dispose();
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) {
            Log.Error(ex, "Unable to open the WebView2 download page");
        }
    }

    /// <summary>The size a log file grows to before the next one is started.</summary>
    internal const long LogFileSizeLimit = 10 * 1024 * 1024;

    /// <summary>
    /// How many files of each log are kept. Each window and each command-line run is a process
    /// of its own, and one that finds a log held by another writes to a numbered sibling
    /// (<c>PlanCake_001.log</c>), so this counts those siblings too.
    /// </summary>
    internal const int RetainedLogFileCount = 10;

    /// <summary>
    /// Five sinks, three audiences: the full logs for a developer, the "-short" pair a user can
    /// paste into a bug report, and a compact-JSON telemetry file for machine analysis. A Debug
    /// build logs at Debug level (every page message, every host command); a Release build from
    /// Information up.
    /// </summary>
    private static void ConfigureLogging() {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .WriteTo.File(
                LogConstants.GenericFile,
                fileSizeLimitBytes: LogFileSizeLimit,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedLogFileCount
            )
            .WriteTo.File(
                LogConstants.GenericFileShort,
                fileSizeLimitBytes: LogFileSizeLimit,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedLogFileCount,
                outputTemplate: LogConstants.OutputTemplateShort
            )
            .WriteTo.File(
                LogConstants.ErrorsFile,
                fileSizeLimitBytes: LogFileSizeLimit,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedLogFileCount,
                restrictedToMinimumLevel: LogLevel.Warning
            )
            .WriteTo.File(
                LogConstants.ErrorsFileShort,
                fileSizeLimitBytes: LogFileSizeLimit,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedLogFileCount,
                restrictedToMinimumLevel: LogLevel.Warning,
                outputTemplate: LogConstants.OutputTemplateShort
            )
            .WriteTo.File(
                new CompactJsonFormatter(),
                LogConstants.TelemetryFile,
                fileSizeLimitBytes: LogFileSizeLimit,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedLogFileCount,
                restrictedToMinimumLevel: LogLevel.Information
            )
            .CreateLogger();
    }
}
