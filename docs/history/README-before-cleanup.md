# MdictViewer v0.6.0

纯 C# / .NET 10 / WPF + WebView2，适配已经实测的 Oxford Advanced Learner's Dictionary 10 V14.3 MDX 及其五个 MDD 分卷。

本机最新构建使用 SDK 10.0.400 和桌面运行时 10.0.11，详情见 [NET 10 构建记录](MdictViewer_BUILD_NET10.md)。此前的 .NET 8 发布目录与历史调试记录保留。

设置页外观更新：按钮及路径输入框统一 6px 圆角，添加/删除/浏览使用绘制的加号、减号与文件夹图片，配色与主界面后退按钮一致。最新发布入口为 `dist/MdictViewer-v0.6.0-net10-settings/MdictViewer.exe`，详见 [设置页验证记录](MdictViewer_SETTINGS_STYLE.md)。

## v0.6.0：多词典与统一导航

- 顶部只保留输入框、后退、前进、设置。回车查词；查询期间输入框临时只读、不禁用，因此不会移走键盘焦点。
- 后退/前进使用圆角方形与 `#ba5b49` 箭头，设置使用圆形与 `#3271ae` 齿轮，背景均为 `#f5f2e9`。
- 设置支持浏览、编辑、删除最多 8 个 MDX 路径。最后一行有效时，左下方出现 `+`；保存后按顺序加载，取消不改变当前设置。
- 路径保存在 `%LocalAppData%/MdictViewer/dictionaries.json`。命令行传入一个 MDX 时仅覆盖本次启动，不自动改写设置。
- 每个词典有自己的标题、浏览器和读者，结果纵向排列，共用外层滚动条。CSS、图片和音频资源分别从对应 MDD 读取，不合并 HTML。
- 回车、候选和 `entry://` 链接查询所有词典，共用最多 50 条历史；后退后查新词会删除前进分支。修改词典配置重置历史，并重新查询当前已显示词。
- 候选按词典顺序合并，保留原拼写，不区分大小写去重，最多 12 个，150 毫秒防抖。只发布最新输入对应的结果。
- 部分未命中/失败只影响对应结果区；全部未命中保留页面和历史。手动查询保留编辑文字，失败的内部词条链接恢复当前已显示词。
- 自动调整结果高度，支持折叠、同页锚点、重复锚点和缩放；F9 自测不进入查词历史。

构建、测试与剩余人工验收项目见 [v0.6.0 调试记录](MdictViewer_DEBUG_v0.6.0.md)。这是功能迭代版，不宣称支持所有 MDX 格式。

## v0.5.2：候选事件路由与取消修复（历史）

- 修正候选点击被主窗口预览事件提前清空的问题。Popup 虽有独立 HWND，预览事件仍会经过主窗口；现在按事件实际来源区分搜索框、候选和其它区域。
- Esc 同时取消尚未显示的后台前缀搜索，避免关闭后旧结果延迟弹出。
- Tab 立即取消候选与待处理搜索，保留正常焦点移动。
- 新增 `dev-tests/Mdict.Wpf.Tests`：运行真实 WPF 窗口、WebView2 和 Oxford MDX，验证搜索事件、页面 DOM、异步取消、内部链接、历史及 F9。

Windows/.NET 8 构建及自动化回归结果见 [v0.5.2 调试记录](MdictViewer_DEBUG_v0.5.2.md)。原生键鼠、音频试听、折叠和图片放大仍需人工验收，本版暂不标记为稳定版。

## v0.5.1：候选键盘与鼠标交互调整（历史）

修正 v0.5.0 Windows 实机反馈：候选能出现，但方向键无响应、Enter 不执行查词，以及点击候选不跳转。

