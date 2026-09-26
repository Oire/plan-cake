using System.Text.Json;
using System.Text.Json.Serialization;
using Oire.PlanCake.Rendering;

namespace Oire.PlanCake.Ui;

/// <summary>
/// Where the page puts the virtual cursor after a render: on a block (by its <c>data-lines</c>)
/// or on a note (by its <c>data-note</c> index). Neither means the top of the document. With
/// <see cref="Task"/>, the focus goes to the task-list check box inside the block instead of
/// the block itself, so it stays on the check box the user just toggled.
/// </summary>
internal sealed record PageFocus(string? Lines = null, int? Note = null, bool? Task = null);

/// <summary>Host → page: shows a rendered document in the page's <c>main</c> element.</summary>
/// <param name="Html">The document body the renderer produced.</param>
/// <param name="Generation">The render's number; the page sends it back with every action.</param>
/// <param name="DocumentLang">The <c>lang</c> of the document (not of the interface).</param>
/// <param name="Title">The page's <c>&lt;title&gt;</c>: the first heading, else the file name.</param>
/// <param name="Focus">Where to put the virtual cursor, or <see langword="null"/> for the top.</param>
internal sealed record RenderMessage(string Html, int Generation, string DocumentLang, string Title, PageFocus? Focus) {
    public string Type { get; } = "render";
}

/// <summary>
/// Host → page: the interface language and direction of the page chrome, and every string the
/// page shows itself, so that <c>app.js</c> holds no user-visible literal.
/// </summary>
/// <param name="UiLang">The interface language, for <c>&lt;html lang&gt;</c>.</param>
/// <param name="UiDir"><c>ltr</c> or <c>rtl</c>, for <c>&lt;html dir&gt;</c>.</param>
/// <param name="NoDocument">What the page shows while no file is open.</param>
internal sealed record StringsMessage(string UiLang, string UiDir, string NoDocument) {
    public string Type { get; } = "strings";
}

/// <summary>Host → page: moves the virtual cursor to the block with these <c>data-lines</c>.</summary>
internal sealed record FocusLinesMessage(string Lines) {
    public string Type { get; } = "focusLines";
}

/// <summary>Host → page: moves the virtual cursor to the note with this <c>data-note</c> index.</summary>
internal sealed record FocusNoteMessage(int Note) {
    public string Type { get; } = "focusNote";
}

/// <summary>
/// Host → page: sets the task-list check box of the block with these <c>data-lines</c> to the
/// file's state, after a toggle was canceled or could not be written.
/// </summary>
internal sealed record TaskStateMessage(string Lines, bool Checked) {
    public string Type { get; } = "taskState";
}

/// <summary>Host → page: moves to the next note after the current position.</summary>
internal sealed record NextNoteMessage {
    public string Type { get; } = "nextNote";
}

/// <summary>Host → page: moves to the previous note before the current position.</summary>
internal sealed record PreviousNoteMessage {
    public string Type { get; } = "previousNote";
}

/// <summary>
/// Host → page: moves to the next block or note after the current position (Alt+Shift+Down Arrow),
/// so that a keyboard user without a screen reader can reach any block to annotate it.
/// </summary>
internal sealed record NextBlockMessage {
    public string Type { get; } = "nextBlock";
}

/// <summary>
/// Host → page: moves to the previous block or note before the current position (Alt+Shift+Up Arrow).
/// </summary>
internal sealed record PreviousBlockMessage {
    public string Type { get; } = "previousBlock";
}

/// <summary>
/// The page protocol of Technical details → "Page protocol" in the PlanCake plan: the names of
/// the messages the page sends, reading their properties, and the JSON the host sends.
/// </summary>
internal static class PageMessages {
    /// <summary>The page has loaded and can take messages.</summary>
    public const string Ready = "ready";

