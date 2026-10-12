// Each document owns its layout; only scrolling is shared by the WPF host.
(function () {
    "use strict";
    if (window !== window.top || location.origin !== "https://mdict.invalid") return;
    const post = (type, value) => window.chrome.webview.postMessage({ type, value });
    let scheduled = false;
    let lastHeight = 0;
    let lastWidth = 0;
    let lastViewportHeight = 0;
    let contentChanged = true;
    let lastMeasuredHeight = 0;
    function measure() {
        scheduled = false;
        if (!document.body) return;
        // Suppress growth caused by our last height report to avoid vh/% feedback.
        // An unrelated viewport resize must still measure CSS-only content changes.
        const viewportChanged = window.innerHeight !== lastViewportHeight;
        const widthChanged = window.innerWidth !== lastWidth;
        lastWidth = window.innerWidth;
        lastViewportHeight = window.innerHeight;
        const body = document.body;
        const bottomMargin = parseFloat(getComputedStyle(body).marginBottom) || 0;
        const height = Math.ceil(Math.max(body.offsetHeight, body.scrollHeight) +
            body.getBoundingClientRect().top + window.scrollY + bottomMargin);
        const geometryChanged = height !== lastMeasuredHeight;
        const followsViewport = viewportChanged && window.innerHeight === lastHeight &&
            !widthChanged && !contentChanged;
        const changed = contentChanged;
        contentChanged = false;
        lastMeasuredHeight = height;
        if ((followsViewport && height >= lastHeight) ||
            (!geometryChanged && !widthChanged && !changed)) return;
        if (height > 0 && height !== lastHeight) {
            lastHeight = height;
            post("mdict-height", height);
        }
    }
    function schedule(changed) {
        contentChanged = contentChanged || changed === true;
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(measure);
    }
    function anchor() {
        if (!location.hash) return;
        let id;
        try { id = decodeURIComponent(location.hash.substring(1)); } catch (_) { return; }
        const target = document.getElementById(id) || document.getElementsByName(id)[0];
        if (target) post("mdict-anchor", Math.max(0, target.getBoundingClientRect().top + window.scrollY));
    }
    document.addEventListener("DOMContentLoaded", function () {
        const style = document.createElement("style");
        style.textContent = "html{overflow:hidden!important}";
        document.head.appendChild(style);
        new ResizeObserver(() => schedule()).observe(document.body);
        new MutationObserver(() => schedule(true)).observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true });
        document.addEventListener("load", () => schedule(true), true);
        document.addEventListener("transitionend", () => schedule(true), true);
        document.addEventListener("animationend", () => schedule(true), true);
        if (document.fonts) document.fonts.ready.then(() => schedule(true));
        schedule(true);
    });
    window.addEventListener("load", () => schedule(true));
    window.addEventListener("resize", () => schedule());
    window.addEventListener("hashchange", () => requestAnimationFrame(anchor));
    window.addEventListener("click", function (event) {
        const target = event.target && (event.target.nodeType === 1 ? event.target : event.target.parentElement);
        const link = target && target.closest && target.closest("a[href]");
        if (link && link.getAttribute("href") === location.hash && location.hash)
            requestAnimationFrame(anchor);
    }, true);
    window.addEventListener("wheel", function (event) {
        if (event.ctrlKey || !event.deltaY) return;
        event.preventDefault();
        const scale = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? window.innerHeight : 1;
        post("mdict-scroll", event.deltaY * scale);
    }, { passive: false });
}());
