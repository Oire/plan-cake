using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// THIRD-PARTY-NOTICES.txt covers every package plancake.exe carries. A package added to the app
/// fails this test until its license is added to the file (see CLAUDE.md, Installer and releases).
/// </summary>
public class ThirdPartyNoticesTests {
    /// <summary>
    /// The packages the app depends on at run time, directly or not, from plancake.deps.json
    /// (build-only packages such as GitVersion and the analyzers are not in it).
    /// </summary>
    private static List<string> RuntimePackages() {
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plancake.deps.json")));
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        var libraries = target.EnumerateObject().ToDictionary(library => library.Name, library => library.Value);
        var app = libraries.Keys.Single(name => name.StartsWith("plancake/", StringComparison.OrdinalIgnoreCase));
        var found = new List<string>();
        var pending = new Queue<string>([app]);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { app };

        while (pending.TryDequeue(out var current)) {
            if (!libraries[current].TryGetProperty("dependencies", out var dependencies)) {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject()) {
                var key = $"{dependency.Name}/{dependency.Value.GetString()}";

                if (libraries.ContainsKey(key) && seen.Add(key)) {
                    found.Add(dependency.Name);
                    pending.Enqueue(key);
                }
            }
        }

        return found;
    }

    [Fact]
    public void Notices_NameEveryPackageTheAppCarries() {
        var notices = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"));
        var packages = RuntimePackages();

        packages.Should().Contain("Markdig", "the walk over plancake.deps.json finds the app's packages");
        packages.Where(package => !notices.Contains(package, StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("every package bundled into plancake.exe needs its license in THIRD-PARTY-NOTICES.txt");
    }
}
