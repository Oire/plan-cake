namespace Oire.PlanCake.Ui;

/// <summary>
/// The navigation rule of <see cref="DocumentView"/>: the one navigation the view may make is
/// the page the host itself asked for, once. Every other navigation (a link, a form, a
/// <c>meta refresh</c> in a plan's raw HTML, the same page started again by the plan) is refused.
/// </summary>
internal sealed class NavigationGate {
    private string? _allowed;

    /// <summary>Lets the next navigation to <paramref name="uri"/> through, instead of any allowed before.</summary>
    public void Allow(string uri) {
        ArgumentNullException.ThrowIfNull(uri);
        _allowed = uri;
    }

    /// <summary>
    /// True when a navigation to <paramref name="uri"/> may go ahead: it is the one allowed, which
    /// is then used up. False for anything else, which the view cancels.
    /// </summary>
    public bool TryPass(string? uri) {
        if (_allowed is null || !String.Equals(uri, _allowed, StringComparison.Ordinal)) {
            return false;
        }

        _allowed = null;

        return true;
    }
}
