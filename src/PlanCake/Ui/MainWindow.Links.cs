using System.ComponentModel;
using System.Diagnostics;
using Oire.PlanCake.Utils;
using Serilog;
using static Oire.PlanCake.Utils.Localization;

namespace Oire.PlanCake.Ui;

internal sealed partial class MainWindow {
    /// <summary>Follows a link from the document: see <see cref="LinkResolver"/>.</summary>
    private void OpenLink(string href) {
        var folder = _file is null ? null : Path.GetDirectoryName(_file.Path);
        var target = LinkResolver.Resolve(href, folder);
        Log.Information(
            "Link {Href} resolved to {Kind} {Target}",
            UrlHelper.ForLog(href), target.Kind, UrlHelper.ForLog(target.Target)
        );

        switch (target.Kind) {
            case LinkKind.External:
            case LinkKind.OtherFile:
                ShellOpen(target.Target);
                break;
            case LinkKind.Markdown:
                OpenFile(target.Target);
                break;
            case LinkKind.Program:
                OfferToShowProgram(target.Target);
                break;
            case LinkKind.InPage:
                // The page scrolls to its own anchors.
                break;
            case LinkKind.Missing:
                _announcer.Announce(_("The link target does not exist: {0}", target.Target));
                break;
            case LinkKind.Unsupported:
                _announcer.Announce(_("This link cannot be opened: {0}", target.Target));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(href), target.Kind, null);
        }
    }

    /// <summary>
    /// A link to a program, a script or any other file that is not a passive document: it is
    /// never opened from a link, whatever the link text says, since its default action might run
    /// something. Yes shows it selected in File Explorer, where the user can decide.
    /// </summary>
    private void OfferToShowProgram(string path) {
        var confirmed = DialogHelper.Confirm(
            _("PlanCake does not open this file from a link, since opening it could run a program:\n\n{0}\n\nShow it in File Explorer?", path),
            _("Open link"),
            MessageBoxIcon.Warning
        );

        if (confirmed) {
            StartProcess(new ProcessStartInfo(ExplorerPath) {
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false,
            }, path);
        }

        ReturnFocus();
    }

    private static string ExplorerPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>
    /// Opens <paramref name="target"/> with the system's default verb: a web link, a folder, a
    /// passive document (<see cref="LinkResolver.IsPassiveDocument"/>). Never for a program, a
    /// script or any other local file from a link, which the default verb might run.
    /// </summary>
    private void ShellOpen(string target) =>
        StartProcess(new ProcessStartInfo(target) { UseShellExecute = true }, target);

    private void StartProcess(ProcessStartInfo startInfo, string target) {
        try {
            Process.Start(startInfo)?.Dispose();
        } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException) {
            Log.Error(ex, "Unable to open {Target}", UrlHelper.ForLog(target));
            _announcer.Announce(_("Unable to open {0}", target));
        }
    }

    /// <summary>Opens the first Markdown file of a drop.</summary>
    private void OpenDroppedFiles(IEnumerable<string> files) {
        if (FirstMarkdownFile(files) is { } path) {
            OpenFile(path);
        } else {
            _announcer.Announce(_("Only Markdown files (.md, .markdown) can be opened."));
        }
    }

    private static string? FirstMarkdownFile(IEnumerable<string> files) =>
        files.FirstOrDefault(LinkResolver.IsMarkdownPath);

    private static string[] DroppedFiles(IDataObject? data) =>
        data?.GetData(DataFormats.FileDrop) as string[] ?? [];

    private void OnWindowDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = FirstMarkdownFile(DroppedFiles(e.Data)) is null ? DragDropEffects.None : DragDropEffects.Copy;

    private void OnWindowDragDrop(object? sender, DragEventArgs e) {
        var files = DroppedFiles(e.Data);

        // Explorer waits until the drop returns: open the file afterwards.
        BeginInvoke(() => OpenDroppedFiles(files));
    }
}
