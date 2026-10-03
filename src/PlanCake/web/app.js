// The document page. The host renders the Markdown and posts it here; this script shows it,
// tells the host which block or note the user acts on, and moves the virtual cursor when
// the host asks. The protocol is Technical details, "Page protocol", in the PlanCake plan.
//
// No user-visible string may be written here: every one comes from the host in the
// "strings" message, already translated.
"use strict";

(function () {
    const webview = window.chrome && window.chrome.webview;
    const main = document.getElementById("document");

    // Only the host writes these attributes: it renames them where the plan's own raw HTML
    // carries them (MarkdownRenderer.NeutralizeProtocolMarkers), so a plan cannot pass an
    // element of its own off as a block, a note or a task.
    const blockSelector = "[data-lines]";
    const noteSelector = "[data-note]";
    const targetSelector = noteSelector + ", " + blockSelector;

    // A task-list check box the host rendered enabled (Task 7a). Toggling one asks the host to
    // rewrite the item's marker in the file. Found by an attribute, not a class: a class can be
    // spelled with character references in raw HTML, an attribute name cannot.
    const taskSelector = "input[data-plancake-task]";

    // Elements with a behavior of their own: a click or Enter on one inside a block does not
    // activate the block.
    const interactiveSelector = "a[href], button, input, select, textarea, summary, label, audio, video, "
        + "[contenteditable]:not([contenteditable='false'])";
    const focusableSelector = "a[href], button, input, select, textarea, summary, [tabindex]";

    // The render the page shows; the host ignores any message carrying an older one.
    let generation = 0;
    let strings = {};
    let hasDocument = false;

    // The block or note the user last interacted with (or the host last focused): where F9
    // and Shift+F9 start from.
    let current = null;

    // The element the page last moved the focus to (a block or note with Alt+Shift+Down, F9
    // or a focus the host asks for; a check box; an anchor's target). It carries this
    // attribute, and app.css draws the focus outline from it, so the outline does not depend on
    // when Chromium matches :focus or :focus-visible: with Alt+Shift+Down the :focus outline did
    // not show at all (Task 15a check). The mark goes when another block or note becomes
    // current, when the focus moves to another element of the document, and when the document
    // loses the focus.
    const currentAttribute = "data-plancake-current";
    let marked = null;

    function mark(element) {
        if (marked === element) {
            return;
        }

        unmark();
        element.setAttribute(currentAttribute, "");
        marked = element;
    }

    function unmark() {
        if (marked !== null) {
            marked.removeAttribute(currentAttribute);
            marked = null;
        }
    }

    function post(message) {
        if (webview) {
            webview.postMessage(message);
        }
    }

    // The block or note an event on `node` belongs to, or null. A link, a button or a form
    // control inside a block or a note keeps its own behavior.
    function targetOf(node) {
        if (!(node instanceof Element) || !main.contains(node)) {
            return null;
        }

        const element = node.closest(targetSelector);

        if (!element || !main.contains(element)) {
            return null;
        }

        const interactive = node.closest(interactiveSelector);

        if (interactive && interactive !== element && element.contains(interactive)) {
            return null;
        }

        return element;
    }

    // A field the user types in, where Backspace deletes text.
    function isTextField(node) {
        if (!(node instanceof Element)) {
            return false;
        }

        if (node.isContentEditable || node.tagName === "TEXTAREA" || node.tagName === "SELECT") {
            return true;
        }

        const nonTextInputs = ["button", "checkbox", "color", "file", "hidden", "image", "radio", "range", "reset", "submit"];
        return node.tagName === "INPUT" && !nonTextInputs.includes((node.getAttribute("type") || "text").toLowerCase());
    }

    function isNote(element) {
        return element.hasAttribute("data-note");
    }

    function noteIndex(element) {
        return Number(element.getAttribute("data-note"));
    }

    // The lines of the block `element` is or sits in, or null (a note at the very top).
    function linesOf(element) {
        const block = element.closest(blockSelector);
        return block && main.contains(block) ? block.getAttribute("data-lines") : null;
    }

    function setCurrent(element) {
        current = element;

        // The mark stays on a check box the page focused inside the block that becomes current.
        if (marked !== null && !element.contains(marked)) {
            unmark();
        }

        if (isNote(element)) {
            post({ type: "position", note: noteIndex(element), generation: generation });
        } else {
            post({ type: "position", lines: element.getAttribute("data-lines"), generation: generation });
        }
    }

    // The element's client rectangle in CSS pixels, where the host shows a menu for it.
    function rectOf(element) {
        const rect = element.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
    }

    // CSS pixels times this are the view's pixels; it already includes the zoom.
    function scale() {
        return window.devicePixelRatio || 1;
    }

    // Enter or a click. On a block it carries the block's rectangle too: a setting can make the
    // host open the block's context menu there instead of the note dialog.
    function activate(element) {
        setCurrent(element);

        if (isNote(element)) {
            post({ type: "activateNote", note: noteIndex(element), generation: generation });
        } else {
            post({
                type: "activate",
                lines: element.getAttribute("data-lines"),
                rect: rectOf(element),
                scale: scale(),
                generation: generation
            });
        }
    }

    // A click that ends a text selection made with the mouse selects; it does not activate.
    function endsSelection(element) {
        const selection = window.getSelection();
        return selection !== null && !selection.isCollapsed && selection.containsNode(element, true);
    }

    // Moves the virtual cursor to `element` and marks it for the focus outline. A block gets
    // tabindex="-1" only while it has focus: a permanent one makes JAWS switch to forms mode on
    // Enter (Task 2 spike). The mark comes first, so the focusin of this very focus keeps it.
    function focusElement(element) {
        if (!element.matches(focusableSelector)) {
            element.setAttribute("tabindex", "-1");
            element.addEventListener("blur", function () {
                element.removeAttribute("tabindex");
            }, { once: true });
        }

        mark(element);
        element.focus();
    }

    function blockAt(lines) {
        return main.querySelector('[data-lines="' + CSS.escape(String(lines)) + '"]');
    }

    // The task-list check box of the block with these lines, or null.
    function taskAt(lines) {
        const element = blockAt(lines);
        return element ? element.querySelector(taskSelector) : null;
    }

    // `task`: focus the block's task-list check box instead of the block, so the focus stays on
    // the check box the user just toggled.
    function focusLines(lines, task) {
        const element = blockAt(lines);

        if (!element) {
            return;
        }

        current = element;
        const checkbox = task ? element.querySelector(taskSelector) : null;
        focusElement(checkbox || element);
    }

    // An unchecked item some of whose nested tasks are done shows as partially checked (the
    // host marks it data-mixed; HTML has no attribute for the indeterminate state).
    function showMixedState(checkbox) {
        checkbox.indeterminate = checkbox.hasAttribute("data-mixed") && !checkbox.checked;
    }

    function setTaskState(lines, checked) {
        const checkbox = taskAt(lines);

        if (checkbox) {
            checkbox.checked = checked;
            showMixedState(checkbox);
        }
    }

    // A task-list check box inside the document, or null.
    function taskCheckbox(node) {
        return node instanceof Element && node.matches(taskSelector) && main.contains(node) ? node : null;
    }

    function focusNote(index) {
        const element = main.querySelector('[data-note="' + CSS.escape(String(index)) + '"]');

        if (element) {
            current = element;
            focusElement(element);
        }
    }

    function moveToNote(forward) {
        const notes = Array.from(main.querySelectorAll(noteSelector));
        let next = null;

        if (current === null || !main.contains(current)) {
            next = forward ? notes[0] : notes[notes.length - 1];
        } else if (forward) {
            next = notes.find(function (note) {
                return note !== current
                    && (current.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
            });
        } else {
            next = notes.reverse().find(function (note) {
                return note !== current
                    && (current.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_PRECEDING) !== 0
                    && !note.contains(current);
            });
        }

        if (!next) {
            post({ type: "noMoreNotes" });
            return;
        }

        setCurrent(next);
        focusElement(next);
    }

    // The places Alt+Shift+Down and Alt+Shift+Up stop at: every block and note, except a block that only
    // repeats the one around it (the <p> of a loose list item carries the item's own lines).
    function blockStops() {
        return Array.from(main.querySelectorAll(targetSelector)).filter(function (element) {
            if (isNote(element) || !element.parentElement) {
                return true;
            }

            const outer = element.parentElement.closest(blockSelector);
            return !outer || !main.contains(outer) || isNote(outer)
                || outer.getAttribute("data-lines") !== element.getAttribute("data-lines");
        });
    }

    // The index in `stops` of the stop the current position is (or sits in), or -1.
    function currentStop(stops) {
        let element = current !== null && main.contains(current) ? current : null;

        while (element) {
            const index = stops.indexOf(element);

            if (index >= 0) {
                return index;
            }

            element = element.parentElement ? element.parentElement.closest(targetSelector) : null;

            if (element && !main.contains(element)) {
                element = null;
            }
        }

        return -1;
    }

    // Alt+Shift+Down / Alt+Shift+Up: the next or previous block or note from the current position (the one
    // the user last acted on or the page last focused), or, with none, from the view. It gets
    // the focus the way F9 gives it to a note, which also scrolls it into view and shows the
    // focus outline, so Enter and the Applications key then act on it.
    function moveToBlock(forward) {
        const stops = blockStops();
        const items = stops.map(function (element) {
            const shown = element.getClientRects().length > 0;
            const rect = shown ? element.getBoundingClientRect() : null;
            return { top: rect ? rect.top : 0, bottom: rect ? rect.bottom : 0, shown: shown };
        });
        const index = PlanCakeBlocks.pick(items, currentStop(stops), forward, window.innerHeight);

        if (index < 0) {
            post({ type: "noMoreBlocks" });
            return;
        }

        const next = stops[index];
        setCurrent(next);
        focusElement(next);
    }

    // The empty window: what it is, and how to open a file (keys from the host's own table).
    function showNoDocument() {
        main.textContent = "";
        main.removeAttribute("lang");

        if (strings.noDocument) {
            const paragraph = document.createElement("p");
            paragraph.textContent = strings.noDocument;
            main.append(paragraph);
        }

        if (Array.isArray(strings.noDocumentHints) && strings.noDocumentHints.length > 0) {
            const list = document.createElement("ul");

            strings.noDocumentHints.forEach(function (hint) {
                const item = document.createElement("li");
                item.textContent = hint;
                list.append(item);
            });

            main.append(list);
        }
    }

    function render(message) {
        // A page shows one file: another file gets a freshly loaded page (Task 8). So a page that
        // already shows a document is getting the same file again, and is updated in place, so
        // that the nodes JAWS's virtual cursor sits on survive (morph.js).
        const again = hasDocument;
        generation = message.generation;
        hasDocument = true;

        // Raw HTML in the plan comes along: the content security policy keeps its scripts and
        // event handler attributes from running, and the host cancels any navigation.
        if (again) {
            PlanCakeMorph.morph(main, message.html);
        } else {
            main.innerHTML = message.html;
        }

        if (current !== null && !main.contains(current)) {
            current = null;
        }

        if (marked !== null && !main.contains(marked)) {
            marked = null;
        }

        // Each check box shows the file's state. The morph compares markup, which holds the
        // checked attribute and not the state the user toggled: a check box whose toggle was
        // not written (the file changed meanwhile) would otherwise keep a state the file lacks.
        main.querySelectorAll(taskSelector).forEach(function (checkbox) {
            checkbox.checked = checkbox.hasAttribute("checked");
            showMixedState(checkbox);
        });
        main.setAttribute("lang", message.documentLang);
        document.title = message.title;

        const focus = message.focus || {};

        if (typeof focus.note === "number") {
            focusNote(focus.note);
        } else if (typeof focus.lines === "string") {
            focusLines(focus.lines, focus.task === true);
        } else if (!again) {
            window.scrollTo(0, 0);
        }
    }

    function applyStrings(message) {
        strings = message;
        document.documentElement.setAttribute("lang", message.uiLang);
        document.documentElement.setAttribute("dir", message.uiDir);

        if (!hasDocument) {
            showNoDocument();
        }
    }

    function scrollToAnchor(fragment) {
        let id = fragment;

        try {
            id = decodeURIComponent(fragment);
        } catch (error) {
            // Not percent-encoded after all: use it as written.
        }

        const element = id === "" ? null : document.getElementById(id);

        if (element && main.contains(element)) {
            const target = element.closest(targetSelector);

            if (target) {
                setCurrent(target);
            }

            focusElement(element);
        }
    }

    function followLink(link) {
        const href = link.getAttribute("href") || "";

        if (href.startsWith("#")) {
            scrollToAnchor(href.substring(1));
            return;
        }

        post({ type: "openLink", href: href });
    }

    document.addEventListener("click", function (event) {
        const node = event.target;
        const link = node instanceof Element ? node.closest("a[href]") : null;

        // No link ever navigates the view: the host decides what a link opens.
        if (link) {
            event.preventDefault();
            followLink(link);
            return;
        }

        // A double-click does nothing more than its first click already did.
        if (event.detail > 1) {
            return;
        }

        const element = targetOf(node);

        if (!element || endsSelection(element)) {
            return;
        }

        activate(element);
    });

    // JAWS turns Enter into a click. When it is in forms mode, or the element has real focus,
    // Enter arrives as a key instead, and a div would ignore it: treat it like the click.
    document.addEventListener("keydown", function (event) {
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
            return;
        }

        // Backspace goes back to the previous file, as in a browser, unless the user is typing.
        // The host usually sees the key first; this covers the case where it does not.
        if (event.key === "Backspace") {
            if (!isTextField(event.target)) {
                event.preventDefault();
                post({ type: "goBack" });
            }

            return;
        }

        if (event.key !== "Enter") {
            return;
        }

        // A check box ignores Enter on its own; Enter toggles a task as Space does.
        const checkbox = taskCheckbox(event.target);

        if (checkbox) {
            event.preventDefault();
            checkbox.click();
            return;
        }

        const element = event.target;

        if (!(element instanceof Element) || !main.contains(element)
            || !element.matches(targetSelector) || element.matches(interactiveSelector)) {
            return;
        }

        event.preventDefault();
        activate(element);
    });

    // Space, Enter or a click on a task-list check box. The page shows the new state at once;
    // the host rewrites the file, or sends the file's state back ("taskState") when the user
    // cancels or the file cannot be written.
    document.addEventListener("change", function (event) {
        const checkbox = taskCheckbox(event.target);

        if (!checkbox) {
            return;
        }

        const block = checkbox.closest(blockSelector);

        if (!block || !main.contains(block)) {
            return;
        }

        setCurrent(block);
        post({
            type: "toggleTask",
            lines: block.getAttribute("data-lines"),
            checked: checkbox.checked,
            generation: generation
        });
    });

    // Focus reaching a link, a check box or anything else in the document (JAWS moves the
    // focus to such elements as its virtual cursor passes them) makes its block or note the
    // current one, so F9 and Shift+F9 start from where the user is. A block or note the page
    // focuses itself is already current.
    document.addEventListener("focusin", function (event) {
        const node = event.target;

        if (!(node instanceof Element) || node === main || !main.contains(node)) {
            return;
        }

        // The focus moved on (Tab to a link, JAWS passing one): that element shows its own.
        if (node !== marked) {
            unmark();
        }

        const element = node.closest(targetSelector);

        if (element && main.contains(element) && element !== current) {
            setCurrent(element);
        }
    });

    // The document lost the focus (to the notes list, a dialog, another window): no outline
    // stays behind where the keys no longer go.
    window.addEventListener("blur", function (event) {
        if (event.target === window) {
            unmark();
        }
    });

    // The Applications key, Shift+F10 and a right-click all end up here.
    document.addEventListener("contextmenu", function (event) {
        event.preventDefault();

        const node = event.target;
        let element = node instanceof Element && main.contains(node) ? node.closest(targetSelector) : null;

        if (!element && current !== null && main.contains(current)) {
            element = current;
        }

        if (!element) {
            return;
        }

        setCurrent(element);

        const message = {
            type: "contextMenu",
            lines: linesOf(element),
            rect: rectOf(element),
            scale: scale(),
            generation: generation
        };

        if (isNote(element)) {
            message.note = noteIndex(element);
        }

        post(message);
    });

    // A file dragged from Explorer: the page cannot see its path, so the File objects go to
    // the host, which reads the paths.
    document.addEventListener("dragover", function (event) {
        event.preventDefault();

        if (event.dataTransfer) {
            event.dataTransfer.dropEffect = Array.from(event.dataTransfer.types).includes("Files") ? "copy" : "none";
        }
    });

    document.addEventListener("drop", function (event) {
        event.preventDefault();

        const files = event.dataTransfer ? event.dataTransfer.files : null;

        if (webview && files && files.length > 0) {
            webview.postMessageWithAdditionalObjects({ type: "dropFiles" }, files);
        }
    });

    if (webview) {
        webview.addEventListener("message", function (event) {
            const message = event.data;

            if (!message || typeof message.type !== "string") {
                return;
            }

            switch (message.type) {
                case "render":
                    render(message);
                    break;
                case "strings":
                    applyStrings(message);
                    break;
                case "taskState":
                    setTaskState(message.lines, message.checked === true);
                    break;
                case "focusNote":
                    focusNote(message.note);
                    break;
                case "nextNote":
                    moveToNote(true);
                    break;
                case "previousNote":
                    moveToNote(false);
                    break;
                case "nextBlock":
                    moveToBlock(true);
                    break;
                case "previousBlock":
                    moveToBlock(false);
                    break;
            }
        });
    }

    post({ type: "ready" });
})();
