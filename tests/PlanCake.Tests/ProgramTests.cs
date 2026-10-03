using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Oire.PlanCake.Utils.Constants;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>What the user is told when the window cannot start.</summary>
[Collection(LocalizationCollection.Name)]
public class ProgramTests {
    public ProgramTests() {
        Localization.SetLanguage("en-US");
    }

    [Fact]
    public void StartupFailureMessage_SaysWhatFailedWhereTheLogsAreAndWhereToReport() {
        var message = Program.StartupFailureMessage(new InvalidOperationException("The settings file is locked."));

        message.Should().Contain("The settings file is locked.")
            .And.Contain(Logging.LogFolder)
            .And.Contain(App.RepoUrl + "/issues");
    }
}
