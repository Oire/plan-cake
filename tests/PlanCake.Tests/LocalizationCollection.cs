using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Tests that switch the interface language, or assert on the text <c>_()</c> returns, share
/// static state: they run in this collection, one at a time and apart from every other test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationCollection {
    public const string Name = "Localization";
}
