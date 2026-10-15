(function () {
    'use strict';

    const storageKey = 'cm_theme';
    const darkTheme = 'dark';
    const lightTheme = 'light';

    function activeTheme() {
        return document.documentElement.dataset.theme === darkTheme ? darkTheme : lightTheme;
    }

    function syncThemeControls() {
        const isDark = activeTheme() === darkTheme;
        const action = isDark ? 'Включить светлую тему' : 'Включить тёмную тему';
        document.querySelectorAll('[data-theme-toggle]').forEach(button => {
            button.setAttribute('aria-label', action);
            button.setAttribute('title', action);
            button.setAttribute('aria-pressed', isDark ? 'true' : 'false');
        });

        const themeColor = document.querySelector('meta[name="theme-color"]');
        if (themeColor) themeColor.setAttribute('content', isDark ? '#111315' : '#f0f3f5');
    }

    function applyTheme(theme, persist) {
        const normalized = theme === darkTheme ? darkTheme : lightTheme;
        document.documentElement.dataset.theme = normalized;
        document.documentElement.style.colorScheme = normalized;
        if (persist) {
            try { localStorage.setItem(storageKey, normalized); } catch (_) { }
        }
        syncThemeControls();
    }

    window.toggleColorTheme = function () {
        applyTheme(activeTheme() === darkTheme ? lightTheme : darkTheme, true);
    };

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', syncThemeControls, { once: true });
    else
        syncThemeControls();
})();
