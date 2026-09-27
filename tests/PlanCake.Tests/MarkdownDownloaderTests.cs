using System.Net;
using System.Text;
using AwesomeAssertions;
using Oire.PlanCake.Services;
using Xunit;

namespace Oire.PlanCake.Tests;

public sealed class MarkdownDownloaderTests: IDisposable {
    private const string Plan = "# Plan\n\n- [ ] first task\n";

    private readonly string _downloads = Path.Combine(Path.GetTempPath(), "PlanCakeTests", Guid.NewGuid().ToString("N"));

    public void Dispose() {
        if (Directory.Exists(_downloads)) {
            Directory.Delete(_downloads, recursive: true);
        }
    }

    /// <summary>Answers every request from <c>respond</c>, recording what was asked; never touches the network.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond): HttpMessageHandler {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);

            return Task.FromResult(respond(request));
        }
    }

    /// <summary>A body that reports no length and goes on longer than the limit.</summary>
    private sealed class EndlessContent: HttpContent {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) {
            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'a');

            for (long written = 0; written <= MarkdownDownloader.MaxSize + chunk.Length; written += chunk.Length) {
                await stream.WriteAsync(chunk);
            }
        }

        protected override bool TryComputeLength(out long length) {
            length = 0;
            return false;
        }
    }

    /// <summary>Answers only once the request is canceled: a server that never sends anything.</summary>
    private sealed class HangingHandler: HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);

            throw new InvalidOperationException("Not reached.");
        }
    }

    /// <summary>A body whose stream fails when read, as a dropped connection or a corrupt gzip body does.</summary>
    private sealed class FailingContent(Exception failure): HttpContent {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw failure;

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new FailingStream(failure));

        protected override bool TryComputeLength(out long length) {
            length = 0;
            return false;
        }
    }

    private sealed class FailingStream(Exception failure): Stream {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw failure;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Text(string body, string mediaType = "text/plain") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, new UTF8Encoding(false), mediaType) };

    private (MarkdownDownloader Downloader, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond) {
        var handler = new FakeHandler(respond);

        return (new MarkdownDownloader(handler, () => _downloads), handler);
    }

    [Fact]
    public async Task Download_SavesTheFileInTheDownloadsFolder() {
        var (downloader, handler) = Create(_ => Text(Plan));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plans/next-plan.md");

        result.Failure.Should().BeNull();
        result.FilePath.Should().Be(Path.Combine(_downloads, "next-plan.md"));
        File.ReadAllText(result.FilePath!).Should().Be(Plan);
        handler.Requests.Should().Equal(new Uri("https://example.com/plans/next-plan.md"));
    }

    [Fact]
    public async Task Download_OfAGitHubBlobLink_FetchesTheRawFile() {
        var (downloader, handler) = Create(_ => Text(Plan));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://github.com/Oire/plan-cake/blob/master/docs/plan.md");

        handler.Requests.Should().Equal(new Uri("https://raw.githubusercontent.com/Oire/plan-cake/master/docs/plan.md"));
        result.FilePath.Should().Be(Path.Combine(_downloads, "plan.md"));
    }

    [Theory]
    [InlineData(
        "https://github.com/Oire/plan-cake/blob/master/README.md",
        "https://raw.githubusercontent.com/Oire/plan-cake/master/README.md"
    )]
    [InlineData(
        "https://www.github.com/Oire/plan-cake/blob/v1.0.0/docs/plans/001-plan-cake-v1.md?plain=1#L10",
        "https://raw.githubusercontent.com/Oire/plan-cake/v1.0.0/docs/plans/001-plan-cake-v1.md"
    )]
    [InlineData(
        "https://github.com/Oire/plan-cake/blob/feature/links/my%20plan.md",
        "https://raw.githubusercontent.com/Oire/plan-cake/feature/links/my%20plan.md"
    )]
    public void RewriteGitHubBlob_GivesTheRawLink(string link, string expected) =>
        MarkdownDownloader.RewriteGitHubBlob(new Uri(link)).AbsoluteUri.Should().Be(expected);

    [Theory]
    [InlineData("https://raw.githubusercontent.com/Oire/plan-cake/master/README.md")]
    [InlineData("https://github.com/Oire/plan-cake")]
    [InlineData("https://github.com/Oire/plan-cake/tree/master/docs")]
    [InlineData("https://github.com/Oire/plan-cake/blob/master")]
    [InlineData("https://gitlab.com/Oire/plan-cake/blob/master/README.md")]
    [InlineData("https://example.com/Oire/plan-cake/blob/master/README.md")]
    public void RewriteGitHubBlob_LeavesOtherLinksAlone(string link) =>
        MarkdownDownloader.RewriteGitHubBlob(new Uri(link)).Should().Be(new Uri(link));

    [Fact]
    public async Task Download_404_IsRefused() {
        var (downloader, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/missing.md");

        result.Failure.Should().Be(DownloadFailure.HttpStatus);
        result.FilePath.Should().BeNull();
        result.Error.Should().Contain("404");
        Directory.Exists(_downloads).Should().BeFalse();
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/xhtml+xml")]
    public async Task Download_HtmlResponse_IsRefused(string mediaType) {
        var (downloader, _) = Create(_ => Text("<p>Sign in</p>", mediaType));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.WebPage);
        result.FilePath.Should().BeNull();
        Directory.Exists(_downloads).Should().BeFalse();
    }

    [Theory]
    [InlineData("<!DOCTYPE html>\n<html><body>Hello</body></html>")]
    [InlineData("﻿\n  <html lang=\"en\"><body>Hello</body></html>")]
    public async Task Download_HtmlServedAsText_IsRefused(string body) {
        var (downloader, _) = Create(_ => Text(body));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.WebPage);
    }

    [Fact]
    public async Task Download_MarkdownMentioningHtml_IsKept() {
        var (downloader, _) = Create(_ => Text("# Plan\n\n<html> is a tag.\n"));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().BeNull();
    }

    [Fact]
    public async Task Download_DeclaredOversized_IsRefused() {
        var (downloader, _) = Create(_ => {
            var response = Text("small");
            response.Content.Headers.ContentLength = MarkdownDownloader.MaxSize + 1;
            return response;
        });
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/huge.md");

        result.Failure.Should().Be(DownloadFailure.TooLarge);
        Directory.Exists(_downloads).Should().BeFalse();
    }

    [Fact]
    public async Task Download_OversizedWithoutLength_IsRefused() {
        var (downloader, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new EndlessContent() });
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/huge.md");

        result.Failure.Should().Be(DownloadFailure.TooLarge);
        Directory.Exists(_downloads).Should().BeFalse();
    }

    [Fact]
    public async Task Download_NetworkError_IsReported() {
        var (downloader, _) = Create(_ => throw new HttpRequestException("No such host is known."));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://nowhere.invalid/plan.md");

        result.Failure.Should().Be(DownloadFailure.Network);
        result.Error.Should().Contain("No such host is known.");
    }

    [Fact]
    public async Task Download_ConnectionDroppedDuringTheBody_IsANetworkFailure() {
        var (downloader, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new FailingContent(new IOException("The response ended prematurely.")),
        });
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.Network);
        result.Error.Should().Contain("ended prematurely");
        Directory.Exists(_downloads).Should().BeFalse();
    }

    [Fact]
    public async Task Download_CorruptCompressedBody_IsANetworkFailure() {
        var (downloader, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new FailingContent(new InvalidDataException("The archive entry was compressed using an unsupported compression method.")),
        });
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.Network);
    }

    [Fact]
    public async Task Download_SlowerThanTheTimeout_IsStopped() {
        using var downloader = new MarkdownDownloader(new HangingHandler(), () => _downloads, TimeSpan.FromMilliseconds(50));

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.Timeout);
        Directory.Exists(_downloads).Should().BeFalse();
    }

    [Fact]
    public async Task Download_ToAFolderThatCannotBeCreated_IsASaveFailure() {
        Directory.CreateDirectory(_downloads);
        var blocker = Path.Combine(_downloads, "a file");
        File.WriteAllText(blocker, "in the way");
        using var downloader = new MarkdownDownloader(new FakeHandler(_ => Text(Plan)), () => Path.Combine(blocker, "Downloads"));

        var result = await downloader.DownloadAsync("https://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.Save);
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task Download_OfAProgramLink_IsSavedAsMarkdownAndMarkedAsFromTheInternet() {
        var (downloader, _) = Create(_ => Text("@echo off\n"));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/tools/setup.bat");

        result.FilePath.Should().Be(Path.Combine(_downloads, "setup.bat.md"));
        var zone = File.ReadAllText(result.FilePath + ":Zone.Identifier");
        zone.Should().Be(
            "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/tools/setup.bat\r\n"
            + "HostUrl=https://example.com/tools/setup.bat\r\n"
        );
    }

    [Fact]
    public async Task Download_InvalidLink_IsRefusedWithoutARequest() {
        var (downloader, handler) = Create(_ => Text(Plan));
        using var d = downloader;

        var result = await downloader.DownloadAsync("ftp://example.com/plan.md");

        result.Failure.Should().Be(DownloadFailure.InvalidLink);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_Cancelled_Throws() {
        var (downloader, _) = Create(_ => Text(Plan));
        using var d = downloader;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var download = () => downloader.DownloadAsync("https://example.com/plan.md", cancellation.Token);

        await download.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Download_WithoutExtension_SavesAsMarkdown() {
        var (downloader, _) = Create(_ => Text(Plan));
        using var d = downloader;

        var result = await downloader.DownloadAsync("https://example.com/plans/README");

        result.FilePath.Should().Be(Path.Combine(_downloads, "README.md"));
    }

    [Fact]
    public async Task Download_TakenName_GetsANumber() {
        Directory.CreateDirectory(_downloads);
        File.WriteAllText(Path.Combine(_downloads, "plan.md"), "mine");
        var (downloader, _) = Create(_ => Text(Plan));
        using var d = downloader;

        var second = await downloader.DownloadAsync("https://example.com/plan.md");
        var third = await downloader.DownloadAsync("https://example.com/plan.md");

        second.FilePath.Should().Be(Path.Combine(_downloads, "plan (2).md"));
        third.FilePath.Should().Be(Path.Combine(_downloads, "plan (3).md"));
        File.ReadAllText(Path.Combine(_downloads, "plan.md")).Should().Be("mine");
        File.ReadAllText(second.FilePath!).Should().Be(Plan);
    }

    [Theory]
    [InlineData("https://example.com/plans/next.md", "next.md")]
    [InlineData("https://example.com/plans/next.markdown?x=1#top", "next.markdown")]
    [InlineData("https://example.com/plans/my%20plan.md", "my plan.md")]
    [InlineData("https://example.com/plans/README", "README.md")]
    [InlineData("https://example.com/plans/notes.txt", "notes.txt.md")]
    [InlineData("https://example.com/tool.bat", "tool.bat.md")]
    [InlineData("https://example.com/x.hta", "x.hta.md")]
    [InlineData("https://example.com/run.js", "run.js.md")]
    [InlineData("https://example.com/link.lnk", "link.lnk.md")]
    [InlineData("https://example.com/PLAN.MD", "PLAN.MD")]
    [InlineData("https://example.com/plans/", "plans.md")]
    [InlineData("https://example.com/", "download.md")]
    [InlineData("https://example.com/a%3Ab%3F.md", "a_b_.md")]
    public void FileNameFor_UsesTheLastSegment(string link, string expected) =>
        MarkdownDownloader.FileNameFor(new Uri(link)).Should().Be(expected);
}