    /// <summary>
    /// Enter or a click on a block: <c>{ lines, rect, scale, generation }</c>, <c>rect</c> and
    /// <c>scale</c> as in <see cref="ContextMenu"/>, for the block's menu when the setting makes
    /// Enter open it.
    /// </summary>
    public const string Activate = "activate";

    /// <summary>Enter or a click on a note: <c>{ note, generation }</c>.</summary>
    public const string ActivateNote = "activateNote";

    /// <summary>
    /// The Applications key, Shift+F10 or a right-click: <c>{ lines, note?, rect, scale, generation }</c>,
    /// <c>rect</c> being the element's client rectangle in CSS pixels and <c>scale</c> the page's
    /// <c>devicePixelRatio</c>.
    /// </summary>
    public const string ContextMenu = "contextMenu";

    /// <summary>
    /// A task-list check box was toggled (Space, Enter or a click): <c>{ lines, checked, generation }</c>,
    /// <c>lines</c> being the item's <c>data-lines</c> and <c>checked</c> its new state.
    /// </summary>
    public const string ToggleTask = "toggleTask";

    /// <summary>The block (or note) the user last interacted with: <c>{ lines?, note?, generation }</c>.</summary>
    public const string Position = "position";

    /// <summary>A link was followed: <c>{ href }</c>, the raw attribute value.</summary>
    public const string OpenLink = "openLink";

    /// <summary><c>nextNote</c> or <c>previousNote</c> found no note in that direction.</summary>
    public const string NoMoreNotes = "noMoreNotes";

    /// <summary><c>nextBlock</c> or <c>previousBlock</c> found no block in that direction.</summary>
    public const string NoMoreBlocks = "noMoreBlocks";

    /// <summary>
    /// Backspace in the page, outside any text field: go back to the previous file. Covers the
    /// case where the browser does not report Backspace to the host as an accelerator key.
    /// </summary>
    public const string GoBack = "goBack";

    /// <summary>Files were dropped on the page; their paths come as the message's additional objects.</summary>
    public const string DropFiles = "dropFiles";

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The JSON the page receives for <paramref name="message"/>: camelCase, nulls left out.</summary>
    public static string Serialize(object message) {
        ArgumentNullException.ThrowIfNull(message);

        return JsonSerializer.Serialize(message, message.GetType(), _options);
    }

    /// <summary>A string property of a page message, or <see langword="null"/> when it is missing or not a string.</summary>
    public static string? GetString(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A Boolean property of a page message, or <see langword="null"/> when it is missing or not a Boolean.</summary>
    public static bool? GetBool(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>A number property of a page message, or <see langword="null"/> when it is missing or not a number.</summary>
    public static double? GetDouble(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    /// <summary>
    /// A rectangle property of a page message (<c>{ x, y, width, height }</c>), or
    /// <see langword="null"/> when it is missing or incomplete.
    /// </summary>
    public static RectangleF? GetRect(JsonElement message, string property) {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty(property, out var rect)
            || GetDouble(rect, "x") is not { } x
            || GetDouble(rect, "y") is not { } y
            || GetDouble(rect, "width") is not { } width
            || GetDouble(rect, "height") is not { } height) {
            return null;
        }

        return new RectangleF((float)x, (float)y, (float)width, (float)height);
    }

    /// <summary>
    /// The note a page message is about (its <c>note</c> index: <c>position</c>,
    /// <c>activateNote</c>, <c>contextMenu</c> on a note), from the render the message belongs to;
    /// <see langword="null"/> when the message names no note or one that is not there.
    /// </summary>
    public static RenderedNote? FindNote(JsonElement message, IReadOnlyList<RenderedNote> notes) {
        ArgumentNullException.ThrowIfNull(notes);

        return GetInt(message, "note") is { } index && index >= 0 && index < notes.Count ? notes[index] : null;
    }

    /// <summary>An integer property of a page message, or <see langword="null"/> when it is missing or not an integer.</summary>
    public static int? GetInt(JsonElement message, string property) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
