const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../Mdict.Wpf/WebView/LayoutBridge.js'), 'utf8');

function fixture(height) {
    const listeners = new Map();
    const observers = [];
    const frames = [];
    const messages = [];
    let resizePending = false;
    let size = height;
    const on = (name, action) => {
        if (!listeners.has(name)) listeners.set(name, []);
        listeners.get(name).push(action);
    };
    const fire = (name, event) => (listeners.get(name) || []).forEach(action => action(event));
    const window = { innerWidth: 800, innerHeight: 120, scrollY: 0, addEventListener: on,
        chrome: { webview: { postMessage(message) {
            messages.push(message);
            if (message.type === 'mdict-height' && message.value !== window.innerHeight) {
                window.innerHeight = message.value;
                resizePending = true;
            }
        } } } };
    window.top = window;
    const body = { get offsetHeight() { return typeof size === 'function' ? size(window.innerHeight) : size; },
        get scrollHeight() { return this.offsetHeight; }, getBoundingClientRect: () => ({ top: 16 }) };
    const location = { origin: 'https://mdict.invalid', hash: '#bottom' };
    const document = { body, head: { appendChild() {} }, addEventListener: on,
        createElement: () => ({}), getElementById: () => ({ getBoundingClientRect: () => ({ top: 90 }) }),
        getElementsByName: () => [] };
    const context = { window, location, document, getComputedStyle: () => ({ marginBottom: '0' }),
        requestAnimationFrame: action => frames.push(action),
        ResizeObserver: class { constructor(action) { observers.push(action); } observe() {} },
        MutationObserver: class { constructor() {} observe() {} } };
    vm.runInNewContext(script, context);
    fire('DOMContentLoaded');
    function drain() {
        for (let i = 0; i < 20; i++) {
            frames.splice(0).forEach(action => action());
            if (resizePending) {
                resizePending = false;
                fire('resize');
                observers.forEach(action => action());
            }
            if (!frames.length && !resizePending) return;
        }
        assert.fail('layout feedback did not settle');
    }
    drain();
    return { window, messages, fire, drain, resize(newHeight, viewportHeight) {
        size = newHeight;
        if (viewportHeight !== undefined) window.innerHeight = viewportHeight;
        observers.forEach(action => action());
        drain();
    } };
}

let count = 0;
function check(name, test) { test(); count++; console.log('PASS: ' + name); }
check('fixed intrinsic height', () => assert.equal(fixture(500).window.innerHeight, 516));
check('CSS-only resize after host feedback', () => {
    const f = fixture(500); f.resize(20); assert.equal(f.window.innerHeight, 36);
});
check('CSS-only collapse coinciding with viewport feedback is not lost', () => {
    const f = fixture(500); f.resize(20, 900); assert.equal(f.window.innerHeight, 36);
});
check('CSS-only growth coinciding with external viewport resize is not lost', () => {
    const f = fixture(500); f.resize(700, 550); assert.equal(f.window.innerHeight, 716);
});
for (const factor of [1, 2, 50]) {
    check('viewport-relative factor ' + factor + ' settles without unbounded growth', () => {
        const f = fixture(h => h * factor);
        assert.equal(f.messages.filter(m => m.type === 'mdict-height').length, 1);
        assert.equal(f.window.innerHeight, 120 * factor + 16);
    });
}
check('CSS-only change remains active after viewport-relative layout', () => {
    const f = fixture(h => h * 2); f.resize(50); assert.equal(f.window.innerHeight, 66);
});
check('same-hash clicks report anchor repeatedly', () => {
    const f = fixture(500);
    const event = { target: { nodeType: 1, closest: () => ({ getAttribute: () => '#bottom' }) } };
    f.fire('click', event); f.drain(); f.fire('click', event); f.drain();
    assert.equal(f.messages.filter(m => m.type === 'mdict-anchor').length, 2);
});
check('wheel line deltas forwarded and Ctrl-wheel ignored', () => {
    const f = fixture(500);
    f.fire('wheel', { deltaY: 3, deltaMode: 1, ctrlKey: false, preventDefault() {} });
    f.fire('wheel', { deltaY: 3, deltaMode: 1, ctrlKey: true, preventDefault() { assert.fail(); } });
    const scroll = f.messages.filter(m => m.type === 'mdict-scroll');
    assert.equal(scroll.length, 1); assert.equal(scroll[0].value, 48);
});
console.log('LayoutBridge: ' + count + ' checks passed');
