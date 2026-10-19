using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Oire.PlanCake.Utils;
using Xunit;

namespace Oire.PlanCake.Tests;

/// <summary>
/// Reads the installer script and its message files as text from the source tree (found from
/// this file's path; <c>installer\</c> is not copied to the build output). There is no Inno
/// Setup parser here: matching lines is enough.
/// </summary>
public class InstallerTests {
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static TheoryData<string> Languages => new(LanguageCodes());

    [Theory]
    [MemberData(nameof(Languages))]
    public void CustomMessages_HaveTheSameKeysAsEnglish(string code) {
        var english = CustomMessageKeys("en");

        CustomMessageKeys(code).Should().BeEquivalentTo(english,
            "Custom.{0}.isl must define every message of Custom.en.isl, and no other", code);
    }

    [Fact]
    public void Script_StartsWithAUtf8Bom() {
        StartsWithBom(Path.Combine(InstallerFolder(), "plancake.iss")).Should().BeTrue(
            "ISCC reads a script without a BOM in the ANSI code page");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void CustomMessages_StartWithAUtf8Bom(string code) {
        StartsWithBom(IslPath(code)).Should().BeTrue(
            "Inno Setup reads Custom.{0}.isl as UTF-8 only with a BOM", code);
    }

    [Fact]
    public void RegisteredAppName_MatchesDefaultApps() {
        Define("RegisteredAppName").Should().Be(DefaultApps.RegisteredName);
    }

    [Fact]
    public void Registry_ListsPlanCakeUnderRegisteredApplications() {
        RegistryLines().Should().Contain(line =>
            line.Contains(@"Subkey: ""Software\RegisteredApplications""", StringComparison.Ordinal)
            && line.Contains(@"ValueName: ""{#RegisteredAppName}""", StringComparison.Ordinal)
            && line.Contains(@"ValueData: ""{#CapabilitiesKey}""", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public void Registry_AddsTheFileTypeToOpenWithProgids(string extension) {
        RegistryLines().Should().Contain(line =>
            line.Contains($@"Subkey: ""Software\Classes\{extension}\OpenWithProgids""", StringComparison.Ordinal)
            && line.Contains(@"ValueName: ""{#AppProgId}""", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_NeverWritesTheExtensionsDefaultValue() {
        RegistryLines()
            .Where(line => line.Contains(@"Subkey: ""Software\Classes\.md""", StringComparison.Ordinal)
                || line.Contains(@"Subkey: ""Software\Classes\.markdown""", StringComparison.Ordinal))
            .Should().OnlyContain(line => !line.Contains("ValueType:", StringComparison.Ordinal)
                && !line.Contains("ValueName:", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_DefinesNoVerbButOpen() {
        RegistryLines()
            .Where(line => line.Contains(@"\shell\", StringComparison.Ordinal))
            .Should().OnlyContain(line => line.Contains(@"\shell\open\command""", StringComparison.Ordinal),
                "File > Open in editor looks for an edit verb and must not find PlanCake");
    }

    [Fact]
    public void Registry_QuotesTheFileArgumentInBothOpenCommands() {
        var commands = RegistryLines()
            .Where(line => line.Contains(@"\shell\open\command""", StringComparison.Ordinal))
            .ToList();

        commands.Should().HaveCount(2);
        commands.Should().OnlyContain(line =>
            line.Contains(@"ValueData: """"""{app}\{#MyAppExeName}"""" """"%1""""""", StringComparison.Ordinal));
    }

    private static string InstallerFolder([CallerFilePath] string testFile = "") {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(testFile)))!;
        return Path.Combine(root, "installer");
    }

    private static string IslPath(string code) =>
        Path.Combine(InstallerFolder(), "Languages", $"Custom.{code}.isl");

    private static IEnumerable<string> LanguageCodes() =>
        Directory.GetFiles(Path.Combine(InstallerFolder(), "Languages"), "Custom.*.isl")
            .Select(path => Path.GetFileNameWithoutExtension(path)["Custom.".Length..])
            .Order(StringComparer.Ordinal);

    private static bool StartsWithBom(string path) {
        var bytes = File.ReadAllBytes(path);
        return bytes.AsSpan().StartsWith(Utf8Bom);
    }

    /// <summary>The keys of the <c>[CustomMessages]</c> section of a <c>Custom.&lt;code&gt;.isl</c> file.</summary>
    private static List<string> CustomMessageKeys(string code) =>
        SectionLines(File.ReadLines(IslPath(code)), "CustomMessages")
            .Where(line => line.Contains('=', StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf('=', StringComparison.Ordinal)].Trim())
            .ToList();

    private static List<string> RegistryLines() =>
        SectionLines(File.ReadLines(Path.Combine(InstallerFolder(), "plancake.iss")), "Registry").ToList();

    /// <summary>The value of a <c>#define Name "value"</c> line in <c>plancake.iss</c>.</summary>
    private static string? Define(string name) {
        var prefix = $"#define {name} ";

        return File.ReadLines(Path.Combine(InstallerFolder(), "plancake.iss"))
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Trim().Trim('"'))
            .FirstOrDefault();
    }

    /// <summary>
    /// The lines of one <c>[Section]</c>, without blank lines and <c>;</c> comments. A section ends
    /// at the next section header or preprocessor line.
    /// </summary>
    private static IEnumerable<string> SectionLines(IEnumerable<string> lines, string section) {
        var inside = false;

        foreach (var raw in lines) {
            var line = raw.TrimStart('﻿').Trim();

            if (line.StartsWith('[') || line.StartsWith('#')) {
                inside = line.Equals($"[{section}]", StringComparison.Ordinal);
                continue;
            }

            if (inside && line.Length > 0 && !line.StartsWith(';')) {
                yield return line;
            }
        }
    }
}
