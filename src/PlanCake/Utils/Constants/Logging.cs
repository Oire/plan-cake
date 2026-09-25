namespace Oire.PlanCake.Utils.Constants;

/// <summary>
/// Paths and formats for the Serilog sinks configured in <c>Program.ConfigureLogging</c>.
/// </summary>
public static class Logging {
    public static readonly string LogFolder = Path.Combine(App.DataFolder, "logs");
    public const string LogFileExtension = "log";
    public static readonly string GenericFile = Path.Combine(LogFolder, $"{App.Name}.{LogFileExtension}");
    public static readonly string GenericFileShort = Path.Combine(LogFolder, $"{App.Name}-short.{LogFileExtension}");
    public static readonly string ErrorsFile = Path.Combine(LogFolder, $"errors.{LogFileExtension}");
    public static readonly string ErrorsFileShort = Path.Combine(LogFolder, $"errors-short.{LogFileExtension}");
    public static readonly string TelemetryFile = Path.Combine(LogFolder, "analysis.json");

    /// <summary>
    /// Terse template for the "-short" sinks: the ones a user is asked to paste into a bug
    /// report. No timestamps, no source context, just level and message.
    /// </summary>
    public const string OutputTemplateShort = "[{Level:u4}] {Message:lj}{NewLine}{Exception}";
}
