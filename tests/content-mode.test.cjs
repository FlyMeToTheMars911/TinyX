"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../src/TinyX/Assets/content.js"), "utf8");

function createPage(options = {}) {
    const classes = new Set();
    const events = new Map();
    const timers = new Map();
    const videos = [];
    const state = { authenticated: true, authForm: false, primary: true, ...options };
    const location = new URL(options.url || "https://x.com/home");
    let nextTimer = 0;
    let observerCount = 0;
    let mutationCallback;
    const root = {
        classList: {
            toggle(name, enabled) { enabled ? classes.add(name) : classes.delete(name); }
        }
    };
    const primary = {
        querySelector() { return null; },
        querySelectorAll() { return state.homeNavigation || []; }
    };
    const document = {
        documentElement: root,
        hidden: false,
        querySelector(selector) {
            if (selector === '[data-testid="primaryColumn"]') return state.primary ? primary : null;
            if (selector.includes("LoginForm_Login_Button")) return state.authForm ? {} : null;
            if (selector.includes("SideNav_AccountSwitcher_Button")) return state.authenticated ? {} : null;
            return null;
        },
        querySelectorAll(selector) { return selector === "video" ? videos : []; },
        addEventListener(name, listener) {
            if (!events.has(name)) events.set(name, []);
            events.get(name).push(listener);
        }
    };
    const history = {
        pushState(_state, _title, target) {
            const updated = new URL(target, location.href);
            location.href = updated.href;
        },
        replaceState(_state, _title, target) {
            const updated = new URL(target, location.href);
            location.href = updated.href;
        }
    };
    const window = {
        __tinyXAllowLocalFixture: options.localFixture === true,
        setTimeout(callback) { const id = ++nextTimer; timers.set(id, callback); return id; },
        addEventListener: document.addEventListener,
    };
    const context = vm.createContext({
        window, document, location, history,
        getComputedStyle(node) { return { position: node.position || "static" }; },
        MutationObserver: class {
            constructor(callback) { mutationCallback = callback; observerCount += 1; }
            observe() {}
        }
    });

    function flush() {
        // A runaway observer/timer chain is itself a failure, rather than an endless test.
        for (let cycle = 0; timers.size && cycle < 10; cycle += 1) {
            const callbacks = Array.from(timers.values());
            timers.clear();
            callbacks.forEach(callback => callback());
        }
        assert.equal(timers.size, 0, "refresh must finish without polling");
    }

    function dispatch(name, event = {}) {
        (events.get(name) || []).forEach(listener => listener(event));
    }

    function makeVideo() {
        let metadataWrites = 0;
        const video = {
            nodeType: 1,
            tagName: "VIDEO",
            isConnected: true,
            paused: false,
            autoplay: true,
            pauseCalls: 0,
            pause() { this.paused = true; this.pauseCalls += 1; },
            removeAttribute(name) { if (name === "autoplay") this.autoplay = false; },
            querySelectorAll() { return []; },
            closest() { return this; },
            set preload(value) { assert.equal(value, "metadata"); metadataWrites += 1; },
            get metadataWrites() { return metadataWrites; }
        };
        videos.push(video);
        return video;
    }

    return {
        state, window, document, history, primary, classes, context, videos, flush, dispatch, makeVideo,
        execute() { vm.runInContext(script, context); },
        mutate(nodes = []) { mutationCallback([{ addedNodes: nodes }]); },
        get observerCount() { return observerCount; },
        listenerCount(name) { return (events.get(name) || []).length; },
        get timerCount() { return timers.size; }
    };
}

test("content mode follows user preference while SPA authentication screens keep normal layout", () => {
    const page = createPage();
    page.execute();
    assert(page.classes.has("tinyx-content-mode"));
    assert(page.classes.has("tinyx-route-home"));

    page.history.pushState(null, "", "/i/flow/login");
    page.flush();
    assert(!page.classes.has("tinyx-content-mode"));

    page.history.replaceState(null, "", "/someone/status/123");
    page.flush();
    assert(page.classes.has("tinyx-content-mode"));
    assert(!page.classes.has("tinyx-route-home"));

    page.state.authForm = true;
    page.mutate();
    page.flush();
    assert(!page.classes.has("tinyx-content-mode"));
    page.state.authForm = false;
    page.window.__tinyX.setContentMode(false);
    page.flush();
    assert(!page.classes.has("tinyx-content-mode"));
    page.window.__tinyX.setContentMode(true);
    page.flush();
    assert(page.classes.has("tinyx-content-mode"));
    page.state.authenticated = false;
    page.mutate();
    page.flush();
    assert(!page.classes.has("tinyx-content-mode"));
});

