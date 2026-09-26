(function () {
    "use strict";

    var existingWebView = window.chrome && window.chrome.webview;
    var hasAvaloniaBridge = typeof window.invokeCSharpAction === "function";

    // The original WPF host already implements the exact API consumed by the player.
    if (existingWebView && typeof existingWebView.addEventListener === "function" && !hasAvaloniaBridge) {
        return;
    }

    var messageHandlers = [];
    var audioUnlocked = false;
    var pendingBackgroundAudio = null;
    var activeBackgroundAudio = null;
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
            } else if (typeof window.invokeCSharpAction === "function") {
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

        if (data
            && data.type === "content"
            && data.placement === "background"
            && Array.isArray(data.content)
            && data.content.some(function (item) { return item && item.type === "audio"; })) {
            if (audioUnlocked) {
                playBackgroundAudio(data);
            } else {
                pendingBackgroundAudio = data;
                showAudioUnlockButton();
            }
            return;
        }

        dispatchHostMessage(data);
    };

    function dispatchHostMessage(data) {
        var event = { data: data };
        messageHandlers.slice().forEach(function (handler) {
            handler(event);
        });
    }

    function showAudioUnlockButton() {
        if (document.getElementById("siquester-audio-unlock")) {
            return;
        }

        var button = document.createElement("button");
        button.id = "siquester-audio-unlock";
        button.type = "button";
        button.textContent = "▶ Audio";
        button.title = "Play question audio";
        button.setAttribute("aria-label", button.title);
        button.style.position = "fixed";
        button.style.left = "50%";
        button.style.top = "50%";
        button.style.transform = "translate(-50%, -50%)";
        button.style.zIndex = "2147483647";
        button.style.padding = "14px 24px";
        button.style.border = "2px solid white";
        button.style.borderRadius = "8px";
        button.style.background = "#0d40cd";
        button.style.color = "white";
        button.style.font = "600 20px sans-serif";
        button.style.cursor = "pointer";
        button.addEventListener("click", function () {
            audioUnlocked = true;
            button.remove();
            webView.postMessage({ type: "siquesterAudioUnlocked" });
            var audioMessage = pendingBackgroundAudio;
            pendingBackgroundAudio = null;
            if (audioMessage) {
                // HTML media playback begins directly inside the trusted click. This avoids
                // WebKitGTK leaving the retained player's WebAudio context suspended.
                playBackgroundAudio(audioMessage);
            }
        }, { once: true });
        document.body.appendChild(button);
    }

    function playBackgroundAudio(message) {
        var item = message.content.find(function (contentItem) {
            return contentItem && contentItem.type === "audio";
        });

        if (!item || typeof item.value !== "string" || item.value.length === 0) {
            return;
        }

        if (activeBackgroundAudio) {
            activeBackgroundAudio.pause();
        }

        activeBackgroundAudio = new Audio(item.value);
        activeBackgroundAudio.preload = "auto";
        activeBackgroundAudio.addEventListener("ended", function () {
            activeBackgroundAudio = null;
        }, { once: true });
        activeBackgroundAudio.play().catch(function () {
            activeBackgroundAudio = null;
        });
    }

    // WKWebView installs invokeCSharpAction after page scripts have run.
    // The native host retries this handshake on NavigationCompleted.
    window.siquesterNotifyHostReady = function () {
        if (!window.siquesterBridgeReady
            && (nativePostMessage || typeof window.invokeCSharpAction === "function")) {
            webView.postMessage({ type: "siquesterBridgeReady" });
            window.siquesterBridgeReady = true;
        }
    };

    window.siquesterNotifyHostReady();
})();
