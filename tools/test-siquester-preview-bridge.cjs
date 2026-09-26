const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const source = readFileSync(join(__dirname, '../src/SIQuester/SIQuester/wwwroot/siquester-bridge.js'), 'utf8');
const load = window => vm.runInNewContext(source, { window });

test('macOS: late native bridge enables readiness and bidirectional player messages', () => {
    const window = {};
    const received = [];
    load(window);
    assert.equal(window.siquesterBridgeReady, undefined);

    // WKWebView installs this function only after page scripts have run.
    window.invokeCSharpAction = message => received.push(message);
    window.siquesterNotifyHostReady();
    window.siquesterNotifyHostReady();
    assert.equal(window.siquesterBridgeReady, true);
    assert.deepEqual(received.map(message => message.type), ['siquesterBridgeReady']);

    window.chrome.webview.postMessage({ type: 'contentLoaded' });
    assert.equal(received[1].type, 'contentLoaded');
    let fragment;
    window.chrome.webview.addEventListener('message', event => { fragment = event.data; });
    window.siquesterReceiveHostMessage(JSON.stringify({ type: 'content', text: 'Вопрос' }));
    assert.equal(fragment.text, 'Вопрос');
});

test('Linux: an early native bridge is ready immediately', () => {
    const received = [];
    const window = { invokeCSharpAction: message => received.push(message) };
    load(window);
    assert.equal(window.siquesterBridgeReady, true);
    assert.equal(received[0].type, 'siquesterBridgeReady');
    window.chrome.webview.postMessage({ type: 'contentLoaded' });
    assert.equal(received[1].type, 'contentLoaded');
});

test('Windows Avalonia: readiness and messages use the original native transport', () => {
    const received = [];
    const window = {
        chrome: { webview: { postMessage: message => received.push(message) } },
        invokeCSharpAction: () => { throw new Error('Use the existing native transport'); }
    };
    load(window);
    assert.equal(received[0].type, 'siquesterBridgeReady');
    window.chrome.webview.postMessage({ type: 'contentLoaded' });
    assert.equal(received[1].type, 'contentLoaded');
});

test('WPF: preserve the existing player integration', () => {
    const native = { addEventListener() {}, postMessage() {} };
    const window = { chrome: { webview: native } };
    load(window);
    assert.equal(window.chrome.webview, native);
    assert.equal(window.siquesterNotifyHostReady, undefined);
});
