using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TinyX;

internal sealed class MainForm : Form
{
    private const string HomeUrl = "https://x.com/home";
    private const int LogicalDpi = 96;
    private const int BorderSize = 4;
    private const int WindowMargin = 12;
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan SuspendTimeout = TimeSpan.FromSeconds(2);
    private readonly LaunchOptions _options;
    private readonly AppSettings _settings;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        Text = "TinyX 正在启动…\nCtrl + Alt + X 隐藏 / 显示\n右键托盘图标可打开菜单",
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.FromArgb(15, 20, 25),
        ForeColor = Color.Gainsboro
    };
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _topMenu;
    private readonly ToolStripMenuItem _contentMenu;
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);
    private CoreWebView2Environment? _environment;
    private string? _initializationScriptId;
    private bool _wantVisible = true;
    private bool _exiting;
    private bool _hotkeyRegistered;
    private bool _smokeStarted;
    private bool _fixtureNavigationPending;
    private Rectangle? _compactBounds;
    private bool? _lastSuspendResult;

    public MainForm(LaunchOptions options)
    {
        _options = options;
        _settings = options.IsSmokeTest ? new AppSettings() : AppSettings.Load();
        Text = "TinyX";
        Name = "TinyX";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(LogicalDpi, LogicalDpi);
        BackColor = Color.FromArgb(25, 32, 38);
        Padding = new Padding(BorderSize);
        Rectangle initialWorkArea = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        ClientSize = new Size(Math.Min(_settings.Width, initialWorkArea.Width),
            Math.Min(_settings.Height, initialWorkArea.Height));
        MinimumSize = new Size(AppSettings.MinimumWidth, AppSettings.MinimumHeight);
        ShowInTaskbar = false;
        TopMost = _settings.AlwaysOnTop;
        Controls.Add(_web);
        Controls.Add(_status);
        _status.BringToFront();

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏  Ctrl+Alt+X", null, (_, _) => ToggleVisibility());
        _topMenu = new ToolStripMenuItem("始终置顶") { Checked = TopMost, CheckOnClick = true };
        _topMenu.CheckedChanged += (_, _) =>
        {
            TopMost = _topMenu.Checked;
            _settings.AlwaysOnTop = TopMost;
            PersistSettings();
            AppLog.Info("topmost-changed", "enabled=" + TopMost);
        };
        menu.Items.Add(_topMenu);
        _contentMenu = new ToolStripMenuItem("仅显示推文（取消勾选可完整登录）")
        {
            Checked = _settings.ContentMode,
            CheckOnClick = true
        };
        _contentMenu.CheckedChanged += async (_, _) => await ChangeContentModeAsync();
        menu.Items.Add(_contentMenu);
        menu.Items.Add("移动窗口（随后移动鼠标 / 方向键）", null, (_, _) =>
        {
            ShowWindow();
            NativeMethods.SendMessage(Handle, NativeMethods.SystemCommandMessage,
                (IntPtr)NativeMethods.MoveCommand, IntPtr.Zero);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("刷新", null, (_, _) => _web.CoreWebView2?.Reload());
        menu.Items.Add("回到首页", null, (_, _) => NavigateHome());
        menu.Items.Add("在浏览器中打开 X", null, (_, _) => OpenExternal(HomeUrl));
        menu.Items.Add("操作说明", null, (_, _) => ShowHelp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 TinyX", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "TinyX · Ctrl+Alt+X 显示 / 隐藏",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowWindow();
        Shown += async (_, _) =>
        {
            RestoreWindowBounds();
            await InitializeWebViewAsync();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style |= NativeMethods.ThickFrameStyle;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, NativeMethods.HotkeyId,
            NativeMethods.HotkeyModifiers, NativeMethods.HideKey);
        AppLog.Info("hotkey-registration", "success=" + _hotkeyRegistered);
        if (!_hotkeyRegistered && !_options.IsSmokeTest)
            BeginInvoke((Action)(() => _tray.ShowBalloonTip(7000, "TinyX 快捷键不可用",
                "Ctrl+Alt+X 已被其他程序占用。仍可双击或右键 TinyX 托盘图标显示窗口。",
                ToolTipIcon.Warning)));
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered)
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.NonClientCalculateSizeMessage)
        {
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == NativeMethods.HotkeyMessage &&
            message.WParam.ToInt32() == NativeMethods.HotkeyId)
        {
            ToggleVisibility();
            return;
        }
        if (message.Msg == NativeMethods.NonClientHitTestMessage)
        {
            Point point = PointToClient(new Point(unchecked((short)(long)message.LParam),
                unchecked((short)((long)message.LParam >> 16))));
            int border = ScaleLogical(BorderSize);
            bool left = point.X < border;
            bool right = point.X >= ClientSize.Width - border;
            bool top = point.Y < border;
            bool bottom = point.Y >= ClientSize.Height - border;
            int hit = NativeMethods.ClientHit;
            if (left && top) hit = NativeMethods.TopLeftHit;
            else if (right && top) hit = NativeMethods.TopRightHit;
            else if (left && bottom) hit = NativeMethods.BottomLeftHit;
            else if (right && bottom) hit = NativeMethods.BottomRightHit;
            else if (left) hit = NativeMethods.LeftHit;
            else if (right) hit = NativeMethods.RightHit;
            else if (bottom) hit = NativeMethods.BottomHit;
            else if (top) hit = (ModifierKeys & Keys.Alt) == Keys.Alt
                ? NativeMethods.CaptionHit : NativeMethods.TopHit;
            if (hit != NativeMethods.ClientHit)
            {
                message.Result = (IntPtr)hit;
                return;
            }
        }
        base.WndProc(ref message);
    }

    public void ShowWindow() => _ = SetWindowVisibleAsync(true);

    private void ToggleVisibility() => _ = SetWindowVisibleAsync(!_wantVisible);

    private async Task SetWindowVisibleAsync(bool visible)
    {
        _wantVisible = visible;
        if (!visible && !_exiting && !IsDisposed)
        {
            RememberWindowBounds();
            _web.Visible = false;
            Hide();
        }
        await _visibilityGate.WaitAsync();
        try
        {
            if (_exiting || IsDisposed || _wantVisible != visible) return;
            CoreWebView2? core = _web.CoreWebView2;
            if (visible)
            {
                if (core is not null)
                {
                    core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                _web.Visible = true;
                Show();
                WindowState = FormWindowState.Normal;
                ClampWindowToScreen();
                Activate();
                if (core is not null)
                    await ExecuteContentCommandAsync("setPageVisible", true);
                AppLog.Info("window-shown");
            }
            else
            {
                if (core is not null)
                    await ExecuteContentCommandAsync("setPageVisible", false);
                if (_wantVisible != visible) return;
                if (core is not null)
                {
                    try
                    {
                        Task<bool> suspension = core.TrySuspendAsync();
                        _ = RestoreAfterLateSuspensionAsync(core, suspension);
                        _lastSuspendResult = await suspension.WaitAsync(SuspendTimeout);
                        if (_lastSuspendResult != true)
                            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                        AppLog.Info("webview-suspend", "success=" + _lastSuspendResult);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException or TimeoutException)
                    {
                        _lastSuspendResult = false;
                        AppLog.Error("webview-suspend-failed", ex);
                        try { core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; }
                        catch (Exception fallback) when (fallback is InvalidOperationException or COMException)
                        { AppLog.Error("webview-low-memory-failed", fallback); }
                    }
                }
                AppLog.Info("window-hidden");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            AppLog.Error("window-visibility-failed", ex);
            if (visible && !_exiting && !IsDisposed)
            {
                Show();
                _web.Visible = true;
            }
        }
        finally { _visibilityGate.Release(); }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            string profile = _options.IsSmokeTest
                ? Path.Combine(AppSettings.DataDirectory, "SmokeProfiles", Environment.ProcessId.ToString())
                : Path.Combine(AppSettings.DataDirectory, "WebView2");
            Directory.CreateDirectory(profile);
            _environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (_exiting) return;
            await _web.EnsureCoreWebView2Async(_environment);
            CoreWebView2 core = _web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += (_, e) =>
            {
                AppLog.Info("webview-process-failed", "kind=" + e.ProcessFailedKind);
                _tray.ShowBalloonTip(5000, "TinyX 页面进程异常",
                    "可右键托盘图标选择刷新；若仍无法恢复，请退出后重开。", ToolTipIcon.Warning);
            };
            await InstallInitializationScriptAsync();
            AppLog.Info("webview-ready", "runtime=" + _environment.BrowserVersionString);
            if (_options.LocalFixture)
            {
                _fixtureNavigationPending = true;
                core.NavigateToString(SmokeFixture.Html);
            }
            else
                core.Navigate(HomeUrl);
            if (!_wantVisible)
                await SetWindowVisibleAsync(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("webview-initialization-failed", ex);
            _status.Text = "TinyX 无法初始化网页组件。\n请确认已安装 Microsoft Edge WebView2 Runtime。\n右键托盘图标可退出；详细信息见本地日志。";
            _status.BringToFront();
            if (_options.IsSmokeTest)
                await WriteFailureAndExitAsync("initialization-failed", ex);
        }
    }

    private async Task InstallInitializationScriptAsync()
    {
        CoreWebView2? core = _web.CoreWebView2;
        if (core is null) return;
        if (_initializationScriptId is not null)
            core.RemoveScriptToExecuteOnDocumentCreated(_initializationScriptId);
        string css = ReadResource("content.css");
        string javascript = ReadResource("content.js");
        string enabled = _settings.ContentMode ? "true" : "false";
        string fixture = _options.LocalFixture ? "true" : "false";
        string script = $$"""
            (() => {
                if (window.top !== window) return;
                const host = location.hostname.toLowerCase();
                const trusted = host === 'x.com' || host.endsWith('.x.com') ||
                    host === 'twitter.com' || host.endsWith('.twitter.com');
                if (!trusted && !({{fixture}} && location.href === 'about:blank')) return;
                window.__tinyXEnabled = {{enabled}};
                window.__tinyXAllowLocalFixture = {{fixture}};
                const installStyle = () => {
                    if (!document.documentElement || document.getElementById('tinyx-content-style')) return;
                    const style = document.createElement('style');
                    style.id = 'tinyx-content-style';
                    style.textContent = {{JsonSerializer.Serialize(css)}};
                    document.documentElement.appendChild(style);
                };
                installStyle();
                document.addEventListener('DOMContentLoaded', installStyle, {once: true});
                {{javascript}}
                window.__tinyX?.setContentMode({{enabled}});
            })();
            """;
        _initializationScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    private static string ReadResource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TinyX.Assets." + name)
            ?? throw new InvalidOperationException("Missing embedded content resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task ExecuteContentCommandAsync(string command, bool value)
    {
        if (_web.CoreWebView2 is null) return;
        try
        {
            await _web.ExecuteScriptAsync("window.__tinyX?." + command + "(" +
                (value ? "true" : "false") + ")").WaitAsync(ScriptTimeout);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or COMException)
        {
            AppLog.Error("content-command-failed", ex);
        }
    }

    private async Task RestoreAfterLateSuspensionAsync(CoreWebView2 core, Task<bool> suspension)
    {
        try
        {
            bool suspended = await suspension;
            if (suspended && _wantVisible && !_exiting && !IsDisposed)
            {
                core.Resume();
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                AppLog.Info("late-suspension-restored");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            AppLog.Error("late-suspension-failed", ex);
        }
    }

    private async Task ChangeContentModeAsync()
    {
        _settings.ContentMode = _contentMenu.Checked;
        if (!_settings.ContentMode)
        {
            _compactBounds = Bounds;
            Size = new Size(ScaleLogical(900), ScaleLogical(760));
            ClampWindowToScreen();
        }
        else if (_compactBounds.HasValue)
        {
            Bounds = _compactBounds.Value;
            _compactBounds = null;
            ClampWindowToScreen();
        }
        try
        {
            await InstallInitializationScriptAsync();
            await ExecuteContentCommandAsync("setContentMode", _settings.ContentMode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            AppLog.Error("content-mode-update-failed", ex);
        }
        PersistSettings();
        AppLog.Info("content-mode-changed", "enabled=" + _settings.ContentMode);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        bool validAddress = Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? address);
        string scheme = validAddress ? address!.Scheme : "invalid";
        AppLog.Info("navigation-scheme", "scheme=" + scheme + ";local-fixture=" + _options.LocalFixture);
        if (_options.LocalFixture && _fixtureNavigationPending &&
            (e.Uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:text/html,", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:text/html;", StringComparison.OrdinalIgnoreCase)))
        {
            _fixtureNavigationPending = false;
            AppLog.Info("fixture-navigation-allowed", "scheme=" + scheme);
            return;
        }
        if (IsTrustedNavigation(e.Uri))
        {
            AppLog.Info("navigation-start", "host=" + new Uri(e.Uri).DnsSafeHost);
            return;
        }
        e.Cancel = true;
        AppLog.Info("navigation-external-blocked");
        if (e.IsUserInitiated) OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsTrustedNavigation(e.Uri))
            _web.CoreWebView2.Navigate(e.Uri);
        else if (e.IsUserInitiated)
            OpenExternal(e.Uri);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        AppLog.Info("navigation-completed", "success=" + e.IsSuccess + ";status=" + e.WebErrorStatus);
        _status.Visible = false;
        if (_options.IsSmokeTest && !_smokeStarted)
        {
            _smokeStarted = true;
            await RunSmokeTestAsync(e.IsSuccess);
        }
    }

    internal static bool IsTrustedNavigation(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        string host = uri.IdnHost;
        return host.Equals("x.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".x.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("twitter.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".twitter.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("appleid.apple.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenExternal(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            AppLog.Info("external-browser-opened", "host=" + uri.DnsSafeHost);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Error("external-browser-open-failed", ex);
        }
    }

    private void NavigateHome()
    {
        ShowWindow();
        _web.CoreWebView2?.Navigate(HomeUrl);
    }

    private void ShowHelp() => MessageBox.Show(this,
        "Ctrl + Alt + X：全局显示 / 隐藏\n双击托盘图标：显示窗口\n右键托盘图标：置顶、内容模式、刷新、退出\n拖动四周细边框：调整大小\nAlt + 拖动顶部细边框：移动窗口\n也可在托盘菜单选择‘移动窗口’\n\n登录受限时，取消勾选‘仅显示推文’后再登录。\n登录态独立保存在本机，不会读取 Chrome 密码或 Cookie。",
        "TinyX 操作说明", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private int ScaleLogical(int value) => (int)Math.Clamp(
        Math.Round((double)value * DeviceDpi / LogicalDpi), int.MinValue, int.MaxValue);

    private void RestoreWindowBounds()
    {
        Screen screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == _settings.Monitor)
            ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        Rectangle work = screen.WorkingArea;
        Location = new Point(work.Left + WindowMargin, work.Top + WindowMargin);
        Size = new Size(ScaleLogical(_settings.Width), ScaleLogical(_settings.Height));
        Location = _settings.Left.HasValue && _settings.Top.HasValue
            ? new Point(work.Left + ScaleLogical(_settings.Left.Value), work.Top + ScaleLogical(_settings.Top.Value))
            : new Point(work.Left + ScaleLogical(WindowMargin), work.Bottom - Height - ScaleLogical(WindowMargin));
        ClampWindowToScreen();
    }

    private void ClampWindowToScreen()
    {
        Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
        Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        Location = new Point(Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width)),
            Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
    }

    private void RememberWindowBounds()
    {
        Rectangle bounds = !_settings.ContentMode && _compactBounds.HasValue ? _compactBounds.Value : Bounds;
        Screen screen = Screen.FromRectangle(bounds);
        double scale = LogicalDpi / (double)DeviceDpi;
        _settings.Monitor = screen.DeviceName;
        _settings.Left = (int)Math.Round((bounds.Left - screen.WorkingArea.Left) * scale);
        _settings.Top = (int)Math.Round((bounds.Top - screen.WorkingArea.Top) * scale);
        _settings.Width = (int)Math.Round(bounds.Width * scale);
        _settings.Height = (int)Math.Round(bounds.Height * scale);
    }

    private void PersistSettings()
    {
        if (_options.IsSmokeTest) return;
        RememberWindowBounds();
        _settings.Save();
    }

    private async Task RunSmokeTestAsync(bool navigationSucceeded)
    {
        try
        {
            await Task.Delay(1800);
            bool borderless = FormBorderStyle == FormBorderStyle.None;
            bool initiallyVisible = Visible;
            bool inWorkArea = Screen.FromRectangle(Bounds).WorkingArea.Contains(Bounds);
            bool frameStyle = (NativeMethods.GetWindowStyle(Handle) & NativeMethods.ThickFrameStyle) != 0;
            var hitTests = new Dictionary<string, int>
            {
                ["Left"] = ReadNativeHitTest(new Point(1, ClientSize.Height / 2)),
                ["Right"] = ReadNativeHitTest(new Point(ClientSize.Width - 1, ClientSize.Height / 2)),
                ["Top"] = ReadNativeHitTest(new Point(ClientSize.Width / 2, 1)),
                ["Bottom"] = ReadNativeHitTest(new Point(ClientSize.Width / 2, ClientSize.Height - 1))
            };
            bool hitTestsPassed = hitTests["Left"] == NativeMethods.LeftHit &&
                hitTests["Right"] == NativeMethods.RightHit && hitTests["Top"] == NativeMethods.TopHit &&
                hitTests["Bottom"] == NativeMethods.BottomHit;
            bool originalTopmost = _topMenu.Checked;
            _topMenu.Checked = false;
            bool topmostDisabled = !TopMost &&
                (NativeMethods.GetWindowStyle(Handle, true) & NativeMethods.TopMostExtendedStyle) == 0;
            _topMenu.Checked = true;
            bool topmostEnabled = TopMost &&
                (NativeMethods.GetWindowStyle(Handle, true) & NativeMethods.TopMostExtendedStyle) != 0;
            _topMenu.Checked = originalTopmost;
            bool topmostTogglePassed = topmostDisabled && topmostEnabled;
            var before = CaptureMemorySnapshot();
            string pageState = await _web.ExecuteScriptAsync(SmokeFixture.DiagnosticsScript);
            bool? contentChecks = null;
            JsonElement? fullPageState = null;
            JsonElement? authenticationPageState = null;
            if (_options.LocalFixture)
            {
                using JsonDocument compact = JsonDocument.Parse(pageState);
                JsonElement state = compact.RootElement;
                bool compactPassed = state.GetProperty("hasContentApi").GetBoolean() &&
                    state.GetProperty("contentMode").GetBoolean() &&
                    !state.GetProperty("navigationVisible").GetBoolean() &&
                    !state.GetProperty("sidebarVisible").GetBoolean() &&
                    state.GetProperty("tweetVisible").GetBoolean() &&
                    !state.GetProperty("horizontalOverflow").GetBoolean();
                await ExecuteContentCommandAsync("setContentMode", false);
                await Task.Delay(200);
                using JsonDocument full = JsonDocument.Parse(await _web.ExecuteScriptAsync(SmokeFixture.DiagnosticsScript));
                fullPageState = full.RootElement.Clone();
                bool fullPassed = !full.RootElement.GetProperty("contentMode").GetBoolean() &&
                    full.RootElement.GetProperty("sidebarVisible").GetBoolean() &&
                    full.RootElement.GetProperty("navigationVisible").GetBoolean();
                await ExecuteContentCommandAsync("setContentMode", true);
                await _web.ExecuteScriptAsync("(() => { const input=document.createElement('input'); input.type='password'; input.autocomplete='current-password'; input.id='tinyx-smoke-login'; document.body.prepend(input); })()");
                await Task.Delay(200);
                using JsonDocument auth = JsonDocument.Parse(await _web.ExecuteScriptAsync(SmokeFixture.DiagnosticsScript));
                authenticationPageState = auth.RootElement.Clone();
                bool authPassed = !auth.RootElement.GetProperty("contentMode").GetBoolean() &&
                    auth.RootElement.GetProperty("sidebarVisible").GetBoolean() &&
                    auth.RootElement.GetProperty("hasLoginForm").GetBoolean();
                await _web.ExecuteScriptAsync("document.getElementById('tinyx-smoke-login')?.remove()");
                await Task.Delay(200);
                contentChecks = compactPassed && fullPassed && authPassed;
            }
            NativeMethods.SendMessage(Handle, NativeMethods.HotkeyMessage,
                (IntPtr)NativeMethods.HotkeyId, IntPtr.Zero);
            await WaitForSmokeVisibilityTransitionAsync();
            await Task.Delay(400);
            bool hidden = !Visible && !_web.Visible;
            bool suspended = _web.CoreWebView2.IsSuspended;
            var hiddenMemory = CaptureMemorySnapshot();
            NativeMethods.SendMessage(Handle, NativeMethods.HotkeyMessage,
                (IntPtr)NativeMethods.HotkeyId, IntPtr.Zero);
            await WaitForSmokeVisibilityTransitionAsync();
            await Task.Delay(400);
            bool restored = Visible && _web.Visible && !_web.CoreWebView2.IsSuspended;
            bool hotkeyDispatchPassed = hidden && restored;
            bool trustedNavigationChecks = IsTrustedNavigation(HomeUrl) &&
                !IsTrustedNavigation("https://x.com.attacker.invalid/") &&
                !IsTrustedNavigation("file:///C:/Windows/") &&
                !IsTrustedNavigation("javascript:alert(1)");
            using JsonDocument pageDocument = JsonDocument.Parse(pageState);
            bool passed = navigationSucceeded && borderless && initiallyVisible && inWorkArea &&
                hotkeyDispatchPassed && frameStyle && hitTestsPassed && topmostTogglePassed &&
                trustedNavigationChecks && contentChecks != false;
            object report = new
            {
                Passed = passed,
                LocalFixture = _options.LocalFixture,
                NavigationSucceeded = navigationSucceeded,
                Borderless = borderless,
                AlwaysOnTop = TopMost,
                HotkeyRegistered = _hotkeyRegistered,
                HotkeyDispatchPassed = hotkeyDispatchPassed,
                ResizableFrameStyle = frameStyle,
                HitTestPassed = hitTestsPassed,
                HitTests = hitTests,
                TopmostTogglePassed = topmostTogglePassed,
                InitiallyVisible = initiallyVisible,
                BoundsInWorkArea = inWorkArea,
                Hidden = hidden,
                SuspendAccepted = _lastSuspendResult,
                Suspended = suspended,
                Restored = restored,
                TrustedNavigationChecks = trustedNavigationChecks,
                ContentChecks = contentChecks,
                BrowserVersion = _environment?.BrowserVersionString,
                Page = pageDocument.RootElement.Clone(),
                FullPageMode = fullPageState,
                AuthenticationPage = authenticationPageState,
                MemoryBefore = before,
                MemoryHidden = hiddenMemory,
                MemoryRestored = CaptureMemorySnapshot(),
                MemoryNote = "Whole process group; working sets can share pages and are not private resident RAM."
            };
            await WriteDiagnosticsAsync(report);
            Environment.ExitCode = passed ? 0 : 1;
            ExitApplication();
        }
        catch (Exception ex)
        {
            await WriteFailureAndExitAsync("smoke-test-failed", ex);
        }
    }

    private int ReadNativeHitTest(Point clientPoint)
    {
        Point point = PointToScreen(clientPoint);
        int packedPoint = (point.Y << 16) | (point.X & ushort.MaxValue);
        return NativeMethods.SendMessage(Handle, NativeMethods.NonClientHitTestMessage,
            IntPtr.Zero, (IntPtr)packedPoint).ToInt32();
    }

    private async Task WaitForSmokeVisibilityTransitionAsync()
    {
        if (!await _visibilityGate.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Smoke visibility transition timed out.");
        _visibilityGate.Release();
    }

    private object CaptureMemorySnapshot()
    {
        var entries = new List<object>();
        long workingSet = 0;
        long privateBytes = 0;
        var processes = new List<(int Id, string Kind)> { (Environment.ProcessId, "Host") };
        if (_environment is not null)
            processes.AddRange(_environment.GetProcessInfos().Select(item => (item.ProcessId, item.Kind.ToString())));
        foreach (var item in processes.DistinctBy(item => item.Id))
        {
            try
            {
                using Process process = Process.GetProcessById(item.Id);
                process.Refresh();
                workingSet += process.WorkingSet64;
                privateBytes += process.PrivateMemorySize64;
                entries.Add(new { item.Id, item.Kind, WorkingSetBytes = process.WorkingSet64,
                    PrivateBytes = process.PrivateMemorySize64 });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { AppLog.Error("diagnostics-process-unavailable", ex); }
        }
        return new { TotalWorkingSetBytes = workingSet, TotalPrivateBytes = privateBytes, Processes = entries };
    }

    private Task WriteDiagnosticsAsync(object report)
    {
        string path = _options.DiagnosticsPath ?? Path.Combine(AppSettings.DataDirectory, "diagnostics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task WriteFailureAndExitAsync(string stage, Exception ex)
    {
        AppLog.Error(stage, ex);
        try { await WriteDiagnosticsAsync(new { Passed = false, Stage = stage, Error = ex.GetType().Name }); }
        catch (Exception writeException) { AppLog.Error("diagnostics-write-failed", writeException); }
        Environment.ExitCode = 1;
        ExitApplication();
    }

    private void ExitApplication()
    {
        PersistSettings();
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _ = SetWindowVisibleAsync(false);
            return;
        }
        PersistSettings();
        _exiting = true;
        _tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
