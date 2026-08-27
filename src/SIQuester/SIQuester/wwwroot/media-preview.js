(function () {
    "use strict";

    var fragment = window.location.hash.substring(1);
    var separator = fragment.indexOf("=");
    if (separator <= 0) {
        return;
    }

    var kind = fragment.substring(0, separator);
    if (kind !== "audio" && kind !== "video") {
        return;
    }

    var source;
    try {
        source = decodeURIComponent(fragment.substring(separator + 1));
    } catch (_) {
        return;
    }

    var mediaUrl;
    try {
        mediaUrl = new URL(source);
    } catch (_) {
        return;
    }

    if (mediaUrl.origin !== window.location.origin || mediaUrl.pathname.indexOf("/media/") < 0) {
        return;
    }

    var media = document.createElement(kind);
    media.controls = true;
    media.preload = "metadata";
    media.src = mediaUrl.href;
    document.body.appendChild(media);
})();
