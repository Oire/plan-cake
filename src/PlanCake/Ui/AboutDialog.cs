using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms.Automation;
using GetText.WindowsForms;
using Microsoft.Win32;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;
using App = Oire.PlanCake.Utils.Constants.App;

namespace Oire.PlanCake.Ui;

/// <summary>
/// Help → About PlanCake: the product, its version, the copyright, a link to the repository, and
/// "Copy info", which puts what a bug report needs on the clipboard.
/// </summary>
internal sealed partial class AboutDialog: Form {
    public AboutDialog() {
        InitializeComponent();
        Localizer.Localize(this, Utils.Localization.Catalog);
        TextDirection.Apply(this);
        Text = _("About PlanCake");

        appNameLabel.Text = App.Name;
        versionLabel.Text = _("Version {0}", Application.ProductVersion);
        copyrightLabel.Text = TextDirection.Embed(Copyright);
        copyInfoStatusLabel.MinimumSize = new Size(0, copyInfoStatusLabel.Font.Height + copyInfoStatusLabel.Padding.Vertical);

        repoLink.LinkClicked += OnRepoLinkClicked;
        copyInfoButton.Click += OnCopyInfoClick;
        copyInfoStatusTimer.Tick += OnCopyInfoStatusTimerTick;
        ActiveControl = okButton;
    }

    /// <summary>
    /// The copyright line of the executable (<c>Copyright</c> in the project file), the same one
    /// its file properties and the installer show. A legal notice, so it is not translated.
    /// </summary>
    internal static string Copyright =>
        typeof(AboutDialog).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? String.Empty;

    /// <summary>What "Copy info" puts on the clipboard. English on purpose: it goes into bug reports.</summary>
    internal static string Info(string version, string osVersion) => String.Join(
        Environment.NewLine,
        $"{App.Name} {version}",
        $"OS: {osVersion}",
        $".NET: {Environment.Version}",
        $"OS locale: {CultureInfo.InstalledUICulture.EnglishName}",
        $"App locale: {Utils.Localization.GetCurrentCulture().EnglishName}",
        $"Portable: {(App.IsPortable ? "yes" : "no")}"
    );

    private void OnRepoLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e) {
        try {
            Process.Start(new ProcessStartInfo(App.RepoUrl) { UseShellExecute = true })?.Dispose();
        } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) {
            Log.Error(ex, "Unable to open {Url}", App.RepoUrl);
            DialogHelper.Show(_("Unable to open {0}", App.RepoUrl), _("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnCopyInfoClick(object? sender, EventArgs e) {
        try {
            Clipboard.SetText(Info(Application.ProductVersion, FriendlyOsVersion()));
            copyInfoStatusLabel.Text = _("Copied");
        } catch (ExternalException ex) {
            Log.Error(ex, "Unable to copy the program information to the clipboard");
            copyInfoStatusLabel.Text = _("Unable to copy to the clipboard.");
        }

        // A label that changes is silent; the notification is what a screen reader speaks.
        copyInfoButton.AccessibilityObject.RaiseAutomationNotification(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            copyInfoStatusLabel.Text
        );
        copyInfoStatusTimer.Stop();
        copyInfoStatusTimer.Start();
    }

    private void OnCopyInfoStatusTimerTick(object? sender, EventArgs e) {
        copyInfoStatusTimer.Stop();
        copyInfoStatusLabel.Text = String.Empty;
    }

    /// <summary>
    /// The Windows edition and build as people know it: <c>Windows 11 Pro 24H2 (Build 26100)</c>.
    /// The registry's product name still says Windows 10 on many Windows 11 machines; Windows 11
    /// starts at build 22000.
    /// </summary>
    private static string FriendlyOsVersion() {
        try {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            if (key?.GetValue("ProductName") is string productName) {
                var build = key.GetValue("CurrentBuildNumber") as string;

                if (Int32.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 22000) {
                    productName = productName.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
                }

                if (key.GetValue("DisplayVersion") is string displayVersion) {
                    productName += $" {displayVersion}";
                }

                return build is null ? productName : $"{productName} (Build {build})";
            }
        } catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) {
            Log.Warning(ex, "Unable to read the Windows version from the registry");
        }

        return Environment.OSVersion.ToString();
    }

    protected override void OnFormClosed(FormClosedEventArgs e) {
        repoLink.LinkClicked -= OnRepoLinkClicked;
        copyInfoButton.Click -= OnCopyInfoClick;
        copyInfoStatusTimer.Tick -= OnCopyInfoStatusTimerTick;
        copyInfoStatusTimer.Stop();
        base.OnFormClosed(e);
    }
}
