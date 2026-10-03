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
}
