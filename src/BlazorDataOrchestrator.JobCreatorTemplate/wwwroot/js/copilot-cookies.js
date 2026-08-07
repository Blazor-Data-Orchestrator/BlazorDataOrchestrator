/**
 * Cookie helper functions for Copilot settings persistence.
 * Used by CopilotCookieService via JS interop.
 */
window.copilotCookies = {
    /**
     * Gets a cookie value by name.
     * @param {string} name - The cookie name.
     * @returns {string|null} The cookie value or null if not found.
     */
    get: function(name) {
        const value = `; ${document.cookie}`;
        const parts = value.split(`; ${name}=`);
        if (parts.length === 2) {
            return parts.pop().split(';').shift();
        }
        return null;
    },

    /**
     * Sets a cookie with the specified name, value, and expiry.
     * @param {string} name - The cookie name.
     * @param {string} value - The cookie value.
     * @param {number} days - Number of days until expiry.
     */
    set: function(name, value, days) {
        const expires = new Date();
        expires.setTime(expires.getTime() + (days * 24 * 60 * 60 * 1000));
        const secure = location.protocol === 'https:' ? ';Secure' : '';
        document.cookie = `${name}=${encodeURIComponent(value)};expires=${expires.toUTCString()};path=/;SameSite=Strict${secure}`;
    },

    /**
     * Deletes a cookie by name.
     * @param {string} name - The cookie name to delete.
     */
    remove: function(name) {
        document.cookie = `${name}=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;SameSite=Strict`;
    }
};
