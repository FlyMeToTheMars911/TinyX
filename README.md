# TinyX

Windows 左下角的 X（Twitter）小窗。C# / WinForms / Microsoft Edge WebView2，直接加载 X 官方网页，不使用第三方推文接口。

## 使用

解压 Windows 发布包，运行 `TinyX.exe`。默认显示在主屏左下角、任务栏上方，初始大小约 400 × 600，默认置顶。运行第二个 TinyX 会唤回已有窗口。

| 操作 | 方法 |
| --- | --- |
| 显示 / 隐藏 | 全局 `Ctrl + Alt + X`；双击系统托盘图标可恢复窗口 |
| 置顶、纯内容模式、刷新等 | 右键系统托盘图标 |
| 移动 | `Alt` + 鼠标拖动顶部细边缘，或在托盘菜单选择“移动窗口” |
| 调整大小 | 拖动窗口边缘 |
| 登录 / 恢复完整网页导航 | 在托盘菜单暂时关闭纯内容模式，按需使用登录窗口大小 |
| 退出 | 托盘菜单“退出” |

首次启动需要在 **TinyX 自己的窗口中登录 X**。登录状态保存在当前 Windows 用户的本地目录中，以后启动继续使用。Chrome 与 WebView2 的会话彼此独立，TinyX 不导入 Chrome 的 Cookie 或密码。Google 等外部身份提供商如要求完整浏览器，优先使用 X 自身的用户名 / 邮箱登录。

支持 Windows 10 / 11，发布包区分 x64 / ARM64。精简发布包需要 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 和 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。程序不包含整套浏览器；这些运行时由系统统一维护。

## 纯内容模式

- 时间线铺满小窗；精简模式隐藏左右侧栏，不显示悬停触发条或导航浮层。
- 保留顶部“为你推荐 / 正在关注 / 列表”等原生标签，随页面滚动移出视野，回到页面顶部时显示；隐藏首页发帖区域、右侧推荐趋势、底部导航和消息 / Grok 浮层。
- 保留推文作者、正文、图片、手动视频播放、互动按钮、详情与弹窗。
- 仅在识别到已登录的 X 主页面时启用，登录、验证码等认证页面保留原布局。
- 关闭纯内容模式可恢复网页导航。X 网页结构变化时，可能需要更新 `src/TinyX/Assets/content.css` 和 `content.js`。

应用和系统托盘使用同一套 TinyX 图标，包含 16–256 像素的八种尺寸。矢量源文件为 `src/TinyX/Assets/TinyX.svg`，可用 `scripts/generate-icon.ps1` 重生成 ICO。

## 资源占用

只有一个 WebView，不轮询推文，不定时刷新，不内置 Node.js，也不携带 Chromium。视频默认不自动播放，手动点击后可播放；隐藏时暂停视频并尝试挂起 WebView，挂起失败则使用 WebView2 低内存目标。重新显示时恢复页面。

内存应统计 **TinyX 和它所属的全部 WebView2 子进程**。X 网页、图片、视频和运行时本身会占用内存，因此小安装包不代表只有几十 MB 的常驻内存。程序不清理登录数据、不禁用 GPU、不定时强制 GC，也不通过裁剪历史推文破坏滚动位置。

本地设置、网页数据和有大小上限的诊断日志存于 `%LOCALAPPDATA%\TinyX`。不会把 Cookie、令牌、推文正文或完整导航 URL 写入日志。仓库和发布包不包含用户浏览数据。

## 开发与打包

需要 .NET 10 SDK；修改网页脚本的检查需要 Node.js 22 或更新版本。

```powershell
dotnet restore src/TinyX/TinyX.csproj --locked-mode
dotnet build src/TinyX/TinyX.csproj -c Release --no-restore
node --check src/TinyX/Assets/content.js
node --test tests/content-mode.test.cjs
./scripts/publish.ps1
```

输出：`dist/TinyX-win-x64-framework-dependent/TinyX.exe` 和同名 ZIP。若该目录的程序正在运行，新程序写入带 `-update-` 后缀的目录，避免覆盖正在使用的 DLL。如需包含 .NET 桌面运行时的较大独立包：

```powershell
./scripts/publish.ps1 -SelfContained
./scripts/publish.ps1 -Runtime win-arm64
```

WebView2 使用固定版本 NuGet 引用和 `packages.lock.json`；Evergreen 浏览器运行时随系统更新。WinForms 不启用裁剪。

GitHub Actions 在推送和 PR 时运行脚本回归检查并打包；推送 `v*` 标签时将 ZIP 与 SHA256 校验文件附加到 GitHub Release，也支持更新已创建的同名发布。

## 验证与边界

离线 smoke 模式通过本地模拟时间线测试窗口、内容样式和隐藏 / 恢复，完全不访问账号：

```powershell
./dist/TinyX-win-x64-framework-dependent/TinyX.exe --smoke-test-local --diagnostics ./work/smoke.json
```

实际 X 页面测试需要可用网络与 TinyX 中的登录态。仅有 Chrome 已登录不足以证明 WebView2 内的登录已通过。测试过程不发推、不点赞、不关注，不修改账号设置。

托盘菜单可导出布局诊断。诊断只收集滚动位置、尺寸、固定 DOM 标记和进程内存，不收集正文、Cookie 或输入内容，文件存于 `%LOCALAPPDATA%\TinyX\diagnostics-current.json`。运行中的新版程序也支持：

```powershell
.\TinyX.exe --diagnostics-existing
.\TinyX.exe --exit-existing
```

开发者可用 `--inspect-profile --diagnostics ./work/layout.json` 检查已登录会话：它创建独立、透明且不抢焦点的诊断窗口，共用 TinyX 登录目录，仅在该诊断窗口内读取布局和测试滚动，结束后自动退出。诊断文件留在本地。逐项验证见 [0.2.0 页面优化](docs/0.2.0-页面优化与验证.md) 和 [0.2.1 顶部标签栏滚动](docs/0.2.1-顶部标签栏滚动.md)。

TinyX 是独立开源工具，与 X Corp 无关联。X 的网站与第三方组件由其各自条款约束，项目源码使用 MIT 许可。
