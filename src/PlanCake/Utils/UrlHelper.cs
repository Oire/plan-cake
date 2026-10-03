namespace Oire.PlanCake.Utils;

/// <summary>The one rule PlanCake applies to a web link, wherever it comes from.</summary>
internal static class UrlHelper {
    /// <summary>
    /// Trims <paramref name="text"/> and reports whether it is an absolute http(s) URL: the rule
    /// for File → Open from link and for a link on the clipboard. The trimmed text is returned in
    /// <paramref name="url"/> whether it passed or not.
    /// </summary>
    public static bool IsValidHttpUrl(string? text, out string url) {
        url = text?.Trim() ?? String.Empty;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !String.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    /// <paramref name="uri"/> as it may be written to a log, which a user pastes into a bug
    /// report: without the user name and password, the query keeping its keys only, and without
    /// the fragment. A private link carries its access token in the query (<c>?token=…</c>), and
    /// the log has no use for it. Never use the result to reach the address.
    /// </summary>
    public static string ForLog(Uri? uri) {
        if (uri is null) {
            return String.Empty;
        }

        if (!uri.IsAbsoluteUri || uri.IsFile || uri.IsUnc
            || !uri.OriginalString.Contains("://", StringComparison.Ordinal)) {
            return ForLog(uri.OriginalString);
        }

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            + QueryKeys(uri.Query.TrimStart('?'));
    }

    /// <summary>
    /// <paramref name="text"/>, a link or path as the user or the plan wrote it, as it may be
    /// written to a log: see <see cref="ForLog(Uri)"/>. Text that is not an absolute web address
    /// (a relative link, a path, <c>mailto:</c>) keeps everything but the values in its query.
    /// </summary>
    public static string ForLog(string? text) {
        if (String.IsNullOrEmpty(text)) {
            return String.Empty;
        }

        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            && !uri.IsFile && !uri.IsUnc && text.Contains("://", StringComparison.Ordinal)) {
            return ForLog(uri);
        }

        var query = text.IndexOf('?', StringComparison.Ordinal);

        if (query < 0) {
            return text;
        }

        var fragment = text.IndexOf('#', query);
        var end = fragment < 0 ? text.Length : fragment;

        return text[..query] + QueryKeys(text[(query + 1)..end]) + (fragment < 0 ? String.Empty : text[fragment..]);
    }

    /// <summary>A query string (without its <c>?</c>) reduced to its keys: <c>?a&amp;token</c>.</summary>
    private static string QueryKeys(string query) {
        var keys = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .Where(key => key.Length > 0)
            .ToList();

        return keys.Count == 0 ? String.Empty : "?" + String.Join('&', keys);
    }
}
