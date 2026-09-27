using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The collection for tests that answer through a named pipe within a client's timeout. They run
/// one at a time and apart from every other test, so no blocking test beside them holds the thread
/// pool threads the pipe's listener needs.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PipeCollection {
    public const string Name = "Pipe";
}
