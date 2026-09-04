window.communityAuth = {
    // Opens the CJL sign-in popup and resolves when the callback page posts back.
    signIn: function (url) {
        return new Promise(function (resolve) {
            var width = 520;
            var height = 700;
            var left = window.screenX + (window.outerWidth - width) / 2;
            var top = window.screenY + (window.outerHeight - height) / 2;

            var popup = window.open(url, 'cjl-signin',
                'width=' + width + ',height=' + height + ',left=' + left + ',top=' + top +
                ',resizable=yes,scrollbars=yes');

            if (!popup) {
                resolve({ status: 'blocked', message: 'The sign-in window was blocked by your browser.' });
                return;
            }

            var settled = false;

            function finish(result) {
                if (settled) { return; }
                settled = true;
                window.removeEventListener('message', onMessage);
                clearInterval(closedTimer);
                resolve(result);
            }

            function onMessage(event) {
                if (event.origin !== window.location.origin) { return; }
                if (!event.data || event.data.source !== 'cjl-auth') { return; }
                finish({ status: event.data.status, message: event.data.message });
            }

            window.addEventListener('message', onMessage);

            var closedTimer = setInterval(function () {
                if (popup.closed) {
                    finish({ status: 'cancelled', message: 'The sign-in window was closed.' });
                }
            }, 500);
        });
    },

    copyText: function (text) {
        if (navigator.clipboard) {
            return navigator.clipboard.writeText(text);
        }
        return Promise.resolve();
    }
};
