using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Enums;
using Oire.WinForms.NativeControls;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Mnemonics are unique on every menu level (submenus and context menus included) and in every
/// dialog, in English and in every shipped catalog. A duplicate in a menu throws when the menu is
/// built, on that language only; one in a dialog makes Alt+letter cycle between two controls
/// instead of acting. These tests stand in for running the application once in each language.
/// </summary>
[Collection(LocalizationCollection.Name)]
public class MnemonicTests: IDisposable {
    public MnemonicTests() => Localization.SetLanguage("en-US");

    public void Dispose() {
        Localization.SetLanguage("en-US");
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Languages() => new(LanguageList.SupportedCodes);

    [Theory]
    [MemberData(nameof(Languages))]
    public void MenuBar_HasUniqueMnemonicsOnEveryLevel(string language) {
        UseLanguage(language);

        Sta.Run(() => {
            using var window = new MainWindow();
            var spec = window.BuildMenuSpec();

            FluentActions.Invoking(() => MenuSpecValidator.Validate(spec)).Should().NotThrow();
            ItemsWithoutMnemonic(spec.Items).Should().BeEmpty("every menu and command is reached by a letter");
        });
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void ContextMenus_HaveUniqueMnemonics(string language) {
        UseLanguage(language);

        var block = MainWindow.BlockMenuSpec(Nothing, Nothing, Nothing, Nothing);
        var list = MainWindow.NotesListMenuSpec(Nothing, Nothing);

        block.Items.Should().HaveCount(4);

        foreach (var spec in new[] { block, list }) {
            FluentActions.Invoking(() => MenuSpecValidator.Validate(spec)).Should().NotThrow();
            ItemsWithoutMnemonic(spec.Items).Should().BeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Forms_HaveUniqueMnemonics(string language) {
        UseLanguage(language);

        Sta.Run(() => {
            foreach (var dialog in Forms()) {
                using (dialog) {
                    var duplicates = AllControls(dialog)
                        .Where(TakesMnemonic)
                        .Select(control => (control.Text, Mnemonic: MenuTextFormatter.ExtractMnemonic(control.Text)))
                        .Where(entry => entry.Mnemonic is not null)
                        .GroupBy(entry => entry.Mnemonic)
                        .Where(group => group.Count() > 1)
                        .Select(group => String.Join(" / ", group.Select(entry => entry.Text)));

                    duplicates.Should().BeEmpty("{0} in {1} needs a letter of its own for each control", dialog.GetType().Name, language);
                }
            }
        });
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Forms_GiveEveryButtonAndCheckBoxAMnemonic(string language) {
        UseLanguage(language);

        Sta.Run(() => {
            foreach (var dialog in Forms()) {
                using (dialog) {
                    AllControls(dialog)
                        .OfType<ButtonBase>()
                        .Where(button => !button.UseMnemonic || MenuTextFormatter.ExtractMnemonic(button.Text) is null)
                        .Select(button => button.Text)
                        .Should().BeEmpty("every button of {0} in {1} is reached by Alt and a letter", dialog.GetType().Name, language);
                }
            }
        });
    }

    [Fact]
    public void Forms_CoverEveryFormOfTheApplication() {
        var built = new List<Type>();

        Sta.Run(() => {
            foreach (var form in Forms()) {
                using (form) {
                    built.Add(form.GetType());
                }
            }
        });

        var forms = typeof(MainWindow).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(Form)) && !type.IsAbstract);

        built.Should().BeEquivalentTo(forms, "a new form must have its mnemonics checked in every catalog too");
    }

    private static void Nothing() { }

    /// <summary>Switches the interface language and makes sure its catalog is really there.</summary>
    private static void UseLanguage(string language) {
        Localization.SetLanguage(language);
        Localization.GetCurrentCulture().TwoLetterISOLanguageName.Should()
            .Be(language, "the {0} catalog must be compiled into the output (Compile-Translations.ps1)", language);
    }

    /// <summary>
    /// Items a user cannot reach by a letter: every item but a separator and a language choice
    /// (language names carry no mnemonics, being written each in its own language).
    /// </summary>
    private static IEnumerable<string> ItemsWithoutMnemonic(IReadOnlyList<NativeMenuItemSpec> items) =>
        items.Where(item => !item.IsSeparator && item.RadioGroup is null && MenuTextFormatter.ExtractMnemonic(item.Text) is null)
            .Select(item => item.Text)
            .Concat(items.Where(item => item.Children is not null).SelectMany(item => ItemsWithoutMnemonic(item.Children!)));

    /// <summary>
    /// Every form PlanCake shows; <see cref="Forms_CoverEveryFormOfTheApplication"/> keeps the list
    /// whole.
    /// </summary>
    private static List<Form> Forms() => [
        new MainWindow(),
        new NoteDialog(NoteDialogMode.Add, "Excerpt", String.Empty, _ => null, NoteEnterAction.Save),
        new OpenLinkDialog(),
        new OpeningDialog("plan.md", Task.CompletedTask),
        new SettingsDialog(),
        new AboutDialog(),
        new ShortcutsDialog(ShortcutsDialog.BuildRows()),
    ];

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => AllControls(child).Prepend(child));

    /// <summary>Controls whose text is a caption with a mnemonic, not content the user typed.</summary>
    private static bool TakesMnemonic(Control control) => control switch {
        Label label => label.UseMnemonic,
        ButtonBase button => button.UseMnemonic,
        GroupBox => true,
        _ => false,
    };
}
