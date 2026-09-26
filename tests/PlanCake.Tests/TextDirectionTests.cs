using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Hebrew mirrors every window and the page chrome, and English leaves them left to right. Each
/// window calls <c>TextDirection.Apply</c> right after <c>Localizer.Localize</c>; these tests
/// build every window in both directions to prove none was missed. The windows are built, never
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

    /// <summary>Every form PlanCake shows.</summary>
    private static List<Form> Windows() => [
        new MainWindow(),
        new NoteDialog(NoteDialogMode.Edit, "Excerpt", "Text", _ => null, NoteEnterAction.Save),
        new OpenLinkDialog(),
        new SettingsDialog(),
        new AboutDialog(),
        new ShortcutsDialog(ShortcutsDialog.BuildRows()),
    ];
}
