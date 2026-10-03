using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Notes;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Ui;
using Xunit;

namespace Oire.PlanCake.Tests;

public class PreparedRenderTests: IDisposable {
    private static readonly Encoding _windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;

    private static readonly RenderOptions _renderOptions =
        new(NoteMarkers.Default, RenderMode.Interactive, new RenderStrings("User note"));

    private static readonly MarkdownFileOptions _converting =
        new(ConvertToUtf8: true, AnsiEncoding: _windows1251, RetryDelay: TimeSpan.FromMilliseconds(20));

    private readonly string _folder;

    public PreparedRenderTests() {
        _folder = Path.Combine(Path.GetTempPath(), $"PlanCake.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() {
        if (Directory.Exists(_folder)) {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string WriteLegacyFile(out byte[] bytes) {
        var path = Path.Combine(_folder, "plan.md");
        bytes = _windows1251.GetBytes("# План\n\nТекст\n");
        File.WriteAllBytes(path, bytes);

        return path;
    }

    [Fact]
    public void Prepare_LegacyFileWithConversionOn_RendersItWithoutWritingIt() {
        var path = WriteLegacyFile(out var bytes);

        var prepared = PreparedRender.Prepare(path, _converting, _renderOptions, CancellationToken.None);

        prepared.Text.Should().Be("# План\n\nТекст\n");
        prepared.Result.Blocks.Should().NotBeEmpty();
        prepared.File.NeedsConversion.Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public void Prepare_Canceled_ThrowsAndLeavesTheFileAlone() {
        var path = WriteLegacyFile(out var bytes);

        var prepare = () => PreparedRender.Prepare(path, _converting, _renderOptions, new CancellationToken(canceled: true));

        prepare.Should().Throw<OperationCanceledException>();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }
}
