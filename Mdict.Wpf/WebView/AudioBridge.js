// MdictViewer v0.3.0 - Oxford sound:// -> local WebView2 HTML Audio.
// Inject once, before Oxford scripts. Do not alter other Oxford interactions.
(function () {
    "use strict";

    if (location.protocol !== "https:" || location.hostname !== "mdict.invalid") {
        return;
    }

    let currentAudio = null;

    function stopAudio() {
        if (currentAudio !== null) {
            const previous = currentAudio;
            currentAudio = null;
            previous.pause();
            previous.removeAttribute("src");
            previous.load();
        }
    }

    function getAudioUrl(href) {
        if (!/^sound:\/\//i.test(href)) {
            return null;
        }

        const path = href.substring("sound://".length).replace(/\\/g, "/");
        const pieces = path.split("/");
        if (pieces.length === 0) {
            return null;
        }

        const safePieces = [];
        for (const piece of pieces) {
            let name;
            try {
                name = decodeURIComponent(piece);
            } catch (_) {
                return null;
            }

            // Reject ambiguous, nested-encoding or non-resource paths.
            if (!name || name === "." || name === ".." ||
                /[\\/:?#%\x00-\x1f]/.test(name)) {
                return null;
            }
            safePieces.push(encodeURIComponent(name));
        }
        return "https://mdict.invalid/" + safePieces.join("/");
    }

    // A capturing listener on window runs before normal handlers on document
    // and links, including Oxford's own click handlers.
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
        const href = (link.getAttribute("href") || "").trim();
        if (!/^sound:\/\//i.test(href)) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        event.stopPropagation();

        const url = getAudioUrl(href);
        stopAudio();
        if (!url) {
            console.warn("[MdictViewer audio] Invalid sound link:", href);
            return;
        }

        // This call is made synchronously during a real user click, which
        // preserves the browser's transient audio playback permission.
        const audio = new Audio();
        currentAudio = audio;
        audio.preload = "auto";
        audio.src = url;
        audio.addEventListener("error", function () {
            if (currentAudio === audio) {
                console.warn("[MdictViewer audio] Unable to load:", url);
            }
        }, { once: true });

        const playResult = audio.play();
        if (playResult && typeof playResult.catch === "function") {
            playResult.catch(function (error) {
                if (currentAudio === audio) {
                    console.warn("[MdictViewer audio] Playback failed:", error);
                }
            });
        }
    }, true);

    window.addEventListener("pagehide", stopAudio, true);
}());
