(function () {
    "use strict";

    const CONTENT_CLASS = "tinyx-content-mode";
    const HOME_CLASS = "tinyx-route-home";
    const COMPOSER_CLASS = "tinyx-home-composer";
    const HOME_NAVIGATION_CLASS = "tinyx-home-navigation";
    const REFRESH_DELAY_MS = 120;
    const MAX_PENDING_ROOTS = 48;
    const MAX_COMPOSER_ANCESTORS = 8;
    const ALLOWED_HOSTS = new Set(["x.com", "www.x.com", "twitter.com", "www.twitter.com"]);
    const LOGIN_MARKERS = '[data-testid="LoginForm_Login_Button"], input[autocomplete="current-password"], [role="dialog"] input[autocomplete="username"], #arkoseFrame';
    const ACCOUNT_MARKERS = '[data-testid="SideNav_AccountSwitcher_Button"], [data-testid="AppTabBar_Profile_Link"], [data-testid="SideNav_NewTweet_Button"]';
    const PRIMARY_COLUMN = '[data-testid="primaryColumn"]';
    const VIDEO_PLAYER = '[data-testid="videoPlayer"]';
    const AUTH_PATH = /^\/(?:i\/flow|login|signup|account\/access|i\/oauth2|oauth)(?:\/|$)/i;

    const isLocalFixture = window.__tinyXAllowLocalFixture === true && location.href === "about:blank";
    if (!ALLOWED_HOSTS.has(location.hostname.toLowerCase()) && !isLocalFixture) {
        return;
    }

    if (window.__tinyX && window.__tinyX.version === 1) {
        window.__tinyX.refresh();
        return;
    }

    let contentMode = true;
    let hostVisible = true;
    let refreshTimer = null;
    let started = false;
    let scanAllVideos = true;
    let approvedVideos = new WeakSet();
    let approvedPlayers = new WeakSet();
    const processedVideos = new WeakSet();
    const processedHomeNavigation = new WeakSet();
    const pendingRoots = new Set();

    function isPageVisible() {
        return hostVisible && !document.hidden;
    }

    function pauseVideo(video) {
        try {
            if (!video.paused) {
                video.pause();
            }
        } catch (_) {
            // A detached or incompletely initialised player may no longer be usable.
        }
    }

    function prepareVideo(video) {
        if (!processedVideos.has(video)) {
            processedVideos.add(video);
            video.autoplay = false;
            video.removeAttribute("autoplay");
            video.preload = "metadata";
        }
        const player = video.closest(VIDEO_PLAYER);
        const userApproved = approvedVideos.has(video) || (player && approvedPlayers.has(player));
        if (!isPageVisible() || !userApproved) {
            pauseVideo(video);
        }
    }

    function visitVideos(root, visit) {
        if (root.nodeType === 1 && root.tagName === "VIDEO") {
            visit(root);
        }
        if (typeof root.querySelectorAll === "function") {
            root.querySelectorAll("video").forEach(visit);
        }
    }

    function pauseAllVideos() {
        approvedVideos = new WeakSet();
        approvedPlayers = new WeakSet();
        visitVideos(document, pauseVideo);
    }

    function isAuthenticatedPage() {
        return !AUTH_PATH.test(location.pathname) &&
            !document.querySelector(LOGIN_MARKERS) &&
            !!document.querySelector(PRIMARY_COLUMN) &&
            !!document.querySelector(ACCOUNT_MARKERS);
    }

    function markHomeComposer() {
        const primary = document.querySelector(PRIMARY_COLUMN);
        if (!primary) {
            return;
        }
        const editor = primary.querySelector('[data-testid="tweetTextarea_0"]');
        if (!editor || editor.closest('[role="dialog"], [aria-modal="true"]')) {
            return;
        }
        let candidate = editor.parentElement;
        for (let depth = 0; candidate && candidate !== primary && depth < MAX_COMPOSER_ANCESTORS; depth += 1) {
            if (candidate.querySelector('article, [data-testid="tweet"]')) {
                return;
            }
            if (candidate.querySelector('[data-testid="tweetButtonInline"]')) {
                candidate.classList.add(COMPOSER_CLASS);
                return;
            }
            candidate = candidate.parentElement;
        }
    }

    function markHomeNavigation() {
        const primary = document.querySelector(PRIMARY_COLUMN);
        if (!primary) {
            return;
        }
        primary.querySelectorAll('[role="tablist"], [data-testid="TopNavBar"]').forEach(function (navigation) {
            if (processedHomeNavigation.has(navigation) ||
                navigation.closest('article, [role="dialog"], [aria-modal="true"]')) {
                return;
            }
            processedHomeNavigation.add(navigation);
            navigation.classList.add(HOME_NAVIGATION_CLASS);
            let candidate = navigation.parentElement;
            for (let depth = 0; candidate && candidate !== primary && depth < MAX_COMPOSER_ANCESTORS; depth += 1) {
                // Hiding the sticky wrapper also reclaims its reserved height; never include content.
                if (candidate.querySelector('article, [data-testid="tweet"], textarea, input, [contenteditable="true"]')) {
                    return;
                }
                if (getComputedStyle(candidate).position === "sticky") {
                    candidate.classList.add(HOME_NAVIGATION_CLASS);
                    return;
                }
                candidate = candidate.parentElement;
            }
        });
    }

    function refresh() {
        refreshTimer = null;
        const root = document.documentElement;
        if (!root) {
            return;
        }
        const active = contentMode && isAuthenticatedPage();
        const isHome = location.pathname === "/home" || (isLocalFixture && location.pathname === "blank");
        root.classList.toggle(CONTENT_CLASS, active);
        root.classList.toggle(HOME_CLASS, active && isHome);
        if (active && isHome) {
            markHomeComposer();
            markHomeNavigation();
        }

        if (scanAllVideos) {
            visitVideos(document, prepareVideo);
        } else {
            pendingRoots.forEach(function (node) {
                if (node.isConnected) {
                    visitVideos(node, prepareVideo);
                }
            });
        }
        pendingRoots.clear();
        scanAllVideos = false;
    }

    function queueRefresh(fullVideoScan) {
        scanAllVideos = scanAllVideos || fullVideoScan === true;
        if (refreshTimer === null) {
            refreshTimer = window.setTimeout(refresh, REFRESH_DELAY_MS);
        }
    }

    function approveUserPlayback(event) {
        if (!event.isTrusted || !isPageVisible()) {
            return;
        }
        if (event.type === "keydown" && event.key !== " " && event.key !== "Enter") {
            return;
        }
        const target = event.target;
        if (!target || target.nodeType !== 1) {
            return;
        }
        const player = target.closest(VIDEO_PLAYER) || (target.tagName === "VIDEO" ? target : null);
        if (player) {
            approvedPlayers.add(player);
            visitVideos(player, function (video) {
                approvedVideos.add(video);
            });
        }
    }

    function onPlay(event) {
        const video = event.target;
        if (video && video.tagName === "VIDEO") {
            prepareVideo(video);
        }
    }

    function onVisibilityChange() {
        if (!isPageVisible()) {
            pauseAllVideos();
        }
    }

    function start() {
        if (started || !document.documentElement) {
            return;
        }
        started = true;
        const observer = new MutationObserver(function (records) {
            records.forEach(function (record) {
                record.addedNodes.forEach(function (node) {
                    if (node.nodeType !== 1) {
                        return;
                    }
                    if (pendingRoots.size < MAX_PENDING_ROOTS) {
                        pendingRoots.add(node);
                    } else {
                        scanAllVideos = true;
                    }
                });
            });
            queueRefresh(false);
        });
        observer.observe(document.documentElement, { childList: true, subtree: true });
        document.addEventListener("pointerdown", approveUserPlayback, true);
        document.addEventListener("keydown", approveUserPlayback, true);
        document.addEventListener("play", onPlay, true);
        document.addEventListener("visibilitychange", onVisibilityChange);
        window.addEventListener("popstate", function () { queueRefresh(true); });
        window.addEventListener("hashchange", function () { queueRefresh(true); });
        ["pushState", "replaceState"].forEach(function (method) {
            const original = history[method];
            history[method] = function () {
                const result = original.apply(this, arguments);
                queueRefresh(true);
                return result;
            };
        });
        refresh();
    }

    window.__tinyX = Object.freeze({
        version: 1,
        setContentMode: function (enabled) {
            contentMode = !!enabled;
            queueRefresh(false);
        },
        setPageVisible: function (visible) {
            hostVisible = !!visible;
            onVisibilityChange();
        },
        refresh: function () {
            queueRefresh(true);
        }
    });

    if (document.documentElement) {
        start();
    } else {
        document.addEventListener("DOMContentLoaded", start, { once: true });
    }
}());
