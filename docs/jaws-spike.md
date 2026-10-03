# JAWS spike results

Task 2 of `docs/plans/completed/001-plan-cake-v1.md` built the WebView2 host and a throw-away page,
`src/PlanCake/web/spike.html`, to find out what JAWS does inside WebView2 before the note
triggers were built. The user ran the checklist with JAWS in the virtual cursor, with JAWS's
default settings. This file records each answer and the decision taken on it; the plan has
been updated to match.

## 1. Enter on a block

Enter on the second paragraph, a nested list item, a table cell, the code block and a heading
was announced with the right block and the right lines ("lines 15-15" and so on). Enter works
as a note trigger.

Two problems came with it:

- The reported `pointerType` was always `mouse`. By default JAWS answers Enter by emulating a
  mouse click, and the user will not change that setting ("nobody will"). See item 6.
- JAWS switched to forms mode every time Enter was used on a block (not with Space). The user
  found this very inconvenient. The likely cause is the permanent `tabindex="-1"` on every
  block: it makes each block a focusable element, and JAWS enters forms mode when a click
  lands on one.

Decision: blocks carry no `tabindex` by default. When the page has to move the virtual cursor
(`focusLines`, `focusNote`, a new note) it sets `tabindex="-1"` on that one element, focuses
it, and removes the attribute again on blur. As a second line of defense, a `keydown` Enter
on a block or note element itself is treated like a click, so if JAWS is in forms mode anyway
(or the element has real focus), a second Enter still does something. The JAWS checks of
Tasks 6 and 7 now include "Enter on a block does not switch JAWS to forms mode".

## 2. Applications key and Shift+F10

Both raise `contextmenu` in the page. They work as the context-menu trigger.

## 3. Moving the virtual cursor from the host

F9 made the page focus the third paragraph; JAWS read it, and Down Arrow continued from
there. `focusLines` and `focusNote` work as planned.

## 4. Task-list checkboxes

Nothing happens: Markdig renders them disabled, so a task cannot be toggled. Enter on them
also switches JAWS to forms mode. The user wants to be able to toggle them, possibly with a
confirmation that can be turned off in the settings.

Decision: new scope, Task 7a in the plan. The checkboxes render enabled; toggling one asks for
confirmation (setting `ConfirmTaskToggle`, on by default), then rewrites `[ ]` or `[x]` on that
item's source line through the same stale-safe write path as notes, with undo and redo.

## 5. Status announcements

The UI Automation notifications raised by `StatusAnnouncer` are heard while in the document.

Decision: the page's `announce` message is not needed and has been dropped from the page
protocol. Every announcement goes through `StatusAnnouncer`.

## 6. Enter compared with a mouse click

A single mouse click on the second paragraph also reported `pointerType` `mouse`, the same as
Enter. The two cannot be told apart.

Decision: the plan's own fallback applies. Enter and a single click both activate: on a block
they add a note, on a note they edit it. A double-click does nothing extra. A click that ends
a text selection does not activate, so text can still be selected with the mouse.

Side remark: the second paragraph's three source lines were joined into one line when
rendered. That is standard CommonMark (a line break inside a paragraph is a soft break) and
stays as it is: plans are hard-wrapped at about 100 columns, and turning soft breaks into
`<br>` would break up every paragraph.

## 7. The two note styles

- The `<button class="note">` note is announced as a button, and B finds it.
- The `role="note"` div is announced acceptably, but Enter on it a second time did nothing.
  The likely explanation: the first Enter put JAWS in forms mode, so the second Enter went to
  the page as a real key press, and a div does not turn Enter into a click. The fixes under
  item 1 (no permanent `tabindex`, and a `keydown` Enter treated like a click) cover this.

Decision: the note style `Note` (the `role="note"` div) remains the default; `Button` stays
available in the settings.

Later decision (Task 7): the `Button` style was dropped altogether. Long button labels are hard
to listen to, and the `role="note"` div already works with Enter and with F9 / Shift+F9, so
every note is a `role="note"` user note.

