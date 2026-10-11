# MdictViewer 开发交接（HANDOFF）

**交接日期：** 2026-10-10  
**交接目标：** 由 Codex 接手调试 MdictViewer v0.5.1 的搜索候选交互，完成 v0.5.x 未通过实机验收的部分；必要时发布 **v0.5.2** 修复版。  
**技术栈：** C# / .NET 8 / WPF / WebView2，Oxford Advanced Learner's Dictionary 10 V14.3（MDX + 五个 MDD 分卷）。

## 1. 先确认真实基线与状态

- **v0.4.0 是已验证的稳定基线**：用户在 Windows 上确认以下 8 项均通过：`entry://` 链接（`cooking apple`、`Big Apple` 等）、返回上一词条、多级/手动查词历史、`Idioms` 页面内锚点、英美发音、折叠/图片放大、F9 WebView2 自测，以及不存在的内部词条不覆盖原页面。v0.4.0 代码已由用户提交到 GitHub。
- **v0.5.0 是第一个候选搜索版本，已被用户实际发现问题**：①逐字符输入时出现/更新候选列表；②按 ↓ 没有反应；③直接按 Enter 没有打开词条，原页面不变；④ Esc 能关闭候选，重新点击输入框不会自动弹出；⑤鼠标点击候选词后原页面也不变。注意：①“输入改变则更新候选”本身符合要求，真正应诊断的是是否反复关闭/重开、闪烁或焦点丢失。
- **v0.5.1 已针对上述问题做过源码修改，但尚未获得用户 Windows 实机结果**：将输入框按键处理从 `KeyDown` 改为 `PreviewKeyDown`；候选鼠标确认改为 `PreviewMouseLeftButtonDown`；调整失焦关闭时机；更新候选时复用已打开的 Popup。已经做过静态检查和原有 JS 脚本测试，**这些都不能证明实际 WPF 交互已修复**。
- 若工作目录中已有 v0.5.1 工程，先检查它；若只含 GitHub 上的 v0.4.0，应让用户提供/放置 `mdictviewer_v0.5.1.zip`，不要假设未提交到 GitHub 的 v0.5.1 修改已存在于远端。ZIP 内有一层 `MdictViewer/` 目录。
- **任务不是重写词典。** 先修复搜索交互，把 v0.4.0 已成功的模块保持稳定。

## 2. 工程结构与边界

```text
MdictViewer/
├── MdictViewer.sln
├── Mdict.Core/                     # MDX/MDD 二进制读取、Key 索引、前缀查找
├── Mdict.Application/
│   └── DictionaryService.cs       # Lookup、SearchPrefix、GetResource；内部加锁
├── Mdict.Wpf/
│   ├── MainWindow.xaml            # WordText、SuggestionPopup、SuggestionList、返回按钮
│   ├── MainWindow.xaml.cs         # 输入、候选、历史、统一 LookupAsync
│   ├── Mdict.Wpf.csproj
│   └── WebView/
│       ├── WebViewController.cs
│       ├── WebResourceHandler.cs
│       ├── AudioBridge.js
│       └── EntryBridge.js
├── MdictReaderTest/               # 24 项既有 Core 回归测试
├── dev-tests/entry_bridge_test.cjs
└── README.md
```

本阶段**优先只修改 `Mdict.Wpf/MainWindow.xaml`、`MainWindow.xaml.cs`**；按需更新 README、版本号及针对候选交互的最小测试。不要无依据地修改 `Mdict.Core`、`Mdict.Application`、WebView2、音频/词条桥接、MDD 资源处理。无需引入 MVVM 框架、新服务、新接口、新搜索索引或第三方 UI 控件。

已存在的调用关系：

```text
WordText.TextChanged
  -> 150 ms DispatcherTimer
  -> ProcessSuggestionQueueAsync()
  -> Task.Run(DictionaryService.SearchPrefix(prefix, 48))
  -> 去重，最多显示 12 项 -> Popup/ListBox

WordText.PreviewKeyDown / SuggestionList.PreviewMouseLeftButtonDown / 查词按钮
  -> MainWindow.LookupAsync(...)
  -> DictionaryService.Lookup(word)
  -> WebViewController.ShowEntryAsync(html)
  -> displayedWord / WordText / CommitHistory
```

**已核对 v0.5.1 关键成员：** `WordText_PreviewKeyDown`、`SuggestionList_PreviewMouseLeftButtonDown`、`WordText_TextChanged`、`ShowSuggestions`、`HasCurrentSuggestions`、`CloseSuggestions`、`WordText_LostKeyboardFocus`、`MainWindow_PreviewMouseDown`、`MainWindow_Deactivated`、`LookupAsync`、`SetBusy`。XAML 里 `Popup` 使用 `StaysOpen="True"`、`Focusable="False"`；`ListBox` 和 `ListBoxItem` 也设为不可获取键盘焦点，键盘选择应始终由 `WordText` 接收。`DictionaryService.SearchPrefix(string prefix, int limit)` 已有实现，线程安全依赖服务内部锁。

