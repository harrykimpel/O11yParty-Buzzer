(function () {
    var storageKey = "o11yparty-theme";
    var root = document.documentElement;

    function persist(theme) {
        try {
            window.localStorage.setItem(storageKey, theme);
        }
        catch (_error) {
            // Ignore storage failures (private mode, blocked storage, etc.).
        }
    }

    function read() {
        try {
            return window.localStorage.getItem(storageKey);
        }
        catch (_error) {
            return null;
        }
    }

    function apply(theme) {
        root.setAttribute("data-theme", theme);
        root.style.colorScheme = theme;
        syncButtonLabels(theme);
    }

    function syncButtonLabels(theme) {
        var isLight = theme === "light";
        var nextTheme = isLight ? "Dark" : "Light";
        var buttons = document.querySelectorAll(".theme-toggle");

        for (var i = 0; i < buttons.length; i++) {
            var button = buttons[i];
            button.textContent = nextTheme;
            button.setAttribute("aria-label", "Switch to " + nextTheme + " theme");
            button.setAttribute("title", "Switch to " + nextTheme + " theme");
        }
    }

    function set(theme) {
        var normalizedTheme = theme === "light" ? "light" : "dark";
        apply(normalizedTheme);
        persist(normalizedTheme);
    }

    function toggle() {
        var currentTheme = root.getAttribute("data-theme") === "light" ? "light" : "dark";
        set(currentTheme === "dark" ? "light" : "dark");
    }

    var savedTheme = read();
    var initialTheme = savedTheme === "light" || savedTheme === "dark" ? savedTheme : "dark";
    apply(initialTheme);

    window.o11yTheme = {
        set: set,
        toggle: toggle,
        get: function () {
            return root.getAttribute("data-theme") || "dark";
        }
    };
})();
