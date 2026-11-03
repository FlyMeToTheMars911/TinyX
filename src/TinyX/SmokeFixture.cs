namespace TinyX;

internal static class SmokeFixture
{
    public const string Html = """
        <!doctype html><html style="height:100%;overflow-y:auto"><head><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>TinyX local fixture</title><style>
        body { background: #000; color: #e7e9ea; font: 15px sans-serif; }
        main > div { display: flex; }
        article { box-sizing: border-box; min-height: 420px; padding: 24px 12px; border-bottom: 1px solid #333; }
        [role="tablist"] { height: 53px; display: flex; }
        [role="tab"] { flex: 1; }
        </style></head><body style="overflow-x:hidden">
        <div id="react-root"><div><div>
        <header role="banner"><nav><button data-testid="SideNav_AccountSwitcher_Button">Fixture account</button></nav></header>
        <main role="main"><div><div data-testid="primaryColumn" style="width:600px;min-width:600px">
        <div id="fixture-home-tabs" style="position:sticky;top:0;z-index:3"><div role="tablist"><button role="tab" aria-selected="true">For you</button><button role="tab">Following</button></div></div>
        <div id="fixture-home-composer"><div><div><div data-testid="tweetTextarea_0" role="textbox" contenteditable="true"></div></div>
        <div data-testid="toolBar"><button data-testid="attachments">Fixture attachment</button><div><button data-testid="tweetButton">Post</button></div></div></div></div>
        <div aria-label="Home timeline"><section role="region"><div aria-label="Timeline: Your Home Timeline">
        <div data-testid="virtualizedList" style="height:3200px;position:relative">
        <div data-testid="cellInnerDiv" id="fixture-cell-one" style="position:absolute;width:100%;height:500px;transform:translateY(0px)"><article data-testid="tweet"><div data-testid="tweetText">Offline fixture one.</div></article></div>
        <div data-testid="cellInnerDiv" id="fixture-cell-two" style="position:absolute;width:100%;height:500px;transform:translateY(700px)"><article data-testid="tweet"><div data-testid="tweetText">Offline fixture two.</div></article></div>
        </div>
        </div></section></div></div><div data-testid="sidebarColumn" style="width:300px">Fixture recommendations</div></div></main>
        </div></div></div></body></html>
        """;

    public const string DiagnosticsScript = """
        (() => ({
            isAboutBlank: location.href === 'about:blank',
            hasContentApi: typeof window.__tinyX?.setContentMode === 'function',
            contentMode: document.documentElement.classList.contains('tinyx-content-mode') ||
                document.documentElement.getAttribute('data-tinyx-content') === 'true',
            tweetCount: document.querySelectorAll('[data-testid="tweet"]').length,
            hasAccountMenu: !!document.querySelector('[data-testid="SideNav_AccountSwitcher_Button"]'),
            hasLoginForm: !!document.querySelector('input[autocomplete="username"],input[type="password"],input[name="text"]'),
            hasLoginLink: !!document.querySelector('a[href="/login"],a[href="/i/flow/login"]'),
            navigationVisible: (() => { const el = document.querySelector('header[role="banner"]');
                return !!el && getComputedStyle(el).display !== 'none' && getComputedStyle(el).visibility !== 'hidden' && el.getBoundingClientRect().width > 0; })(),
            sidebarVisible: (() => { const el = document.querySelector('[data-testid="sidebarColumn"]');
                return !!el && getComputedStyle(el).display !== 'none'; })(),
            tweetVisible: (() => { const el = document.querySelector('[data-testid="tweet"]');
                return !!el && el.getBoundingClientRect().width > 0 && getComputedStyle(el).display !== 'none'; })(),
            primaryWidth: document.querySelector('[data-testid="primaryColumn"]')?.getBoundingClientRect().width || 0,
            viewportWidth: innerWidth,
            homeTabsVisible: (() => { const el = document.querySelector('[role="tablist"]');
                return !!el && el.getBoundingClientRect().height > 0 && getComputedStyle(el).display !== 'none'; })(),
            homeTabsInViewport: (() => { const el = document.querySelector('[role="tablist"]');
                const VIEWPORT_START = 0;
                if (!el) return false; const rect = el.getBoundingClientRect();
                return rect.height > VIEWPORT_START && rect.bottom > VIEWPORT_START && rect.top < innerHeight; })(),
            homeTabsTop: document.querySelector('[role="tablist"]')?.getBoundingClientRect().top || 0,
            homeTabsBottom: document.querySelector('[role="tablist"]')?.getBoundingClientRect().bottom || 0,
            homeTabsAtViewportTop: (() => { const el = document.querySelector('[role="tablist"]');
                const VIEWPORT_CENTER_DIVISOR = 2, VIEWPORT_PROBE_Y = 2;
                return !!el && el.contains(document.elementFromPoint(innerWidth / VIEWPORT_CENTER_DIVISOR, VIEWPORT_PROBE_Y)); })(),
            homeComposerHidden: (() => { const el = document.querySelector('#fixture-home-composer');
                return !!el && el.getBoundingClientRect().height === 0; })(),
            documentScrollMode: document.documentElement.classList.contains('tinyx-document-scroll'),
            bodyOverflowY: getComputedStyle(document.body).overflowY,
            nestedVerticalScrollers: Array.from(document.querySelectorAll('body,main,section,div')).filter(el => {
                const style = getComputedStyle(el);
                return /^(auto|scroll)$/.test(style.overflowY) && el.clientHeight > 0 && el.scrollHeight > el.clientHeight + 1;
            }).length,
            virtualCellHeight: (() => { const el = document.querySelector('#fixture-cell-two');
                return el ? getComputedStyle(el).height : null; })(),
            virtualCellTransform: (() => { const el = document.querySelector('#fixture-cell-two');
                return el ? getComputedStyle(el).transform : null; })(),
            visibleTweetCount: Array.from(document.querySelectorAll('[data-testid="tweet"]')).filter(el => {
                const rect = el.getBoundingClientRect(); return rect.width > 0 && rect.bottom > 0 && rect.top < innerHeight;
            }).length,
            scrollTop: document.scrollingElement?.scrollTop || 0,
            horizontalOverflow: document.documentElement.scrollWidth > innerWidth + 1
        }))()
        """;
}
