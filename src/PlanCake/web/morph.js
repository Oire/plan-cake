// Updates the document in place when the same file is rendered again (an outside change, a
// reload, a note action, undo and redo), instead of replacing it. JAWS keeps its virtual cursor
// on DOM nodes: replacing the whole content destroyed the node it was on and sent it back to the
// top of the document (Task 10 JAWS check). Here every node whose content did not change is
// kept as the same object, with only its attributes patched (data-lines shift when lines are
// added above it); a node whose content changed is patched in place when it is the same kind of
// element, and only what was really added or removed is inserted or removed.
//
// planMatches is pure, so the tests run it without a browser (PlanCake.Tests, MorphPlanTests).
"use strict";

var PlanCakeMorph = (function () {
    // Attributes that do not make a node's content different: line numbers and note indices
    // shift with edits elsewhere, and the page adds tabindex while it focuses an element.
    const volatileAttributes = / (?:data-lines|data-note|tabindex)="[^"]*"/g;

    // Past this many cells (old times new nodes) the middle of a change is not diffed node by
    // node: it is patched position by position instead.
    const maxDiffCells = 250000;

    // For each new item, the index of the old item it becomes, or -1 for an item to insert. Old
    // items no new item takes are removed. Items are { key, kind }: equal keys mean the same
    // content; a new item may also take the old item at the same place in a changed stretch
    // when their kinds match. The indices taken always increase, so no kept node ever moves.
    function planMatches(oldItems, newItems) {
        const oldCount = oldItems.length;
        const newCount = newItems.length;
        const result = new Array(newCount).fill(-1);

        let start = 0;

        while (start < oldCount && start < newCount && oldItems[start].key === newItems[start].key) {
            result[start] = start;
            start++;
        }

        let oldEnd = oldCount;
        let newEnd = newCount;

        while (oldEnd > start && newEnd > start && oldItems[oldEnd - 1].key === newItems[newEnd - 1].key) {
            oldEnd--;
            newEnd--;
            result[newEnd] = oldEnd;
        }

        const anchors = commonItems(oldItems, start, oldEnd, newItems, start, newEnd);
        anchors.push([oldEnd, newEnd]);

        let oldIndex = start;
        let newIndex = start;

        anchors.forEach(function (anchor) {
            // The changed stretch before this anchor: same kinds pair up in order.
            const length = Math.min(anchor[0] - oldIndex, anchor[1] - newIndex);

            for (let offset = 0; offset < length; offset++) {
                if (oldItems[oldIndex + offset].kind === newItems[newIndex + offset].kind) {
                    result[newIndex + offset] = oldIndex + offset;
                }
            }

            if (anchor[1] < newEnd) {
                result[anchor[1]] = anchor[0];
            }

            oldIndex = anchor[0] + 1;
            newIndex = anchor[1] + 1;
        });

        return result;
    }

    // The longest common subsequence of keys in old[oldStart, oldEnd) and new[newStart, newEnd),
    // as [oldIndex, newIndex] pairs in order; empty when the stretch is too large to diff.
    function commonItems(oldItems, oldStart, oldEnd, newItems, newStart, newEnd) {
        const rows = oldEnd - oldStart;
        const columns = newEnd - newStart;

        if (rows === 0 || columns === 0 || rows * columns > maxDiffCells) {
            return [];
        }

        // lengths[i][j]: the common length of old[i..] and new[j..], flattened.
        const width = columns + 1;
        const lengths = new Uint32Array((rows + 1) * width);

        for (let i = rows - 1; i >= 0; i--) {
            for (let j = columns - 1; j >= 0; j--) {
                lengths[i * width + j] = oldItems[oldStart + i].key === newItems[newStart + j].key
                    ? lengths[(i + 1) * width + j + 1] + 1
                    : Math.max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);
            }
        }

        const pairs = [];
        let i = 0;
        let j = 0;

        while (i < rows && j < columns) {
            if (oldItems[oldStart + i].key === newItems[newStart + j].key) {
                pairs.push([oldStart + i, newStart + j]);
                i++;
                j++;
            } else if (lengths[(i + 1) * width + j] >= lengths[i * width + j + 1]) {
                i++;
            } else {
                j++;
            }
        }

        return pairs;
    }

    function describe(node) {
        if (node.nodeType === 1) {
            return {
                key: node.outerHTML.replace(volatileAttributes, ""),
                kind: node.tagName + (node.hasAttribute("data-note") ? "#note" : "")
            };
        }

        return { key: "#" + node.nodeType + ":" + node.nodeValue, kind: "#" + node.nodeType };
    }

    function syncAttributes(target, source) {
        Array.from(target.attributes).forEach(function (attribute) {
            // The element the page focused keeps its tabindex until it loses the focus.
            const keep = attribute.name === "tabindex" && target === document.activeElement;

            if (!keep && !source.hasAttribute(attribute.name)) {
                target.removeAttribute(attribute.name);
            }
        });

        Array.from(source.attributes).forEach(function (attribute) {
            if (target.getAttribute(attribute.name) !== attribute.value) {
                target.setAttribute(attribute.name, attribute.value);
            }
        });

        // Once clicked, a check box's state no longer follows its attribute.
        if (target.tagName === "INPUT") {
            target.checked = source.hasAttribute("checked");
            target.disabled = source.hasAttribute("disabled");
        }
    }

    // Makes `target` (a node in the document) look like `source` (the same kind of node).
    function morphNode(target, source) {
        if (target.nodeType !== 1) {
            if (target.nodeValue !== source.nodeValue) {
                target.nodeValue = source.nodeValue;
            }

            return;
        }

        syncAttributes(target, source);

        if (target.innerHTML !== source.innerHTML) {
            morphChildren(target, source);
        }
    }

    // Makes the children of `target` look like those of `source`, keeping every node it can.
    function morphChildren(target, source) {
        const oldNodes = Array.from(target.childNodes);
        const newNodes = Array.from(source.childNodes);
        const plan = planMatches(oldNodes.map(describe), newNodes.map(describe));
        const taken = new Set(plan.filter(function (index) { return index >= 0; }));

        oldNodes.forEach(function (node, index) {
            if (!taken.has(index)) {
                target.removeChild(node);
            }
        });

        // What is left of the old nodes is in plan order: walk it alongside the new nodes.
        let next = target.firstChild;

        newNodes.forEach(function (node, index) {
            if (plan[index] < 0) {
                target.insertBefore(node, next);
                return;
            }

            const kept = oldNodes[plan[index]];
            morphNode(kept, node);

            if (kept === next) {
                next = kept.nextSibling;
            } else {
                target.insertBefore(kept, next);
            }
        });
    }

    // Replaces the content of `target` with `html`, keeping every unchanged node.
    function morph(target, html) {
        const template = document.createElement("template");
        template.innerHTML = html;
        morphChildren(target, template.content);
    }

    return { planMatches: planMatches, morph: morph };
})();
