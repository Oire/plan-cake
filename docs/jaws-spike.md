# JAWS spike results

Task 2 of `docs/plans/001-plan-cake-v1.md` built the WebView2 host and a throw-away page,
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

## 8. Host shortcuts

Every shortcut in the plan's Keyboard section was announced by the host, except F8: JAWS
takes F8 for its extended-select mode, so it never reaches the page or the host.

Decision: note navigation moves from F8 / Shift+F8 to F9 / Shift+F9, which the spike had
used only for its test focus command.
