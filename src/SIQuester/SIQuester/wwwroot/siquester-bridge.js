(function () {
    "use strict";

    var existingWebView = window.chrome && window.chrome.webview;
    var hasAvaloniaBridge = typeof window.invokeCSharpAction === "function";

    // The original WPF host already implements the exact API consumed by the player.
    if (existingWebView && typeof existingWebView.addEventListener === "function" && !hasAvaloniaBridge) {
        return;
    }

    var messageHandlers = [];
    var nativePostMessage = existingWebView && typeof existingWebView.postMessage === "function"
        ? existingWebView.postMessage.bind(existingWebView)
        : null;
    var webView = {
        addEventListener: function (type, handler) {
            if (type === "message" && typeof handler === "function" && messageHandlers.indexOf(handler) < 0) {
                messageHandlers.push(handler);
            }
        },
        removeEventListener: function (type, handler) {
            if (type !== "message") {
                return;
            }

            var index = messageHandlers.indexOf(handler);
            if (index >= 0) {
                messageHandlers.splice(index, 1);
            }
        },
        postMessage: function (data) {
            if (nativePostMessage) {
                nativePostMessage(data);
            } else if (hasAvaloniaBridge) {
                window.invokeCSharpAction(data);
            }
        }
    };

    // Avalonia's Windows adapter also exposes chrome.webview. Replace the whole
    // host object so the retained player subscribes to this cross-engine facade,
    // while nativePostMessage continues to use the original WebView2 object.
    window.chrome = { webview: webView };

    window.siquesterReceiveHostMessage = function (message) {
        var data = typeof message === "string" ? JSON.parse(message) : message;
        var event = { data: data };
        messageHandlers.slice().forEach(function (handler) {
            handler(event);
        });
    };

    window.siquesterBridgeReady = true;
    window.invokeCSharpAction({ type: "siquesterBridgeReady" });
})();