## 3. 必须实现的 v0.5.x 交互契约（方案 A：纯输入触发）

1. **仅用户输入变化触发搜索**：字母输入、粘贴、删除均可触发；只是点击/重新聚焦搜索框不触发；程序更新 `WordText.Text`（查询成功、内部链接、返回、打开词典）不触发。
2. **候选与输入**：150 ms 防抖；最多 12 个不区分大小写去重的真实 MDX Key，显示原 Key 和原有顺序；无匹配则关闭；Popup 浮动于搜索框下方，不挤压 WebView2。正常连续输入时列表可更新，但不要无谓闪烁或窃取输入焦点。
3. **键盘**：默认不选中候选；↓ 选中第一项，↑ 在无选中时选中末项，随后可循环移动；输入框焦点不转移；选中变化**不自动查词，也不改写输入框文字**。Enter：有当前有效且明确选中的候选时查该候选，否则查输入框原文。没有候选时 Enter 仍必须正常查词；Esc 关闭候选但保留输入，再次点击输入框不重新弹出；Tab 采用正常焦点移动并关闭候选。
4. **鼠标**：单击有效候选项立即查询该词。不能被 Popup 独立 HWND、失焦、`Deactivated`、预览鼠标事件、列表清空或 `busy` 标志抢先吞掉事件；点搜索按钮始终查询输入框文字，而非候选选中项；点窗口其它区域关闭候选。
5. **异步并发**：任何过期前缀的结果不得覆盖当前输入；窗口关闭/词典切换/开始导航/F9/返回后不得延迟弹出旧候选。同一时间原则上只执行一个前缀搜索，将待处理输入合并为最新值；UI 更新必须在 WPF Dispatcher 线程。
6. **查词与历史**：输入时不自动导航，只有 Enter、查词按钮或候选点击确认才导航；所有成功的查询复用同一个 `LookupAsync()` 和 v0.4.0 历史记录机制。手动/候选查询失败保留输入及原页面，且不新增历史；内部链接失败应保留当前词条并恢复显示 Key；F9 不加入历史。
7. **旧功能不回归**：英美/例句音频、HTML/CSS、图片/折叠、`entry://`、`#anchor`、F9、返回历史及 Core 自测保持正常。

## 4. Codex 的排查步骤（先实证，再修改）

**A. 环境与基线**

- 从解决方案根目录查看 `git status`、当前分支及 diff，保护用户已有修改；尽可能建立修复分支，例如 `fix/v0.5.1-search-interaction`。不强制重置工作区，不覆盖用户未提交代码。
- Windows 环境依次执行：

```powershell
dotnet restore .\MdictViewer.sln
dotnet build .\MdictViewer.sln
dotnet run --project .\MdictReaderTest -- --self-test
dotnet run --project .\Mdict.Wpf
```

- 打开实际 Oxford V14.3 MDX，保持五个 MDD 卷与 MDX 在同一目录。若 Codex 无法操作 Windows 图形界面，应明确列出尚未实机验证的步骤，不能宣称完整通过。

**B. 最小复现，分离两类故障**

- 场景 1：当前显示 `apple`，点击搜索框、全选输入 `banana`，**不依赖 Popup**，直接按 Enter；观察是否进入 `WordText_PreviewKeyDown`，是否调用 `LookupAsync("banana", ...)`，以及 `DictionaryService.Lookup()`、`ShowEntryAsync()`、`displayedWord` 的结果。
- 场景 2：输入 `appl`，等 150 ms 后按 ↓；观察 `WordText.IsKeyboardFocused`、`SuggestionPopup.IsOpen`、`SuggestionList.Items.Count`、`displayedSuggestionPrefix`、`HasCurrentSuggestions()` 和 `SelectedIndex` 的变化；然后按 Enter。
- 场景 3：输入 `appl`，**用鼠标选择和当前已显示词条不同的候选**；确认 `SuggestionList_PreviewMouseLeftButtonDown` 是否被触发、`ContainerFromElement` 是否正确获得 `ListBoxItem`、是否进入 `LookupAsync()`。
- 场景 4：Esc 关闭后不输入，仅点击文本框；确认不会重新弹出。修改字符后又能出现候选。
- 对比查词按钮与 Enter/候选鼠标的行为；如果查词按钮能用而两者不能，优先定位事件、焦点、Popup，而不是重新改动 MDX 查询算法。

