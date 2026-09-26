using System.Runtime.ExceptionServices;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Runs test code that builds windows on a single-threaded apartment thread, as WinForms
/// expects; the test runner's threads are multithreaded. The windows are built and disposed,
/// never shown.
/// </summary>
internal static class Sta {
    public static void Run(Action action) {
        ArgumentNullException.ThrowIfNull(action);

        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => {
            try {
                action();
            } catch (Exception ex) {
                // Rethrown on the test's own thread, where the runner reports it.
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
