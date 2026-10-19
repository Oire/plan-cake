using Microsoft.Win32;
using Serilog;

namespace Oire.PlanCake.Utils;

/// <summary>
/// PlanCake's page in Windows Settings → Default apps, and whether the installer's registration
/// is there to show it. Windows lets only the user choose the default program for a file type, so
/// PlanCake can only open that page; it never sets the default itself.
/// </summary>
internal static class DefaultApps {
    /// <summary>
    /// The value name under <c>HKLM\Software\RegisteredApplications</c> that the installer writes.
    /// It must equal <c>RegisteredAppName</c> in <c>installer/plancake.iss</c> (<c>InstallerTests</c>
    /// checks), and must stay URI-safe, since it goes into <see cref="SettingsUri"/> unescaped.
    /// </summary>
    public const string RegisteredName = "PlanCake";

    /// <summary>
    /// Opens PlanCake's own page in Windows Settings → Default apps, where the user can choose
    /// PlanCake for <c>.md</c> and <c>.markdown</c> files. <c>registeredAppMachine</c> matches the
    /// registration under <c>HKLM\Software\RegisteredApplications</c>. Windows 11 21H2 and 22H2
    /// with the 2023-04 cumulative update, and 23H2 and later, understand it; older versions
    /// ignore the parameter and open the general Default apps page.
    /// </summary>
    public const string SettingsUri = "ms-settings:defaultapps?registeredAppMachine=" + RegisteredName;

    private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";

    /// <summary>
    /// Whether the installer registered PlanCake: <c>HKLM\Software\RegisteredApplications</c> has
    /// a <see cref="RegisteredName"/> value. Never throws; a registry that cannot be read counts
    /// as no registration.
    /// </summary>
    public static bool IsRegistered() {
        try {
            using var key = Registry.LocalMachine.OpenSubKey(RegisteredApplicationsKey);
            return key?.GetValue(RegisteredName) is not null;
        } catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) {
            Log.Debug(ex, "Unable to read the registered applications");
            return false;
        }
    }

    /// <summary>
    /// Whether Settings → Advanced shows the Default apps button and its label: only when
    /// PlanCake is not portable (the portable version registers nothing) and the installer's
    /// registration is there.
    /// </summary>
    public static bool ShowsSettingsButton(bool isPortable, bool isRegistered) => !isPortable && isRegistered;
}
