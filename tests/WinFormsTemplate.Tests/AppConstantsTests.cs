using AwesomeAssertions;
using Oire.WinFormsTemplate.Utils.Constants;
using Xunit;

namespace Oire.WinFormsTemplate.Tests;

/// <summary>
/// Smoke tests over the application identity constants. They are deliberately cheap: their
/// job is to prove the test project compiles against the app assembly and runs on CI, and to
/// fail loudly if someone renames the product without updating the data folder layout.
/// </summary>
public class AppConstantsTests {
    [Fact]
    public void Name_IsNotEmpty() {
        App.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DataFolder_ContainsManufacturerAndAppName() {
        App.DataFolder.Should().Contain(App.ManufacturerNameShort).And.Contain(App.Name);
    }

    [Fact]
    public void ConfigPath_LivesAtTheRootOfTheDataFolder() {
        // Config deliberately sits beside the data subfolder, not inside it, so that clearing
        // user data does not take the settings with it.
        Path.GetDirectoryName(App.ConfigPath).Should().Be(App.DataFolder);
    }

    [Fact]
    public void DatabasePath_LivesInTheDataSubfolder() {
        Path.GetDirectoryName(App.DatabasePath).Should().Be(Path.Combine(App.DataFolder, App.DataSubfolder));
    }
}
