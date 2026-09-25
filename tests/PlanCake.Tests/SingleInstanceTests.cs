using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

public class SingleInstanceTests {
    /// <summary>A path of its own for each test, so no test (and no running PlanCake) shares a pipe.</summary>
    private static string UniquePath() =>
        Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}", "plan.md");

    [Fact]
    public void PipeName_IsPlanCakeAndTheSha256OfTheNormalizedPath() {
        var name = SingleInstance.PipeName(@"C:\plans\plan.md");

        name.Should().MatchRegex("^PlanCake-[0-9A-F]{64}$");
    }

    [Fact]
    public void PipeName_IgnoresCase() =>
        SingleInstance.PipeName(@"C:\Plans\Plan.md").Should().Be(SingleInstance.PipeName(@"c:\PLANS\PLAN.MD"));

    [Fact]
    public void PipeName_ResolvesDotDot() =>
        SingleInstance.PipeName(@"C:\plans\drafts\..\plan.md").Should().Be(SingleInstance.PipeName(@"C:\plans\plan.md"));

    [Fact]
    public void PipeName_IgnoresTrailingSeparators() =>
        SingleInstance.PipeName(@"C:\plans\plan.md\").Should().Be(SingleInstance.PipeName(@"C:\plans\plan.md"));

    [Fact]
    public void PipeName_ReadsForwardSlashesAsSeparators() =>
        SingleInstance.PipeName("C:/plans/plan.md").Should().Be(SingleInstance.PipeName(@"C:\plans\plan.md"));

    [Fact]
    public void PipeName_DiffersForDifferentFiles() =>
        SingleInstance.PipeName(@"C:\plans\a.md").Should().NotBe(SingleInstance.PipeName(@"C:\plans\b.md"));

    [Fact]
    public void NormalizePath_IsFullAndUpperCased() =>
        SingleInstance.NormalizePath(@"C:\Plans\Drafts\..\Plan.md\").Should().Be(@"C:\PLANS\PLAN.MD");

    [Fact]
    public void SecondRegistration_ForTheSamePath_IsDetected() {
        var path = UniquePath();
        using var first = SingleInstance.TryRegister(path, () => { });

        first.Should().NotBeNull();
        SingleInstance.TryRegister(path.ToUpperInvariant(), () => { }).Should().BeNull();
    }

    [Fact]
    public void Registration_IsFreedWhenDisposed() {
        var path = UniquePath();
        SingleInstance.TryRegister(path, () => { })!.Dispose();

        using var again = SingleInstance.TryRegister(path, () => { });

        again.Should().NotBeNull();
    }

    [Fact]
    public void TryActivate_WithoutAWindowForTheFile_ReturnsFalseAtOnce() {
        var started = DateTime.UtcNow;

        SingleInstance.TryActivate(UniquePath()).Should().BeFalse();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TryActivate_ActivatesTheWindowShowingTheFile() {
        var path = UniquePath();
        using var activated = new ManualResetEventSlim();
        using var registration = SingleInstance.TryRegister(path, activated.Set);

        SingleInstance.TryActivate(path.ToLowerInvariant()).Should().BeTrue();
        activated.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void TryActivate_WorksAgainAfterAFirstRequest() {
        var path = UniquePath();
        var count = 0;
        using var registration = SingleInstance.TryRegister(path, () => Interlocked.Increment(ref count));

        SingleInstance.TryActivate(path).Should().BeTrue();
        SingleInstance.TryActivate(path).Should().BeTrue();
        Volatile.Read(ref count).Should().Be(2);
    }

    [Fact]
    public void TryActivate_AfterTheWindowMovedToAnotherFile_ReturnsFalse() {
        var path = UniquePath();
        SingleInstance.TryRegister(path, () => { })!.Dispose();

        SingleInstance.TryActivate(path).Should().BeFalse();
    }
}
