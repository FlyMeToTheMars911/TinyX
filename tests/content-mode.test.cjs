"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../src/TinyX/Assets/content.js"), "utf8");
const css = fs.readFileSync(path.join(__dirname, "../src/TinyX/Assets/content.css"), "utf8");

function makeElement(tagName = "DIV") {
    const classes = new Set();
    const attributes = new Map();
    const events = new Map();
    return {
        nodeType: 1, tagName, classes, attributes, children: [], isConnected: true,
        classList: {
            add(name) { classes.add(name); },
            remove(name) { classes.delete(name); },
            contains(name) { return classes.has(name); },
            toggle(name, enabled) { enabled ? classes.add(name) : classes.delete(name); }
        },
        setAttribute(name, value) { attributes.set(name, value); },
        addEventListener(name, listener) { events.set(name, listener); },
        dispatch(name, event) { if (events.has(name)) events.get(name)(event); },
        appendChild(child) { child.parentElement = this; this.children.push(child); },
        contains(target) {
            for (let node = target; node; node = node.parentElement) if (node === this) return true;
            return false;
        },
        querySelector() { return null; },
        querySelectorAll() { return []; },
        getClientRects() { return [{}]; },
        closest() { return null; },
        matches() { return false; }
    };
}

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
    const primary = makeElement();
    primary.querySelector = selector => {
        if (selector.includes("tweetTextarea_")) return state.editor || null;
        if (selector === '[role="tablist"]') return state.homeTabs || null;
        return null;
    };
    const document = {
        documentElement: root,
        scrollingElement: root,
        body: makeElement("BODY"),
        hidden: false,
        querySelector(selector) {
            if (selector === '[data-testid="primaryColumn"]') return state.primary ? primary : null;
            if (selector.includes("LoginForm_Login_Button")) return state.authForm ? {} : null;
            if (selector.includes("SideNav_AccountSwitcher_Button")) return state.authenticated ? {} : null;
            if (selector === 'header[role="banner"]') return state.header || null;
            if (selector === '[role="dialog"], [aria-modal="true"]') return state.modal ? {} : null;
            return null;
        },
        createElement(tagName) { return makeElement(tagName.toUpperCase()); },
        querySelectorAll(selector) {
            if (selector === "video") return videos;
            if (selector === '[role="dialog"], [aria-modal="true"]' && state.modal) {
                return [state.modal === true ? makeElement() : state.modal];
            }
            return [];
        },
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
        getComputedStyle(node) { return { position: node.position || "static", visibility: node.visibility || "visible" }; },
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

test("home composer supports the current tweetButton while preserving tabs and virtual-list containers", () => {
    const page = createPage();
    const editor = makeElement();
    const inner = makeElement();
    const composer = makeElement();
    const timelineWrapper = makeElement();
    editor.parentElement = inner;
    inner.parentElement = composer;
    composer.parentElement = timelineWrapper;
    timelineWrapper.parentElement = page.primary;
    inner.querySelector = selector => selector.includes("tweetButton") ? {} : null;
    composer.querySelector = selector => selector.includes("tweetButton") ? {} : null;
    timelineWrapper.querySelector = selector => selector.includes("article") ? {} : null;
    page.state.editor = editor;
    page.execute();
    assert(composer.classes.has("tinyx-home-composer"));
    assert(!inner.classes.has("tinyx-home-composer"), "choose the highest safe composer wrapper");
    assert(!timelineWrapper.classes.has("tinyx-home-composer"), "never hide an ancestor containing timeline content");
    page.history.pushState(null, "", "/someone/status/123");
    page.flush();
    assert(!composer.classes.has("tinyx-home-composer"), "reply composers remain available on a detail route");

    const protectedPage = createPage();
    const protectedComposer = makeElement();
    protectedComposer.parentElement = protectedPage.primary;
    protectedComposer.matches = selector => selector.includes("cellInnerDiv");
    protectedComposer.querySelector = () => ({});
    const protectedEditor = makeElement();
    protectedEditor.parentElement = protectedComposer;
    protectedPage.state.editor = protectedEditor;
    protectedPage.execute();
    assert(!protectedComposer.classes.has("tinyx-home-composer"), "a virtual cell itself is never hidden");
    protectedComposer.matches = () => false;
    protectedEditor.closest = () => ({});
    protectedPage.mutate();
    protectedPage.flush();
    assert(!protectedComposer.classes.has("tinyx-home-composer"), "a dialog composer remains intact");

    protectedEditor.closest = selector => selector.includes("article") ? {} : null;
    protectedPage.mutate();
    protectedPage.flush();
    assert(!protectedComposer.classes.has("tinyx-home-composer"), "an inline reply inside a home-timeline tweet is preserved");
});

test("document scrolling is selected without unlocking native dialog scrolling", () => {
    const page = createPage();
    page.execute();
    assert(page.classes.has("tinyx-document-scroll"));
    assert(!page.classes.has("tinyx-modal-open"));
    page.state.modal = true;
    page.mutate();
    page.flush();
    assert(page.classes.has("tinyx-modal-open"), "CSS scrolling overrides are disabled while X owns a modal");
    const cachedDialog = makeElement();
    cachedDialog.getClientRects = () => [];
    page.state.modal = cachedDialog;
    page.mutate();
    page.flush();
    assert(!page.classes.has("tinyx-modal-open"), "a cached hidden dialog must not disable the mini-window layout");
    page.state.modal = false;
    page.document.scrollingElement = page.document.body;
    page.mutate();
    page.flush();
    assert(!page.classes.has("tinyx-document-scroll"), "do not guess a different existing scroll root");
});

test("deep rich-text wrappers do not prevent locating the separate home composer controls", () => {
    const page = createPage();
    const editor = makeElement();
    let descendant = editor;
    let outermost;
    for (let depth = 0; depth < 29; depth += 1) {
        const wrapper = makeElement();
        descendant.parentElement = wrapper;
        wrapper.querySelector = selector => depth >= 24 && selector.includes("tweetButton") ? {} : null;
        descendant = wrapper;
        outermost = wrapper;
    }
    outermost.parentElement = page.primary;
    page.state.editor = editor;
    page.execute();
    assert(outermost.classes.has("tinyx-home-composer"));
});

test("composer attachment tabs do not hide deep publish controls or the separate home timeline tabs", () => {
    const page = createPage();
    const editor = makeElement();
    const homeTabs = makeElement();
    const composerTabs = makeElement();
    const wrappers = [];
    let descendant = editor;
    for (let depth = 1; depth < 30; depth += 1) {
        const wrapper = makeElement();
        descendant.parentElement = wrapper;
        wrapper.querySelector = selector => {
            if (selector.includes("tweetButton")) return depth >= 21 ? { testId: "tweetButtonInline" } : null;
            if (selector.includes('[role="tablist"]')) return depth >= 20 ? composerTabs : null;
            return null;
        };
        wrappers.push(wrapper);
        descendant = wrapper;
    }
    wrappers[28].parentElement = page.primary;
    homeTabs.parentElement = wrappers[27];
    composerTabs.parentElement = wrappers[19];
    page.state.editor = editor;
    page.state.homeTabs = homeTabs;
    page.execute();
    assert(wrappers[26].classes.has("tinyx-home-composer"), "the composer wrapper before the home-tabs ancestor is hidden");
    assert(!wrappers[27].classes.has("tinyx-home-composer"), "the ancestor containing the first home tablist remains intact");
    assert(!homeTabs.classes.has("tinyx-home-composer"));
});

test("compact mode hides the native header without adding navigation controls and full mode restores it", () => {
    const page = createPage();
    const header = makeElement("HEADER");
    page.state.header = header;
    page.execute();
    assert(page.classes.has("tinyx-content-mode"));
    assert(css.includes('html.tinyx-content-mode header[role="banner"]:not(:has([role="dialog"], [aria-modal="true"]))'),
        "header hiding is scoped to compact mode and preserves a contained dialog");
    assert.equal(page.document.body.children.length, 0, "no navigation portal or trigger is injected");
    assert.equal(page.window.__tinyX.setNavigationOpen, undefined);
    assert.equal(page.listenerCount("pointerover"), 0);
    assert.equal(page.listenerCount("click"), 0);
    assert.equal(header.classes.size, 0, "the original header has no overlay classes or inline mutations");
    page.window.__tinyX.setContentMode(false);
    page.flush();
    assert(!page.classes.has("tinyx-content-mode"), "full mode disables the scoped header hiding rule");
    page.window.__tinyX.setContentMode(true);
    page.flush();
    page.state.authForm = true;
    page.mutate();
    page.flush();
    assert(!page.classes.has("tinyx-content-mode"), "authentication also retains the complete native layout");
    assert.equal(page.document.body.children.length, 0);
});
