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
}
