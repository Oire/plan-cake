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

    const blockSelector = "[data-lines]";
    const noteSelector = "[data-note]";
    const targetSelector = noteSelector + ", " + blockSelector;

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

    function post(message) {
        if (webview) {
            webview.postMessage(message);
        }
    }

    // The block or note an event on `node` belongs to, or null. A link, a button or a form
    // control inside a block keeps its own behavior; a note shown as a button is the note.
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

        if (isNote(element)) {
            post({ type: "position", note: noteIndex(element), generation: generation });
        } else {
            post({ type: "position", lines: element.getAttribute("data-lines"), generation: generation });
        }
    }

    function activate(element) {
        setCurrent(element);

        if (isNote(element)) {
            post({ type: "activateNote", note: noteIndex(element), generation: generation });
        } else {
            post({ type: "activate", lines: element.getAttribute("data-lines"), generation: generation });
        }
    }

    // A click that ends a text selection made with the mouse selects; it does not activate.
    function endsSelection(element) {
        const selection = window.getSelection();
        return selection !== null && !selection.isCollapsed && selection.containsNode(element, true);
    }

    // Moves the virtual cursor to `element`. A block gets tabindex="-1" only while it has
    // focus: a permanent one makes JAWS switch to forms mode on Enter (Task 2 spike).
    function focusElement(element) {
        if (!element.matches(focusableSelector)) {
            element.setAttribute("tabindex", "-1");
            element.addEventListener("blur", function () {
                element.removeAttribute("tabindex");
            }, { once: true });
        }

        element.focus();
    }

    function focusLines(lines) {
        const element = main.querySelector('[data-lines="' + CSS.escape(String(lines)) + '"]');

        if (element) {
            current = element;
            focusElement(element);
        }
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

        focusElement(next);
        setCurrent(next);
    }

    function showNoDocument() {
        main.textContent = "";
        main.removeAttribute("lang");

        if (strings.noDocument) {
            const paragraph = document.createElement("p");
            paragraph.textContent = strings.noDocument;
            main.append(paragraph);
        }
    }

    function render(message) {
        generation = message.generation;
        hasDocument = true;
        current = null;

        // Raw HTML in the plan comes along: the content security policy keeps its scripts and
        // event handler attributes from running, and the host cancels any navigation.
        main.innerHTML = message.html;
        main.setAttribute("lang", message.documentLang);
        document.title = message.title;

        const focus = message.focus || {};

        if (typeof focus.note === "number") {
            focusNote(focus.note);
        } else if (typeof focus.lines === "string") {
            focusLines(focus.lines);
        } else {
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

        const element = event.target;

        if (!(element instanceof Element) || !main.contains(element)
            || !element.matches(targetSelector) || element.matches(interactiveSelector)) {
            return;
        }

        event.preventDefault();
        activate(element);
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

        const rect = element.getBoundingClientRect();
        const message = {
            type: "contextMenu",
            lines: linesOf(element),
            rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
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
                case "focusLines":
                    focusLines(message.lines);
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
            }
        });
    }

    post({ type: "ready" });
})();