test("domain gate excludes other sites and requires an explicit about:blank fixture flag", () => {
    for (const options of [
        { url: "https://example.com/home" },
        { url: "https://x.com.evil.example/home" },
        { url: "about:blank" },
        { url: "https://example.com/", localFixture: true }
    ]) {
        const page = createPage(options);
        page.execute();
        assert.equal(page.window.__tinyX, undefined);
        assert.equal(page.observerCount, 0);
    }
    const fixture = createPage({ url: "about:blank", localFixture: true });
    fixture.execute();
    assert(fixture.classes.has("tinyx-content-mode"));
    const twitter = createPage({ url: "https://twitter.com/home" });
    twitter.execute();
    assert(twitter.classes.has("tinyx-content-mode"));
});

test("repeated injection preserves preference and registers one observer and each event only once", () => {
    const page = createPage();
    page.execute();
    const api = page.window.__tinyX;
    page.window.__tinyX.setContentMode(false);
    page.flush();
    page.execute();
    page.execute();
    page.flush();
    assert.equal(page.window.__tinyX, api);
    assert.equal(page.observerCount, 1);
    for (const name of ["pointerdown", "keydown", "play", "visibilitychange", "popstate", "hashchange"]) {
        assert.equal(page.listenerCount(name), 1, name);
    }
    assert(!page.classes.has("tinyx-content-mode"));
});

test("autoplay is paused, manual playback works, and hiding revokes permission without resuming", () => {
    const page = createPage();
    const video = page.makeVideo();
    page.execute();
    assert.equal(video.paused, true);
    assert.equal(video.autoplay, false);
    assert.equal(video.metadataWrites, 1);

    video.paused = false;
    page.dispatch("play", { target: video });
    assert.equal(video.paused, true, "programmatic autoplay stays blocked");
    page.dispatch("pointerdown", { target: video, isTrusted: true });
    video.paused = false;
    page.dispatch("play", { target: video });
    assert.equal(video.paused, false, "a user may play video normally");

    page.window.__tinyX.setPageVisible(false);
    assert.equal(video.paused, true);
    const previousPauseCalls = video.pauseCalls;
    page.window.__tinyX.setPageVisible(true);
    assert.equal(video.paused, true, "returning to the window must not start video");
    assert.equal(video.pauseCalls, previousPauseCalls);
    video.paused = false;
    page.dispatch("play", { target: video });
    assert.equal(video.paused, true, "hiding revoked the earlier playback permission");

    page.dispatch("keydown", { target: video, isTrusted: true, key: "Enter" });
    video.paused = false;
    page.dispatch("play", { target: video });
    assert.equal(video.paused, false);
    page.document.hidden = true;
    page.dispatch("visibilitychange");
    assert.equal(video.paused, true);
    assert.equal(video.metadataWrites, 1, "existing video is configured once");
});

test("new videos are prepared in coalesced mutation work without repeated setup or polling", () => {
    const page = createPage();
    page.execute();
    const video = page.makeVideo();
    for (let i = 0; i < 150; i += 1) page.mutate([video]);
    assert.equal(page.timerCount, 1);
    page.flush();
    assert.equal(video.paused, true);
    assert.equal(video.metadataWrites, 1);
    page.mutate([video]);
    page.flush();
    assert.equal(video.metadataWrites, 1);
    assert.equal(page.timerCount, 0);
});

test("clicking a player preview permits a video created after that click", () => {
    const page = createPage();
    page.execute();
    const player = { nodeType: 1, closest() { return this; }, querySelectorAll() { return []; } };
    page.dispatch("pointerdown", { target: player, isTrusted: true });
    const video = page.makeVideo();
    video.closest = () => player;
    page.dispatch("play", { target: video });
    assert.equal(video.paused, false, "late-created media preserves the explicit play request");
    page.window.__tinyX.setPageVisible(false);
    assert.equal(video.paused, true);
});

test("home tabs reclaim an empty sticky header without hiding the timeline or detail navigation", () => {
    const page = createPage();
    const stickyClasses = new Set();
    const tabClasses = new Set();
    const stickyHeader = {
        parentElement: page.primary,
        position: "sticky",
        classList: { add(name) { stickyClasses.add(name); } },
        querySelector() { return null; }
    };
    const tabs = {
        parentElement: stickyHeader,
        classList: { add(name) { tabClasses.add(name); } },
        closest() { return null; }
    };
    page.state.homeNavigation = [tabs];
    page.execute();
    assert(stickyClasses.has("tinyx-home-navigation"));
    assert(tabClasses.has("tinyx-home-navigation"));
    assert(page.classes.has("tinyx-route-home"));
    page.history.pushState(null, "", "/someone/status/123");
    page.flush();
    assert(!page.classes.has("tinyx-route-home"), "home-only hiding must not affect detail headers");

    const protectedPage = createPage();
    const protectedClasses = new Set();
    const contentWrapper = {
        parentElement: protectedPage.primary,
        position: "sticky",
        classList: { add(name) { protectedClasses.add(name); } },
        querySelector() { return {}; }
    };
    protectedPage.state.homeNavigation = [{
        parentElement: contentWrapper,
        classList: { add() {} },
        closest() { return null; }
    }];
    protectedPage.execute();
    assert.equal(protectedClasses.size, 0, "an ancestor containing a tweet or input is never hidden");
});