- TextBox 键盘事件改为 `PreviewKeyDown`，优先拦截 ↑/↓、Enter、Esc，再由 TextBox 自身处理其它按键。
- ListBox 候选确认改为 `PreviewMouseLeftButtonDown`，在弹窗焦点可能改变之前取得真实候选项并调用同一 `LookupAsync()`。
- 弹窗失焦关闭动作延迟到当前输入处理结束，并在鼠标停留于候选列表时不提前关闭。
- 更新结果时复用已经打开的 Popup，不强制反复关闭和重建；防抖期间旧候选只可显示，不允许键盘或鼠标误选，查询仍以实际输入为准。
- 本版只修改 `MainWindow.xaml`、`MainWindow.xaml.cs` 和本文档；词典 Core、Application、WebView2、音频以及原有历史逻辑保持不变。

实机测试请使用**明显不同的词条**观察内容是否更新：打开 `apple` 后输入 `banana` 并按 Enter；输入 `appl` 后使用 ↓、Enter 选 `applesauce` 等非当前词条；用鼠标单击另一候选；最后重测 Esc、内部链接与 F9。

## v0.5.0：输入触发的前缀候选（方案 A）

在用户已通过全部 8 项 v0.4.0 实机功能测试的基线上开发。本版仅调整 `Mdict.Wpf/MainWindow.xaml` 与 `MainWindow.xaml.cs`：

- **只有实际修改搜索框内容时**才开始候选搜索；仅点击 / 聚焦搜索框不弹出候选。粘贴及删除字符也视为用户输入。
- 150 毫秒输入防抖；后台调用已有的 `DictionaryService.SearchPrefix(prefix, limit)`。同一时间最多一个后台候选查询，后续请求合并为最新输入；旧结果不会覆盖新输入。
- 候选浮动显示在搜索框下方，最大 12 个不区分大小写的不同 Key，保留词典 Key 原拼写和原始顺序；最大列表高度 300 px。底层最多取 48 个结果，UI 去重并截取前 12 个。
- 初始**不选中**任何候选。↑/↓ 循环选择（不改动搜索框文字，也不移走键盘焦点）；Enter 打开明确选中的候选，否则按输入框文字直接精确查词。鼠标单击候选立即查词；Esc 关闭候选且保留文字，直到下次实际修改输入才可能再次显示。Tab 正常转移焦点并关闭候选。
- 查询按钮**始终查询输入框本身的内容**，不采用列表中键盘选中的候选。
- 未找到手动输入/候选目标：保留当前页面、历史及用户正在编辑的输入文字；内部 `entry://` 链接查词失败：恢复当前页面对应的搜索框内容。二者均只在状态栏提示。
- 打开/切换词典、执行查询、点击返回历史或内部词条、按 F9、主窗口失焦时清除候选及尚未提交的查询结果。程序内部修改搜索框文字不会再次弹出候选。
- **未修改** `Mdict.Core` / `Mdict.Application` / `MdictReaderTest`（保留 24 项 Core 回归测试），也未修改 WebView2 导航、原版 Oxford CSS/JS、音频桥接和资源处理器。

## 原有功能保持不变

- MDX/MDD 精确查词；WebView2 显示 Oxford 原始样式及图片；英美音、例句音频；图片放大、折叠区域。
- `entry://` 内部词条跳转；最多 50 条的统一内存查词历史与后退/前进按钮；`#anchor` 页面内跳转。
- F9 WebView2 自测；未找到词条时不覆盖现有页面。

## Windows 编译与测试

Windows 10/11 需安装 .NET 10 SDK、.NET 10 Desktop Runtime 和 WebView2 Runtime。`global.json` 固定 SDK 10.0.400（允许同功能带更新补丁）。从项目目录运行：

```powershell
dotnet restore .\MdictViewer.sln
dotnet build .\MdictViewer.sln
dotnet run --project .\MdictReaderTest -- --self-test
dotnet run --project .\Mdict.Wpf
```

