namespace TinyX;

internal static class SmokeFixture
{
    public const string Html = """
        <!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>TinyX local fixture</title><style>
        body { background: #000; color: #e7e9ea; font: 15px sans-serif; }
        main > div { display: flex; }
        article { padding: 24px 12px; border-bottom: 1px solid #333; }
        </style></head><body>
        <div id="react-root"><div><div>
        <header role="banner"><nav><button data-testid="SideNav_AccountSwitcher_Button">Fixture account</button></nav></header>
        <main role="main"><div><div data-testid="primaryColumn" style="width:600px;min-width:600px">
        <div aria-label="Home timeline"><section role="region"><div aria-label="Timeline: Your Home Timeline">
        <article data-testid="tweet"><div data-testid="tweetText">Offline fixture one.</div></article>
        <article data-testid="tweet"><div data-testid="tweetText">Offline fixture two.</div></article>
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
                return !!el && getComputedStyle(el).display !== 'none'; })(),
            sidebarVisible: (() => { const el = document.querySelector('[data-testid="sidebarColumn"]');
                return !!el && getComputedStyle(el).display !== 'none'; })(),
            tweetVisible: (() => { const el = document.querySelector('[data-testid="tweet"]');
                return !!el && el.getBoundingClientRect().width > 0 && getComputedStyle(el).display !== 'none'; })(),
            primaryWidth: document.querySelector('[data-testid="primaryColumn"]')?.getBoundingClientRect().width || 0,
            viewportWidth: innerWidth,
            horizontalOverflow: document.documentElement.scrollWidth > innerWidth + 1
        }))()
        """;
}
