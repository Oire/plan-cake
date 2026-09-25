using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The shortcuts dialog is the one place that lists every key, so its rows come from the same
/// table that runs the keys, plus the keys of the document and the notes list.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class ShortcutsDialogTests {
    public ShortcutsDialogTests() {
        // The rows are asserted in English, the language of the source strings.
        Localization.SetLanguage("en-US");
    }

    [Fact]
    public void BuildRows_ListEveryAvailableHostCommandWithAllItsKeys() {
        var rows = ShortcutsDialog.BuildRows();

        foreach (var (command, keys) in HostCommands.AvailableShortcuts()) {
            rows.Should().Contain(new ShortcutRow(
                HostCommands.DisplayName(command),
                String.Join(", ", keys.Select(HostCommands.KeyText))
            ));
        }

        rows.Should().Contain(new ShortcutRow("Back to the previous file", "Alt+Left Arrow, Backspace"));
        rows.Should().Contain(new ShortcutRow("Zoom in", "Ctrl+Plus, Ctrl+Numpad Plus"));
    }

    [Fact]
    public void BuildRows_LeaveOutTheCommandsOfLaterTasks() {
        var commands = ShortcutsDialog.BuildRows().Select(row => row.Command).ToList();

        commands.Should().NotContain(HostCommands.DisplayName(HostCommand.UserManual));
    }

    [Fact]
    public void BuildRows_ListSettingsWithCtrlComma() =>
        ShortcutsDialog.BuildRows().Should().Contain(new ShortcutRow("Settings", "Ctrl+Comma"));

    [Fact]
    public void BuildRows_ListTheKeysNoMenuShows() {
        var rows = ShortcutsDialog.BuildRows();

        rows.Should().Contain(new ShortcutRow("In the document: add a note after a block, or edit a note", "Enter"));
        rows.Should().Contain(new ShortcutRow("In the document: menu of a block or a note", "Applications, Shift+F10"));
        rows.Should().Contain(new ShortcutRow("In the document: check or uncheck a task", "Space, Enter"));
        rows.Should().Contain(new ShortcutRow("In the notes list: go to the note in the document", "Enter"));
        rows.Should().Contain(new ShortcutRow("In the notes list: delete the note", "Delete"));
        rows.Should().Contain(new ShortcutRow("In the notes list: menu of the note", "Applications, Shift+F10"));
        rows[^1].Should().Be(new ShortcutRow("Exit", "Alt+F4"));
    }
}