Superseded: notes became `role="region"` elements named "User note" (see "Later findings
(after the plan)", Notes are regions).

## 8. Host shortcuts

Every shortcut in the plan's Keyboard section was announced by the host, except F8: JAWS
takes F8 for its extended-select mode, so it never reaches the page or the host.

Decision: note navigation moves from F8 / Shift+F8 to F9 / Shift+F9, which the spike had
used only for its test focus command.

## Later findings (Task 8)

### The notes list had no name

The list is a `NativeListView`: a real `SysListView32` inside a WinForms container. Its
`AccessibleName` sets the list window's text, but an MSAA probe showed the list window's
`accName` empty: the system proxy for a list view does not use the window text, and falls back
only to a static control just before the list window among its siblings, which it has none of
(it is the container's only child). JAWS reads the MSAA name, so it announced the rows but not
"Notes". Screen readers do not read a preceding label for a list view anyway.

Decision: the list keeps its visible "Notes" label, gets `AccessibleName` as the library
documents, and its window is also named through `IAccPropServices.SetHwndPropStr`
(`PROPID_ACC_NAME`), which the proxy consults first; the probe then reports "Notes". The
library should do this itself; until it does, PlanCake does it (`WindowAccessibleName`).

### A followed link landed at the end of the new file

The Task 7a fix sent focus to the new file's first block, and the page did focus it, yet JAWS
stayed at its old offset in the virtual buffer (the end of a shorter file). Two things differed
from a re-render after a note, which JAWS follows correctly. The page's content was replaced
in place, so JAWS treated it as an update of the same document and kept its offset; and the
host focused the document view again right after posting the render, which took the focus from
the browser's window and handed it back while the page was replacing its content. A UI
Automation probe after following the link reported the focus on the document, not on the
heading the page had focused.

Decision: a different file gets a freshly loaded page (the host navigates to `index.html` again
and renders once the page reports `ready`), so JAWS starts it as a new document; Back and
Forward send their saved block with that render. A re-render of the same file stays in place.
Focusing the document view does nothing when the document already has the focus.

## Later findings (Task 10)

### An outside edit threw the reader to the top

After a change made outside PlanCake the view reloaded, but JAWS jumped to the top of the
document instead of staying on the block being read. The log showed why: every reload was
posted with `focus` on lines `1-1`. The page reports a position only when the user acts on a
block (Enter, a click, the context menu, F9, a link or check box getting the focus); reading with
the arrow keys moves the JAWS virtual cursor, which the page never sees. So the last position
the host knew was the file's first block, where a newly opened file starts, and each reload sent
the virtual cursor back there. On top of that, the page replaced the whole content of `main`,
destroying the nodes the virtual cursor was on, which on its own is enough for JAWS to fall back
to the top ("sometimes" in other situations too).

Decision: a render the user did not ask for by acting on a block (a change outside PlanCake,
Reload, a language switch, the same file opened again) carries no focus. And every re-render of
the same file updates the page in place (`web/morph.js`): unchanged nodes stay the same objects
with their shifted `data-lines` and `data-note` patched, a changed node of the same kind is
patched in place, and only what was added or removed is inserted or removed. The choice of which
old node each new one becomes (common prefix and suffix, then a longest common subsequence of
the nodes' content, then the changed stretches position by position) is a pure function, tested
in Jint (`MorphPlanTests`). Explicit focus (a new note, a toggled task, Back and Forward) still
applies after the update; a different file still gets a freshly loaded page.

### Escape did not close a confirmation

A Yes/No message box has no cancel button, so Escape and the close button did nothing in the
Delete all notes confirmation (and in every other one). Every yes-or-no question now goes
through `DialogHelper.Confirm`, a task dialog with Yes and No that allows cancel: Escape and the
close button answer No; Yes stays the default button, and the dialog is mirrored in a
right-to-left language.

## Later findings (Task 12)

### A hint is read only in the label

A separate hint label under the Closing marker box, also given as the box's description, was
read by JAWS in neither form. The hint is now in the box's own label ("Closing marker (leave
empty for a single marker that runs to the end of the line):").

## Later findings (Task 15a)

### Keys for moving between blocks

JAWS's `default.jkm` binds Alt+Down and Alt+Up to OpenListBox and CloseListBox, so they cannot be
PlanCake's. Alt+Shift+Down and Alt+Shift+Up are bound to MouseDown and MouseUp, which pass the
keys to the application unless the JAWS cursor is active. PlanCake uses those. Checked: the keys
reach PlanCake, JAWS reads each block reached, JAWS stays out of forms mode, and Enter adds a
note to that block.

## Later findings (after the plan)

### Notes are regions

With real plans, JAWS navigated `role="note"` notes worse than regions: a named region is a
landmark, which JAWS can list and move to with its region keys. Commit 31e4f4c made every note
a `role="region"`. It still carried role descriptions, "user note" and "unote" for braille, so
JAWS read "user note" where it would say "region"; that turned out cumbersome to listen to, note
after note, so commit 9bfce78 dropped them. A note is now a plain
region named by `aria-label="User note"`, translated into the interface language, in the window
and in exported HTML alike.

### The menu bar from the document

Alt and a menu's letter, Alt alone and F10 did nothing while the document had the focus: keys
pressed in WebView2 never pass through the host's message loop, so Windows never sees them, and
the browser reports them to the host (`AcceleratorKeyPressed`) without acting on them. The host
now classifies every key the browser reports (`MenuKeys`, tested
without a browser): Alt with a letter that is not a host shortcut opens that menu, and Alt
pressed and released alone, or F10, enters the menu bar, through `WM_SYSCOMMAND` /
`SC_KEYMENU` as Windows itself does.
