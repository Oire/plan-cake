namespace Oire.PlanCake.Utils;

/// <summary>What File → Open from clipboard does with what the clipboard holds.</summary>
internal enum ClipboardContentKind {
    /// <summary>A local Markdown file, opened in PlanCake.</summary>
    MarkdownFile,

    /// <summary>An http(s) link, downloaded and then opened.</summary>
    Link,

    /// <summary>Copied files, none of them a Markdown file.</summary>
    NoMarkdownFile,

    /// <summary>Nothing PlanCake can open: other text, or an empty clipboard.</summary>
    Nothing,
}

/// <summary>What the clipboard holds, as far as opening a file goes.</summary>
/// <param name="Kind">What opening it does.</param>
/// <param name="Target">
/// The full path (<see cref="ClipboardContentKind.MarkdownFile"/>), the trimmed URL
/// (<see cref="ClipboardContentKind.Link"/>), or an empty string.
/// </param>
internal sealed record ClipboardContent(ClipboardContentKind Kind, string Target) {
    public static ClipboardContent Nothing { get; } = new(ClipboardContentKind.Nothing, String.Empty);
}

/// <summary>
/// Decides what File → Open from clipboard opens, from the clipboard's file drop list and text,
/// so the decision is tested without a real clipboard. Files copied in Explorer come first: the
/// first Markdown file among them. Then text holding a full path to a Markdown file (Explorer's
/// "Copy as path" adds quotes, which are stripped), then text holding an http(s) link.
/// </summary>
internal static class ClipboardClassifier {
    /// <param name="dropList">The files copied in Explorer, or <see langword="null"/> when there are none.</param>
    /// <param name="text">The clipboard's text, or <see langword="null"/> when it holds none.</param>
    public static ClipboardContent Classify(IReadOnlyList<string>? dropList, string? text) {
        if (dropList is { Count: > 0 }) {
            if (dropList.FirstOrDefault(LinkResolver.IsMarkdownPath) is { } file) {
                return new ClipboardContent(ClipboardContentKind.MarkdownFile, file);
            }

            // Explorer puts no text on the clipboard with copied files, but another program
            // might: a usable text still wins over "no Markdown file".
            var fromText = ClassifyText(text);

            return fromText.Kind == ClipboardContentKind.Nothing
                ? new ClipboardContent(ClipboardContentKind.NoMarkdownFile, String.Empty)
                : fromText;
        }

        return ClassifyText(text);
    }

    private static ClipboardContent ClassifyText(string? text) {
        var trimmed = text?.Trim() ?? String.Empty;

        if (trimmed.Length == 0) {
            return ClipboardContent.Nothing;
        }

        if (UrlHelper.IsValidHttpUrl(trimmed, out var url)) {
            return new ClipboardContent(ClipboardContentKind.Link, url);
        }

        return LocalMarkdownPath(Unquote(trimmed)) is { } path
            ? new ClipboardContent(ClipboardContentKind.MarkdownFile, path)
            : ClipboardContent.Nothing;
    }

    /// <summary>Strips one pair of surrounding double quotes, as Explorer's "Copy as path" adds.</summary>
    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1].Trim() : text;

    /// <summary>
    /// The full path when <paramref name="text"/> is one path to a Markdown file (a fully
    /// qualified path or a <c>file:</c> URI), else <see langword="null"/>. Whether it exists is
    /// left to opening it, which says so when it does not.
    /// </summary>
    private static string? LocalMarkdownPath(string text) {
        if (text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)) {
            return null;
        }

        string path;

        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.IsFile) {
                return null;
            }

            path = uri.LocalPath;
        } else {
            path = text;
        }

        try {
            return Path.IsPathFullyQualified(path) && LinkResolver.IsMarkdownPath(path)
                ? Path.GetFullPath(path)
                : null;
        } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return null;
        }
    }
}
