// Chooses the block Alt+Shift+Down Arrow and Alt+Shift+Up Arrow move to. Tab reaches only links and check
// boxes, and F9 only notes; a block carries no tabindex (Task 2 spike), so without these keys a
// keyboard user who does not use a screen reader could not reach an ordinary block to press
// Enter on it. Screen reader users move with their own reading cursor instead.
//
// pick is pure, so the tests run it without a browser (PlanCake.Tests, BlockPickTests).
"use strict";

var PlanCakeBlocks = (function () {
    // `items`: the blocks and notes of the document in document order, each { top, bottom, shown }:
    // the top and bottom of its client rectangle in CSS pixels, and whether it is rendered at all
    // (false inside a closed <details>, for instance). `current`: the index of the item the user
    // is on, or -1 when there is none. Returns the index of the item to move to, or -1 when there
    // is none in that direction.
    function pick(items, current, forward, viewportHeight) {
        const step = forward ? 1 : -1;

        if (current >= 0) {
            for (let index = current + step; index >= 0 && index < items.length; index += step) {
                if (items[index].shown) {
                    return index;
                }
            }

            return -1;
        }

        // No position yet: Alt+Shift+Down starts at the first item that reaches into the view (or lies
        // below it), Alt+Shift+Up at the last one that reaches into it (or lies above it).
        if (forward) {
            for (let index = 0; index < items.length; index++) {
                if (items[index].shown && items[index].bottom > 0) {
                    return index;
                }
            }
        } else {
            for (let index = items.length - 1; index >= 0; index--) {
                if (items[index].shown && items[index].top < viewportHeight) {
                    return index;
                }
            }
        }

        return -1;
    }

    return { pick: pick };
})();
