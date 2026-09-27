using System.IO.Pipes;
using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <remarks>
/// These tests talk through real pipes, within the two seconds a real client waits for an answer,
/// and the listener answers on thread pool threads. xUnit runs tests on thread pool threads too, so
/// a test blocked in a call, or blocking tests running beside these (sleeps, waits, retries), can
/// hold every thread the pool starts with and leave the listener waiting for the pool to grow. So
/// these tests run apart, and make the calls that block on a thread of their own, as the app makes
/// them on the window's thread.
/// </remarks>
[Collection(PipeCollection.Name)]
public class SingleInstanceTests {
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    /// <summary>A path of its own for each test, so no test (and no running PlanCake) shares a pipe.</summary>
    private static string UniquePath() =>
        Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}", "plan.md");

    /// <summary>Runs <paramref name="call"/> on a new thread, leaving the thread pool to the listener.</summary>
    private static Task<T> OnOwnThread<T>(Func<T> call) {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try {
                result.SetResult(call());
            } catch (Exception ex) {
                result.SetException(ex);
            }
        }) {
            IsBackground = true,
        };
        thread.Start();

        return result.Task;
    }

    private static Task<bool> TryActivateOnOwnThread(string path) => OnOwnThread(() => SingleInstance.TryActivate(path));

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
    public async Task TryActivate_ActivatesTheWindowShowingTheFile() {
        var path = UniquePath();
        using var activated = new ManualResetEventSlim();
        using var registration = SingleInstance.TryRegister(path, activated.Set);

        (await TryActivateOnOwnThread(path.ToLowerInvariant())).Should().BeTrue();
        activated.IsSet.Should().BeTrue("the window is activated before it answers");
    }

    [Fact]
    public async Task TryActivate_WorksAgainAfterAFirstRequest() {
        var path = UniquePath();
        var count = 0;
        using var registration = SingleInstance.TryRegister(path, () => Interlocked.Increment(ref count));

        (await TryActivateOnOwnThread(path)).Should().BeTrue();
        (await TryActivateOnOwnThread(path)).Should().BeTrue();
        Volatile.Read(ref count).Should().Be(2);
    }

    [Theory]
    [InlineData(false)] // The client comes and goes before the window waits for it.
    [InlineData(true)] // The window is waiting: the client leaves the pipe broken.
    public async Task TryActivate_WorksAfterAClientLeftWithoutAsking(bool windowWaiting) {
        var path = UniquePath();
        using var activated = new ManualResetEventSlim();
        using var clientLeft = new ManualResetEventSlim();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The listener's hooks, not a sleep, put the client before or during the window's wait.
        var hooks = windowWaiting
            ? new SingleInstance.ListenerHooks(Waiting: () => waiting.TrySetResult())
            : new SingleInstance.ListenerHooks(BeforeWait: () => clientLeft.Wait(_patience));
        using var registration = SingleInstance.TryRegister(path, activated.Set, hooks);

        if (windowWaiting) {
            await waiting.Task.WaitAsync(_patience);
        }

        // Connects and closes without a word, as a process killed while it activates would.
        using (var client = new NamedPipeClientStream(
            ".",
            SingleInstance.PipeName(path),
            PipeDirection.InOut,
            PipeOptions.CurrentUserOnly
        )) {
            client.Connect(TimeSpan.FromSeconds(5));
        }

        clientLeft.Set();

        (await TryActivateOnOwnThread(path)).Should().BeTrue();
        activated.IsSet.Should().BeTrue("the window is activated before it answers");
    }

    [Fact]
    public void TryActivate_AfterTheWindowMovedToAnotherFile_ReturnsFalse() {
        var path = UniquePath();
        SingleInstance.TryRegister(path, () => { })!.Dispose();

        SingleInstance.TryActivate(path).Should().BeFalse();
    }

    [Fact]
    public void TryClaim_WithoutAWindowForTheFile_Registers() {
        var path = UniquePath();

        SingleInstance.TryClaim(path, () => { }, out var registration).Should().Be(ClaimOutcome.Registered);

        using (registration) {
            registration.Should().NotBeNull();
            SingleInstance.TryRegister(path, () => { }).Should().BeNull();
        }
    }

    [Fact]
    public async Task TryClaim_OfAFileAnotherWindowShows_ActivatesThatWindowAndRegistersNothing() {
        var path = UniquePath();
        using var activated = new ManualResetEventSlim();
        using var owner = SingleInstance.TryRegister(path, activated.Set);

        var (outcome, registration) = await OnOwnThread(() => {
            var claimed = SingleInstance.TryClaim(path, () => { }, out var registered);

            return (claimed, registered);
        });

        outcome.Should().Be(ClaimOutcome.ActivatedOther);
        registration.Should().BeNull();
        activated.IsSet.Should().BeTrue("the window is activated before it answers");
    }

    [Fact]
    public async Task TryClaim_ByWindowsOpeningTheSameFileAtOnce_RegistersExactlyOne() {
        const int windows = 4;
        var path = UniquePath();
        var activations = 0;
        var outcomes = new ClaimOutcome[windows];
        var registrations = new SingleInstance?[windows];
        using var start = new Barrier(windows);

        await Task.WhenAll(Enumerable.Range(0, windows).Select(i => OnOwnThread(() => {
            start.SignalAndWait();
            outcomes[i] = SingleInstance.TryClaim(
                path,
                () => Interlocked.Increment(ref activations),
                out registrations[i]
            );

            return true;
        })));

        try {
            outcomes.Count(outcome => outcome == ClaimOutcome.Registered).Should().Be(1);
            outcomes.Count(outcome => outcome == ClaimOutcome.ActivatedOther).Should().Be(windows - 1);
            registrations.Count(registration => registration is not null).Should().Be(1);
            Volatile.Read(ref activations).Should().Be(windows - 1);
        } finally {
            foreach (var registration in registrations) {
                registration?.Dispose();
            }
        }
    }

    [Fact]
    public void TryClaim_AfterTheWindowLetTheFileGo_Registers() {
        var path = UniquePath();
        SingleInstance.TryRegister(path, () => { })!.Dispose();

        SingleInstance.TryClaim(path, () => { }, out var registration).Should().Be(ClaimOutcome.Registered);
        registration!.Dispose();
    }

    [Fact]
    public void TryClaim_OfAFileHeldByAWindowThatDoesNotAnswer_IsUnavailable() {
        var path = UniquePath();

        // Holds the pipe name and never answers.
        using var silent = new NamedPipeServerStream(
            SingleInstance.PipeName(path),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );

        SingleInstance.TryClaim(path, () => { }, out var registration, TimeSpan.FromMilliseconds(200))
            .Should().Be(ClaimOutcome.Unavailable);
        registration.Should().BeNull();
    }
}
