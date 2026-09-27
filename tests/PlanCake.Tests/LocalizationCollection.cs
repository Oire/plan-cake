using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// The collection for the app's static state: tests that change <c>Config</c> (loading it from a
/// temp file through <c>Config.OverrideFilePath</c>, setting its sections), switch the interface
/// language, or assert on the text <c>_()</c> returns. They run in this collection, one at a time
/// and apart from every other test. The name is historical: Config shares it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationCollection {
    public const string Name = "Localization";
}
