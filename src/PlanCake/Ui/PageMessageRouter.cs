using System.Text.Json;
using Oire.PlanCake.Rendering;
using Oire.PlanCake.Utils.Enums;

namespace Oire.PlanCake.Ui;

/// <summary>
/// The block or note a page action is about, with the render it came from: the text that render
/// was made from is what the note store checks the file against.
/// </summary>
/// <param name="Generation">The render's number.</param>
/// <param name="RenderedText">The file text the render was made from.</param>
/// <param name="Block">The block, or the block the note is on; <see langword="null"/> for a note at the top.</param>
/// <param name="Note">The note, when the action is about one.</param>
internal sealed record NoteTarget(int Generation, string RenderedText, BlockInfo? Block, RenderedNote? Note);

/// <summary>What the page shows, as <see cref="PageMessageRouter"/> needs it to read a message.</summary>
/// <param name="Render">
/// The render the page shows (or is about to show); <see langword="null"/> before the first.
/// </param>
/// <param name="Generation">The number of <paramref name="Render"/>.</param>
/// <param name="RenderedText">The file text <paramref name="Render"/> was made from.</param>
/// <param name="BlockEnterAction">What Enter (or a click) on a block does, from Settings.</param>
internal sealed record PageState(
    RenderResult? Render,
    int Generation,
    string RenderedText,
    BlockEnterAction BlockEnterAction
);

/// <summary>
/// What the window is to do about a page message (<see cref="PageMessageRouter.Route"/>). The
/// window does the work itself: dialogs and menus, deferred with <c>BeginInvoke</c>, since the
/// message arrives inside a WebView2 event.
/// </summary>
internal abstract record PageIntent {
    /// <summary>The page has loaded: send it the render and the strings.</summary>
    public sealed record Ready: PageIntent;

    /// <summary>
    /// The page reports where the user is. With a <paramref name="Note"/>, the document is on that
    /// note and on the block it follows; without one, on <paramref name="Block"/>, or on a block
    /// the render does not know when that is <see langword="null"/> (the last known block stays).
    /// </summary>
    public sealed record Position(RenderedNote? Note, BlockInfo? Block): PageIntent;

    /// <summary>Enter or a click on a block: ask for a note and write it after the block.</summary>
    public sealed record AddNote(NoteTarget Target, BlockInfo Block): PageIntent;

    /// <summary>Enter or a click on a note: edit its text.</summary>
    public sealed record EditNote(NoteTarget Target, RenderedNote Note): PageIntent;

    /// <summary>
    /// The context menu of a block or a note, at <paramref name="Rect"/> (CSS pixels, when the
    /// page gave it) times <paramref name="Scale"/> (the page's <c>devicePixelRatio</c>).
    /// </summary>
    public sealed record ShowContextMenu(NoteTarget Target, RectangleF? Rect, double Scale): PageIntent;

    /// <summary>A task-list check box was set to <paramref name="Checked"/>: write it to the file.</summary>
    public sealed record ToggleTask(NoteTarget Target, BlockInfo Item, bool Checked): PageIntent;

    /// <summary>
    /// A check box was toggled in a render that has since been replaced: nothing is written, and
    /// the user is told to try again.
    /// </summary>
    public sealed record TaskToggleDropped: PageIntent;

    /// <summary>A link was followed; <paramref name="Href"/> is the raw attribute value.</summary>
    public sealed record OpenLink(string Href): PageIntent;

    /// <summary>Backspace in the page: back to the previous file.</summary>
    public sealed record GoBack: PageIntent;

    /// <summary>The page found no note in the direction asked for.</summary>
    public sealed record NoMoreNotes: PageIntent;

    /// <summary>The page found no block in the direction asked for.</summary>
    public sealed record NoMoreBlocks: PageIntent;

    /// <summary>Files were dropped on the page.</summary>
    public sealed record DropFiles(IReadOnlyList<string> Files): PageIntent;

    /// <summary>
    /// Nothing to do: the message is about an older render, names a block or note the render does
    /// not have, or lacks what the action needs. A <paramref name="Target"/> found in the current
    /// render still becomes the current block or note, as it does for every action.
    /// </summary>
    public sealed record Ignored(NoteTarget? Target = null): PageIntent;

    /// <summary>A message type the host does not know.</summary>
    public sealed record Unknown(string Type): PageIntent;
}

/// <summary>
/// Reads a page message against the render the page shows and decides what the window does
/// about it, without touching the window: which block or note it is about, whether it belongs to
/// the current render (<see cref="PageState.Generation"/>), and so whether it may lead to a write.
/// </summary>
internal static class PageMessageRouter {
    /// <summary>What the window does about the message <paramref name="type"/>.</summary>
    /// <param name="type">The message's <c>type</c>.</param>
    /// <param name="message">The whole message.</param>
    /// <param name="files">The files the page passed along with it (a drop).</param>
    /// <param name="state">The render the page shows.</param>
    public static PageIntent Route(string type, JsonElement message, IReadOnlyList<string> files, PageState state) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(state);