**C. 最小诊断**

可以临时在上述事件、`CloseSuggestions()`、`SetBusy()` 和 `LookupAsync()` 加 `Debug.WriteLine`，至少记录时间、事件来源、输入、焦点、选中项、`busy`、`browserReady`、`dictionaryService.IsOpen`、`HasCurrentSuggestions()`、Lookup 结果/异常，以及 `ShowEntryAsync()` 成败。需要时在 Visual Studio 的“输出”窗口/断点查看。先以日志判断链路在何处中断，再修改。最终删除嘈杂的临时日志，或保留默认关闭的轻量诊断，不要引入复杂日志框架。

特别注意：`MainWindow_PreviewMouseDown`、`WordText_LostKeyboardFocus`、`MainWindow_Deactivated` 与 `SuggestionList_PreviewMouseLeftButtonDown` 之间可能存在时序问题；`Popup` 拥有独立窗口。这里是**需要验证的嫌疑点，而不是已经证实的根因**。检查鼠标事件路由、焦点和失活顺序，避免通过增加大量延迟或反复 `Focus()` 来掩盖问题。`HasCurrentSuggestions()` 会因前缀变化后的失效标记而拒绝选择旧列表，这是预期保护，但必须保证新结果可被选中。

**D. 小步修复与回归**

每修复一条故障链就重新执行最小复现；优先确保“Enter 查词”通畅，再修方向键，再修鼠标；最后检查防抖/焦点/并发及历史/F9。必要时给搜索逻辑增加少量可测试方法或自动化测试，但避免为了单个窗口重新设计架构。

## 5. 完成标准（请逐项报告 PASS / FAIL / NOT TESTED）

1. 在 `apple` 页面输入 `banana` 直接 Enter，显示 `banana`；查询按钮也正常。
2. 输入 `appl` 显示候选；↓/↑ 选择有效，Enter 打开选中候选；焦点仍在输入框。
3. 未手动选中候选时 Enter 只查用户输入，而不是自动查第一项。
4. 鼠标单击候选词立即切换到对应页面；不能只更新搜索框或关闭 Popup。
5. Esc 后点击框不自动弹出；再次编辑文本后可以重新触发。
6. 快速输入、删除、粘贴时只显示当前前缀有效结果；不会无谓闪烁、卡界面或发生过期查询覆盖。
7. 手动或候选查询失败：原页面与历史不变，输入可继续修改。
8. 手动查询、候选查询、`entry://`、历史返回协作正确；`Idioms` 锚点保持原生跳转。
9. 英美音、折叠、图片放大、F9 均通过回归。
10. `MdictReaderTest --self-test` 原有 24 项全部通过；`node .\dev-tests\entry_bridge_test.cjs`（若有 Node.js）通过。

## 6. 交付与版本管理

- 未修复前不要将 v0.5.1 称为稳定版；修复后建议版本号升为 **v0.5.2**，更新窗口标题、README、必要的测试说明。若用户仓库版本号规则不同，遵循仓库约定。
- 提交清晰、最小的 diff；报告修改的文件、确认的根因、修复方式、构建/测试命令及结果、尚未验证的点和是否存在回归。
- **不要未经用户确认执行 `git push`、覆盖远端分支、删除未提交代码或合并到 `main`**；可以先创建本地提交/修复分支，再交由用户审查。
- 若需要给用户交付 ZIP，使用版本化名称（例如 `mdictviewer_v0.5.2.zip`），保留完整解决方案、README 和 `.gitignore`；不要包含 `bin/`、`obj/`、MDX/MDD 大文件或本机私有路径。
- 此版本完成后先让用户在 Windows 上验收，不在同一修复任务内扩展拼写纠错、模糊搜索、全文检索、前进历史或浏览器重构。

## 7. 给 Codex 的第一条指令

> 请先阅读本 HANDOFF 和当前项目中的 README、`Mdict.Wpf/MainWindow.xaml`、`MainWindow.xaml.cs`、`Mdict.Application/DictionaryService.cs`。以用户已提交 GitHub 的 v0.4.0 为稳定参照，以 v0.5.1 为待验收源码。先检查当前 Git 状态并确认两个版本差异；在 Windows 上构建并按第 4 节复现候选列表可显示却无法通过方向键、Enter、鼠标提交查询的问题。优先用断点或最小日志定位真实原因，不要先盲改。按第 3、5 节的交互契约完成最小修复；如需发布补丁，使用 v0.5.2。不要改动已稳定的 MDX/MDD Core、WebView2 音频、内部词条跳转与 F9 逻辑，除非有直接证据证明必须修改。最终提供 diff、测试结果、未验证项及建议的用户实机验收步骤。
