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
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    private static int Main() {
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

            // To customize application configuration such as high DPI settings or the default
            // font, see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            Config.Load();
            Localization.SetLanguage(Config.General.Language);
            Log.Information("App startup: config loaded, language={Language}", Config.General.Language);

            Application.Run(new MainWindow());

            return ExitCode.Success;
        } catch (Exception ex) {
            Log.Fatal(ex, "App startup: unable to initialize the application");

            MessageBox.Show(
                _("Unable to start the program up. Please contact the developer."),
                _("Error"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Exclamation
            );

            return ExitCode.Error;
        } finally {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Five sinks, three audiences: the full logs for a developer, the "-short" pair a user can
    /// paste into a bug report, and a compact-JSON telemetry file for machine analysis.
    /// </summary>
    private static void ConfigureLogging() {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                LogConstants.GenericFile,
                rollOnFileSizeLimit: true
            )
            .WriteTo.File(
                LogConstants.GenericFileShort,
                rollOnFileSizeLimit: true,
                outputTemplate: LogConstants.OutputTemplateShort
            )
            .WriteTo.File(
                LogConstants.ErrorsFile,
                rollOnFileSizeLimit: true,
                restrictedToMinimumLevel: LogLevel.Warning
            )
            .WriteTo.File(
                LogConstants.ErrorsFileShort,
                rollOnFileSizeLimit: true,
                restrictedToMinimumLevel: LogLevel.Warning,
                outputTemplate: LogConstants.OutputTemplateShort
            )
            .WriteTo.File(
                new CompactJsonFormatter(),
                LogConstants.TelemetryFile,
                rollOnFileSizeLimit: true,
                restrictedToMinimumLevel: LogLevel.Information
            )
            .CreateLogger();
    }
}
