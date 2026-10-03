using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Hebrew mirrors every window and the page chrome, and English leaves them left to right. Each
/// window calls <c>TextDirection.Apply</c> right after <c>Localizer.Localize</c>; these tests
/// build every window in both directions to prove none was missed. They also check what a mirrored
/// window must not turn around (code, links, text from the document) and the one layout of every
/// dialog's buttons, which the mirroring carries to the other side. The windows are built, never
/// shown.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class TextDirectionTests: IDisposable {
    public TextDirectionTests() => Localization.SetLanguage("en-US");

    public void Dispose() {
        Localization.SetLanguage("en-US");
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryWindow_InHebrew_IsMirrored() {
        Localization.SetLanguage("he");
        TextDirection.IsRightToLeft.Should().BeTrue("the Hebrew catalog ships");

        Sta.Run(() => {
            foreach (var window in Windows()) {
                using (window) {
                    window.RightToLeft.Should().Be(RightToLeft.Yes, "{0} reads right to left in Hebrew", window.GetType().Name);
                    window.RightToLeftLayout.Should().BeTrue("{0} is mirrored in Hebrew", window.GetType().Name);
                }
            }
        });
    }

    [Fact]
    public void EveryWindow_InEnglish_IsNotMirrored() =>
        Sta.Run(() => {
            foreach (var window in Windows()) {
                using (window) {
                    window.RightToLeft.Should().Be(RightToLeft.No, "{0} reads left to right in English", window.GetType().Name);
                    window.RightToLeftLayout.Should().BeFalse();
                }
            }
        });

    [Fact]
    public void CodeAndLinkBoxes_InHebrew_StayLeftToRight() {
        Localization.SetLanguage("he");

        Sta.Run(() => {
            using var settings = new SettingsDialog();
            using var link = new OpenLinkDialog();
            using var note = new NoteDialog(NoteDialogMode.Add, "Excerpt", String.Empty, _ => null, NoteEnterAction.Save);

            Find(settings, "openingMarkerTextBox").RightToLeft.Should().Be(RightToLeft.No, "a marker is code");
            Find(settings, "closingMarkerTextBox").RightToLeft.Should().Be(RightToLeft.No, "[/usernote] must not read [usernote/]");
            Find(link, "urlTextBox").RightToLeft.Should().Be(RightToLeft.No, "a link is read left to right");
            Find(note, "noteTextBox").RightToLeft.Should().Be(RightToLeft.Yes, "a note may well be written in Hebrew");
        });
    }

    [Fact]
    public void Embed_InHebrew_KeepsLeftToRightTextInItsOwnDirection() {
        Localization.SetLanguage("he");

        TextDirection.Embed("This plan describes the work.").Should().Be("\u202AThis plan describes the work.\u202C");
        TextDirection.Embed("(see below) the work…").Should().Be("\u202A(see below) the work…\u202C");
    }

    [Theory]
    [InlineData("התוכנית מתארת את העבודה.")]
    [InlineData("12-14")]
    [InlineData("")]
    public void Embed_InHebrew_LeavesTextThatDoesNotStartLeftToRightAlone(string text) {
        Localization.SetLanguage("he");

        TextDirection.Embed(text).Should().Be(text);
    }

    [Fact]
    public void Embed_InEnglish_LeavesTheTextAlone() =>
        TextDirection.Embed("This plan describes the work.").Should().Be("This plan describes the work.");

    [Fact]
    public void NoteDialog_InHebrew_KeepsTheExcerptLeftToRight() {
        Localization.SetLanguage("he");

        Sta.Run(() => {
            using var dialog = new NoteDialog(NoteDialogMode.Add, "The work.", String.Empty, _ => null, NoteEnterAction.Save);

            Find(dialog, "excerptLabel").Text.Should().Be("\u202AThe work.\u202C", "its final period stays at its end");
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("he")]
    public void EveryDialog_HasItsButtonsTogetherAtTheEndOfItsLastRow(string language) {
        Localization.SetLanguage(language);

        Sta.Run(() => {
            foreach (var window in Windows().Where(window => window is not MainWindow)) {
                using (window) {
                    var name = window.GetType().Name;
                    var found = window.Controls.Find(DialogButtons.RowName, searchAllChildren: true);
                    found.Should().ContainSingle("{0} lays out its buttons with DialogButtons", name);

                    var row = (TableLayoutPanel)found[0];
                    var parent = (TableLayoutPanel)row.Parent!;
                    parent.GetRow(row).Should().Be(parent.RowCount - 1, "{0} has its buttons at the bottom", name);
                    parent.GetColumnSpan(row).Should().Be(parent.ColumnCount, "{0} has its buttons across the dialog", name);
                    row.ColumnStyles[0].SizeType.Should().Be(SizeType.Percent, "the free width comes first, so the buttons sit together at the end");
                    row.RightToLeft.Should().Be(window.RightToLeft, "{0} mirrors its buttons with itself", name);

                    var buttons = Enumerable.Range(1, row.ColumnCount - 1)
                        .Select(column => row.GetControlFromPosition(column, 0))
                        .Cast<Button>()
                        .ToList();
                    buttons.Should().HaveCount(row.Controls.Count, "{0} keeps only buttons in its button row", name);
                    buttons.Select(button => button.TabIndex).Should().BeInAscendingOrder("{0} tabs through its buttons in their order", name);
                    buttons.Should().OnlyContain(button => button.MinimumSize.Width >= DialogButtons.MinimumWidth);

                    if (window.AcceptButton is Button accept && window.CancelButton is Button cancel && accept != cancel) {
                        buttons.Should().StartWith(accept, "{0} puts the button that commits first", name);
                        buttons.Should().EndWith(cancel, "{0} puts Cancel last", name);
                    } else if (window.CancelButton is Button close) {
                        buttons.Should().EndWith(close, "{0} puts Cancel or Close last", name);
                    }
                }
            }
        });
    }

    [Fact]
    public void EveryWindow_LetsItsLayoutWrapLabelsInsteadOfAMaximumSize() =>
        Sta.Run(() => {
            foreach (var window in Windows()) {
                using (window) {
                    AllControls(window)
                        .Where(control => control.MaximumSize != Size.Empty)
                        .Select(control => control.Name)
                        .Should().BeEmpty(
                            "a MaximumSize wraps a label at a width of its own, which broke words in {0}; a table column gives the width",
                            window.GetType().Name
                        );
                }
            }
        });

    [Fact]
    public void PageChrome_InHebrew_IsRightToLeft() {
        Localization.SetLanguage("he-IL");

        var strings = MainWindow.PageStrings();

        strings.UiDir.Should().Be("rtl");
        strings.UiLang.Should().Be("he");
        strings.NoDocument.Should().NotBe("No file is open.");
    }

    [Fact]
    public void PageChrome_InEnglish_IsLeftToRight() {
        var strings = MainWindow.PageStrings();

        strings.UiDir.Should().Be("ltr");
        strings.NoDocument.Should().Be("No file is open.");
    }

    [Fact]
    public void PageChrome_EmptyWindowHints_NameTheKeysOfTheHostTable() {
        var strings = MainWindow.PageStrings();

        strings.NoDocumentHints.Should().Equal(
            "To open a Markdown file, press Ctrl+O.",
            "To open one from a link, press Ctrl+L.",
            "You can also drag a Markdown file here.",
            "For the user manual, press F1."
        );
    }

    [Fact]
    public void Windows_CoverEveryFormOfTheApplication() {
        var built = new List<Type>();

        Sta.Run(() => {
            foreach (var window in Windows()) {
                using (window) {
                    built.Add(window.GetType());
                }
            }
        });

        var forms = typeof(MainWindow).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(Form)) && !type.IsAbstract);

        built.Should().BeEquivalentTo(forms, "a new window must be checked in both directions too");
    }

    private static Control Find(Form form, string name) => form.Controls.Find(name, searchAllChildren: true).Single();

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => AllControls(child).Prepend(child));

    /// <summary>Every form PlanCake shows.</summary>
    private static List<Form> Windows() => [
        new MainWindow(),
        new NoteDialog(NoteDialogMode.Edit, "Excerpt", "Text", _ => null, NoteEnterAction.Save),
        new OpenLinkDialog(),
        new OpeningDialog("plan.md", Task.CompletedTask),
        new SettingsDialog(),
        new AboutDialog(),
        new ShortcutsDialog(ShortcutsDialog.BuildRows()),
    ];
}
