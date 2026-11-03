(function () {
    "use strict";

    const CONTENT_CLASS = "tinyx-content-mode";
    const HOME_CLASS = "tinyx-route-home";
    const COMPOSER_CLASS = "tinyx-home-composer";
    const HOME_HEADER_CLASS = "tinyx-home-header";
    const DOCUMENT_SCROLL_CLASS = "tinyx-document-scroll";
    const MODAL_CLASS = "tinyx-modal-open";
    const LAYOUT_CLASS = "tinyx-content-row";
    const REFRESH_DELAY_MS = 120;
    const MAX_PENDING_ROOTS = 48;
    const MAX_COMPOSER_ANCESTORS = 40;
    const MAX_LAYOUT_ANCESTORS = 5;
    const MAX_HOME_HEADER_ANCESTORS = 12;
    const ALLOWED_HOSTS = new Set(["x.com", "www.x.com", "twitter.com", "www.twitter.com"]);
    const LOGIN_MARKERS = '[data-testid="LoginForm_Login_Button"], input[autocomplete="current-password"], [role="dialog"] input[autocomplete="username"], #arkoseFrame';
    const ACCOUNT_MARKERS = '[data-testid="SideNav_AccountSwitcher_Button"], [data-testid="AppTabBar_Profile_Link"], [data-testid="SideNav_NewTweet_Button"]';
    const PRIMARY_COLUMN = '[data-testid="primaryColumn"]';
    const VIDEO_PLAYER = '[data-testid="videoPlayer"]';
    const MODAL_MARKERS = '[role="dialog"], [aria-modal="true"]';
    const COMPOSER_BUTTONS = '[data-testid="tweetButtonInline"], [data-testid="tweetButton"]';
    const COMPOSER_BOUNDARIES = 'article, [data-testid="tweet"], [data-testid="cellInnerDiv"], [data-testid="virtualScroller"], [role="list"], [role="region"], section, main, [role="dialog"], [aria-modal="true"]';
    const REPLY_OR_DIALOG_ANCESTORS = 'article, [data-testid="tweet"], [data-testid="cellInnerDiv"], [data-testid="virtualScroller"], [role="dialog"], [aria-modal="true"]';
    const HOME_HEADER_BOUNDARIES = 'article, [data-testid="tweet"], [data-testid="cellInnerDiv"], [data-testid="virtualScroller"], [data-testid="virtualizedList"], [role="region"], [role="list"], section, [data-testid^="tweetTextarea_"], [role="dialog"], [aria-modal="true"]';
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
    const pendingRoots = new Set();
    let markedComposer = null;
    let markedHomeHeaders = [];
    let layoutWrappers = [];

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

    function hasVisibleModal() {
        const dialogs = document.querySelectorAll(MODAL_MARKERS);
        for (const dialog of dialogs) {
            if (dialog.getClientRects().length && getComputedStyle(dialog).visibility !== "hidden") {
                return true;
            }
        }
        return false;
    }

    function markHomeComposer() {
        const primary = document.querySelector(PRIMARY_COLUMN);
        const editor = primary && primary.querySelector('[data-testid^="tweetTextarea_"]');
        const homeTabs = primary && primary.querySelector('[role="tablist"]');
        let safeContainer = null;
        if (editor && !editor.closest(REPLY_OR_DIALOG_ANCESTORS)) {
            let candidate = editor.parentElement;
            for (let depth = 0; candidate && candidate !== primary && depth < MAX_COMPOSER_ANCESTORS; depth += 1) {
                // The composer has its own attachment tabs. Protect the first home
                // timeline tabs without treating those inner controls as a boundary.
                if ((homeTabs && candidate.contains(homeTabs)) ||
                    candidate.matches(COMPOSER_BOUNDARIES) || candidate.querySelector(COMPOSER_BOUNDARIES)) {
                    break;
                }
                if (candidate.querySelector(COMPOSER_BUTTONS)) {
                    safeContainer = candidate;
                }
                candidate = candidate.parentElement;
            }
        }
        if (markedComposer && markedComposer !== safeContainer) {
            markedComposer.classList.remove(COMPOSER_CLASS);
        }
        markedComposer = safeContainer;
        if (markedComposer) {
            markedComposer.classList.add(COMPOSER_CLASS);
        }
    }

    function markContentWrappers() {
        const primary = document.querySelector(PRIMARY_COLUMN);
        const nextWrappers = [];
        let candidate = primary && primary.parentElement;
        for (let depth = 0; candidate && depth < MAX_LAYOUT_ANCESTORS; depth += 1) {
            if (candidate.matches('main, [role="main"]')) {
                break;
            }
            if (candidate.tagName !== "DIV" || candidate.matches('[data-testid="cellInnerDiv"], [data-testid="virtualScroller"], [role="region"], [role="list"]')) {
                break;
            }
            candidate.classList.add(LAYOUT_CLASS);
            nextWrappers.push(candidate);
            candidate = candidate.parentElement;
        }
        layoutWrappers.forEach(function (wrapper) {
            if (!nextWrappers.includes(wrapper)) {
                wrapper.classList.remove(LAYOUT_CLASS);
            }
        });
        layoutWrappers = nextWrappers;
    }

    function updateHomeHeader(active) {
        const nextHeaders = [];
        const primary = active && document.querySelector(PRIMARY_COLUMN);
        const tabs = primary && primary.querySelector('[role="tablist"]');
        if (tabs && !tabs.closest(REPLY_OR_DIALOG_ANCESTORS + ", ." + COMPOSER_CLASS)) {
            let candidate = tabs;
            for (let depth = 0; candidate && candidate !== primary && depth < MAX_HOME_HEADER_ANCESTORS; depth += 1) {
                if (candidate.matches(HOME_HEADER_BOUNDARIES) || candidate.querySelector(HOME_HEADER_BOUNDARIES)) {
                    break;
                }
                const position = getComputedStyle(candidate).position;
                // Sticky headers already occupy document space. Leave fixed
                // layouts alone unless their separate placeholder is understood.
                if (position === "sticky" || candidate.classList.contains(HOME_HEADER_CLASS)) {
                    candidate.classList.add(HOME_HEADER_CLASS);
                    nextHeaders.push(candidate);
                }
                candidate = candidate.parentElement;
            }
        }
        markedHomeHeaders.forEach(function (header) {
            if (!nextHeaders.includes(header)) {
                header.classList.remove(HOME_HEADER_CLASS);
            }
        });
        markedHomeHeaders = nextHeaders;
    }

    function refresh() {
        refreshTimer = null;
        const root = document.documentElement;
        if (!root) {
            return;
        }
        const active = contentMode && isAuthenticatedPage();
        const modalOpen = hasVisibleModal();
        const isHome = location.pathname === "/home" || (isLocalFixture && location.pathname === "blank");
        root.classList.toggle(CONTENT_CLASS, active);
        root.classList.toggle(HOME_CLASS, active && isHome);
        root.classList.toggle(DOCUMENT_SCROLL_CLASS, active && document.scrollingElement === root);
        root.classList.toggle(MODAL_CLASS, active && modalOpen);
        if (active) {
            markContentWrappers();
        }
        if (active && isHome) {
            markHomeComposer();
        } else if (markedComposer) {
            markedComposer.classList.remove(COMPOSER_CLASS);
            markedComposer = null;
        }
        updateHomeHeader(active && isHome);

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