本机 PATH 中的 `dotnet` 仍指向 SDK 8；执行以上命令时，请将 `dotnet` 替换为 `& "$env:USERPROFILE\.dotnet\dotnet.exe"`，使用已有的 SDK 10，不需要修改系统 PATH。

通过右上角齿轮设置 MDX 的完整文件路径。Oxford V14.3 的 MDX 同目录应有匹配的 `.mdd`、`.1.mdd` 至 `.4.mdd`，不要只选择文件夹。项目目标为 `net10.0-windows10.0.17763.0`，构建时由 .NET SDK 自动还原所需 Windows SDK 引用。运行时自动选择已安装的 .NET 10 最新补丁；这不是旧式 .NET Framework 项目。

建议验证：

1. 仅点击搜索框不出现列表；输入 `appl` 等待片刻，出现候选词；输入 `apple` 后直接 Enter，应查 `apple`，不强制第一项。
2. 继续输入、删除、粘贴单词；确认列表按新前缀更新且不出现过时结果，最多显示 12 项、不重复。
3. 按 ↓ / ↑ 选词，Enter 查询；手动输入时键盘焦点仍在搜索框；鼠标点击候选项可以直接查询。
4. Esc 关闭后不立即重新弹出，单纯点击或聚焦搜索框不触发，只有修改文字后才能重新出现。点击别处或 Tab 可关闭列表。
5. 手动查询不存在的词，状态栏提示但原页面和输入文字保留；点击不存在的 `entry://` 链接则保持原页面并恢复搜索框。
6. 手动查词、候选词选词、Oxford 内部词条点击、返回历史、F9 测试混用，查词历史正确；返回/跳转/F9 不触发新的候选。
7. 英美音、折叠、图片放大与 F9 正常；Core 自测原有 24 项全部通过。

可选（安装了 Node.js 的开发机器）：

```powershell
node .\dev-tests\entry_bridge_test.cjs
node .\dev-tests\layout_bridge_test.cjs
```

真实词典 WPF 集成回归（传入 MDX 文件路径，测试会短暂打开窗口）：

```powershell
dotnet run --project .\dev-tests\Mdict.Wpf.Tests -- "<Oxford.mdx 的完整路径>"
```

该测试通过 WPF 路由事件与 WebView2 DOM 验证行为，不代替真实鼠标、键盘、焦点切换及音频试听。测试使用自己的构建输出目录；重新构建主程序前请关闭正在运行的 MdictViewer，以避免可执行文件占用错误。

## 模块边界与暂不支持功能

- `MainWindow.xaml/.cs`：候选显示、输入防抖、键盘鼠标交互、多词典 `LookupAsync()`、统一历史和滚动。
- `DictionarySettings` / `SettingsWindow`：路径校验、设置对话框与本地持久化。
- `DictionaryResultView`：独立读者、浏览器、标题与生命周期。
- `DictionaryService`：现有线程安全的 `SearchPrefix`/`Lookup` 接口，未改。
- `Mdict.Core`：现有 Key 索引及前缀线性扫描，未改；仅当真实运行出现性能瓶颈时再研究优化。
- `WebViewController` / `LayoutBridge.js`：独立浏览器导航、高度、滚动、锚点与缩放。采用组合式浏览器解决 WPF 滚动裁剪问题，参见 [Microsoft WPF WebView2 文档](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf)。
- `WebResourceHandler`：原有资源路由，增加关闭期间回调保护；`AudioBridge.js` / `EntryBridge.js`：未改。

不实现拼写纠错、模糊查询、全文搜索、新的索引系统或查词历史持久化。仅继续使用现有 MDX/MDD 读者；不保证所有第三方词典格式和脚本兼容。资源统计在状态栏的悬停提示内。

**历史验证状态（v0.5.1 交接时）**：原开发环境仅完成静态检查与桥接脚本测试，尚未验证 Windows WPF 交互。v0.5.2 在本机使用真实 Oxford MDX/MDD 进行构建和集成回归，详情及未验收项目见调试记录。
