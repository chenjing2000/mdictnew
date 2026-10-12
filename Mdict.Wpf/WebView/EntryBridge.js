// Oxford entry:// links -> internal, cancelable WebView2 lookup navigation.
// v0.4.0: keep Oxford's original HTML and all unrelated click handlers intact.
(function () {
    "use strict";

    if (location.protocol !== "https:" || location.hostname !== "mdict.invalid") {
        return;
    }

    function isTextTarget(event) {
        let target = event.target;
        if (target && target.nodeType !== 1) target = target.parentElement;
        return event.button === 0 && target && typeof target.closest === "function" &&
            !target.closest("a,button,input,textarea,select,audio,video,[onclick],[contenteditable]");
    }

    let dragging = false, startX = 0, startY = 0;
    window.addEventListener("mousedown", function (event) {
        dragging = isTextTarget(event);
        startX = event.clientX;
        startY = event.clientY;
    }, true);
    window.addEventListener("mouseup", function (event) {
        const moved = Math.abs(event.clientX - startX) + Math.abs(event.clientY - startY) > 3;
        if (dragging && moved && event.button === 0) {
            const text = window.getSelection().toString();
            if (text.trim()) window.chrome.webview.postMessage({type: "mdict-copy", value: text});
        }
        dragging = false;
    }, true);
    window.addEventListener("dblclick", function (event) {
        if (!isTextTarget(event)) return;
        const word = window.getSelection().toString().trim();
        if (word && word.length <= 512 && !/[\x00-\x1f\x7f]/.test(word))
            window.chrome.webview.postMessage({type: "mdict-copy-lookup", value: word});
    }, true);

    window.addEventListener("click", function (event) {
        let target = event.target;
        if (target && target.nodeType !== 1) {
            target = target.parentElement;
        }
        if (!target || typeof target.closest !== "function") {
            return;
        }

        const link = target.closest("a[href]");
        if (!link) {
            return;
        }
        const href = link.getAttribute("href") || "";
        if (!/^entry:\/\//i.test(href)) {
            return;
        }

        // Intercept only entry://. Let anchors, audio, and Oxford's collapsible
        // controls continue through their existing handlers.
        event.preventDefault();
        event.stopImmediatePropagation();
        event.stopPropagation();

        let key;
        try {
            // Decode once, retaining literal +, @, &, and punctuation in the key.
            // Do not parse entry:// as a conventional URL with a hostname.
            key = decodeURIComponent(href.substring("entry://".length));
        } catch (_) {
            console.warn("[MdictViewer entry] Invalid encoded key:", href);
            return;
        }
        if (!key || key.length > 512 || /[\x00-\x1f\x7f]/.test(key)) {
            console.warn("[MdictViewer entry] Invalid key:", href);
            return;
        }

        // The host cancels /lookup before any network request; it schedules
        // a normal MainWindow query outside the NavigationStarting callback.
        location.assign("https://mdict.invalid/lookup?key=" + encodeURIComponent(key));
    }, true);
}());
