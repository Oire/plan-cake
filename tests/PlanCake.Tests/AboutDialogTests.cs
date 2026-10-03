using System.Reflection;
using AwesomeAssertions;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

public class AboutDialogTests {
    [Fact]
    public void Copyright_IsTheExecutablesOwnLine() {
        var executable = typeof(AboutDialog).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();

        executable.Should().NotBeNull("the project file sets Copyright");
        AboutDialog.Copyright.Should().Be(executable!.Copyright);
        AboutDialog.Copyright.Should().Contain("Oire Software SARL", "the About dialog names the company the installer and the license name");
    }

    [Fact]
    public void LicenseFiles_AreNextToTheExecutable() {
        // The build copies LICENSE and THIRD-PARTY-NOTICES.txt beside plancake.exe (and so into
        // the test output); the installer and the portable zip ship them from the publish folder.
        foreach (var path in AboutDialog.LicenseFiles(AppContext.BaseDirectory)) {
            File.Exists(path).Should().BeTrue("{0} is what About → Licenses opens", path);
        }
    }
}
