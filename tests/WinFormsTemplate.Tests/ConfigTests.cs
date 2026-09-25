using AwesomeAssertions;
using Oire.WinFormsTemplate.Utils;
using Oire.WinFormsTemplate.Utils.Constants;
using Xunit;

namespace Oire.WinFormsTemplate.Tests;

/// <summary>
/// Round-trips the configuration file through a temp directory. <c>Config.OverrideFilePath</c>
/// is an <c>InternalsVisibleTo</c> hook that exists for exactly this: without it these tests
/// would read and overwrite the developer's real settings under <c>%APPDATA%</c>.
/// </summary>
/// <remarks>
/// <c>Config</c> is static, so these tests must not run in parallel with each other. xUnit
/// serializes tests within a single class by default, which is enough here; a second class
/// touching <c>Config</c> would need a shared collection fixture.
/// </remarks>
public class ConfigTests: IDisposable {
    private readonly string _tempFolder;

    public ConfigTests() {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"WinFormsTemplate.Tests-{Guid.NewGuid():N}");
        Config.OverrideFilePath = Path.Combine(_tempFolder, $"{App.Name}.{App.ConfigFileExtension}");
    }

    public void Dispose() {
        Config.OverrideFilePath = null;

        if (Directory.Exists(_tempFolder)) {
            Directory.Delete(_tempFolder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_OnFirstRun_WritesTheDefaultsToDisk() {
        Config.Load();

        File.Exists(Config.OverrideFilePath).Should().BeTrue();
        Config.General.Language.Should().Be(App.SystemLanguageName);
        Config.General.ConfirmExit.Should().BeTrue();
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsChangedValues() {
        Config.Load();
        Config.General.Language = "fr-FR";
        Config.General.ConfirmExit = false;
        Config.Save().Should().BeTrue();

        Config.Load();

        Config.General.Language.Should().Be("fr-FR");
        Config.General.ConfirmExit.Should().BeFalse();
    }

    [Fact]
    public void Load_OnAnUnreadableFile_FallsBackToTheDefaultsWithoutThrowing() {
        Directory.CreateDirectory(_tempFolder);
        File.WriteAllText(Config.OverrideFilePath!, "[General]\nLanguage = fr-FR\n");

        // Hold the file open with no sharing: Load must survive an IO failure rather than
        // taking the whole startup path down with it.
        using var locked = new FileStream(Config.OverrideFilePath!, FileMode.Open, FileAccess.Read, FileShare.None);

        Config.Load();

        Config.General.Language.Should().Be(App.SystemLanguageName);
    }
}