        switch (type) {
            case PageMessages.Ready:
                return new PageIntent.Ready();
            case PageMessages.Position:
                return RoutePosition(message, state);
            case PageMessages.Activate:
                if (FindTarget(message, state) is not { } target) {
                    return new PageIntent.Ignored();
                }

                if (target.Block is not { } block) {
                    return new PageIntent.Ignored(target);
                }

                // The setting makes Enter (and a click) on a block open its menu instead.
                return state.BlockEnterAction == BlockEnterAction.ContextMenu
                    ? MenuIntent(target, message)
                    : new PageIntent.AddNote(target, block);
            case PageMessages.ActivateNote:
                return FindTarget(message, state) switch {
                    { Note: { } note } noteTarget => new PageIntent.EditNote(noteTarget, note),
                    var other => new PageIntent.Ignored(other),
                };
            case PageMessages.ContextMenu:
                return FindTarget(message, state) is { } menuTarget
                    ? MenuIntent(menuTarget, message)
                    : new PageIntent.Ignored();
            case PageMessages.ToggleTask:
                return RouteToggleTask(message, state);
            case PageMessages.OpenLink:
                return new PageIntent.OpenLink(PageMessages.GetString(message, "href") ?? String.Empty);
            case PageMessages.GoBack:
                return new PageIntent.GoBack();
            case PageMessages.NoMoreNotes:
                return new PageIntent.NoMoreNotes();
            case PageMessages.NoMoreBlocks:
                return new PageIntent.NoMoreBlocks();
            case PageMessages.DropFiles:
                return new PageIntent.DropFiles(files);
            default:
                return new PageIntent.Unknown(type);
        }
    }

    /// <summary>True when <paramref name="message"/> belongs to the render the page shows now.</summary>
    public static bool IsCurrentRender(JsonElement message, PageState state) {
        ArgumentNullException.ThrowIfNull(state);

        return state.Render is not null && PageMessages.GetInt(message, "generation") == state.Generation;
    }

    /// <summary>
    /// The block or note a page message is about (<c>note</c> wins over <c>lines</c>), from the
    /// current render; <see langword="null"/> for an older render or an unknown element.
    /// </summary>
    public static NoteTarget? FindTarget(JsonElement message, PageState state) {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Render is not { } render || !IsCurrentRender(message, state)) {
            return null;
        }

        if (PageMessages.GetInt(message, "note") is not null) {
            return PageMessages.FindNote(message, render.Notes) is { } note
                ? new NoteTarget(state.Generation, state.RenderedText, note.Block, note)
                : null;
        }

        if (PageMessages.GetString(message, "lines") is { } lines
            && render.Blocks.FirstOrDefault(block => block.Lines == lines) is { } found) {
            return new NoteTarget(state.Generation, state.RenderedText, found, null);
        }

        return null;
    }

    /// <summary>
    /// The block or note <paramref name="intent"/> is about, which becomes the current one in the
    /// window; <see langword="null"/> for an intent about none.
    /// </summary>
    public static NoteTarget? TargetOf(PageIntent intent) => intent switch {
        PageIntent.AddNote add => add.Target,
        PageIntent.EditNote edit => edit.Target,
        PageIntent.ShowContextMenu menu => menu.Target,
        PageIntent.ToggleTask toggle => toggle.Target,
        PageIntent.Ignored ignored => ignored.Target,
        _ => null,
    };

    /// <summary>
    /// Wraps a deferred action about <paramref name="target"/> so that it does nothing once the
    /// render the target came from has been replaced.
    /// </summary>
    /// <param name="target">The block or note the action is about.</param>
    /// <param name="currentGeneration">The number of the render the page shows when the action runs.</param>
    /// <param name="action">The action.</param>
    public static Action WhileCurrent(NoteTarget target, Func<int> currentGeneration, Action action) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(currentGeneration);
        ArgumentNullException.ThrowIfNull(action);

        return () => {
            if (target.Generation == currentGeneration()) {
                action();
            }
        };
    }

    private static PageIntent RoutePosition(JsonElement message, PageState state) {
        if (state.Render is not { } render || !IsCurrentRender(message, state)) {
            return new PageIntent.Ignored();
        }

        if (PageMessages.GetInt(message, "note") is not null) {
            return PageMessages.FindNote(message, render.Notes) is { } note
                ? new PageIntent.Position(note, null)
                : new PageIntent.Ignored();
        }

        if (PageMessages.GetString(message, "lines") is { } lines) {
            return new PageIntent.Position(null, render.Blocks.FirstOrDefault(block => block.Lines == lines));
        }

        return new PageIntent.Ignored();
    }

    private static PageIntent RouteToggleTask(JsonElement message, PageState state) {
        if (PageMessages.GetBool(message, "checked") is not { } isChecked) {
            return new PageIntent.Ignored();
        }

        var target = FindTarget(message, state);

        if (target is { Block: { } item, Note: null }) {
            return new PageIntent.ToggleTask(target, item, isChecked);
        }

        // The file changed since the page showed the check box: the new render, on its way to the
        // page, shows the file's state.
        return state.Render is not null && !IsCurrentRender(message, state)
            ? new PageIntent.TaskToggleDropped()
            : new PageIntent.Ignored(target);
    }

    private static PageIntent.ShowContextMenu MenuIntent(NoteTarget target, JsonElement message) => new(
        target,
        PageMessages.GetRect(message, "rect"),
        PageMessages.GetDouble(message, "scale") ?? 1
    );
}
