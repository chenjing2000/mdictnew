# MdictViewer

C# / .NET 10 / WPF + WebView2 的本地 MDX 查词程序。已用 Oxford 第 10 版 V14.3 MDX 和五个配套 MDD 分卷进行集成测试，不保证兼容所有第三方词典格式。

唯一发布入口：`dist/MdictViewer/MdictViewer.exe`。

## 当前功能

- 启动输入框和结果区留空，不显示词典名称及容器；只打开配置的词典，不查示例词、不恢复旧词、不读取词条索引，浏览器首次命中时初始化。
- 回车查词，输入框保留焦点；顶部包含后退、前进和设置图标。
- 主窗口和设置窗口的信息栏只显示英文 Warning / Error；正常启动、查词、复制、无匹配结果和空输入保持静默。异常详情使用异常类型，避免系统语言带来中文提示。查词输入框及词典页面的默认右键菜单禁用。
- 标题栏仅显示 `MdictViewer`，主窗口最小尺寸为 450×450。输入框最小宽度 100、高度 30、字号 15，宽度随窗口伸缩；右侧三个按钮为 30×30，图标同步缩小，尺寸均为 WPF 逻辑像素。窗口外轮廓继续采用系统边框。
- 设置最多 8 个 MDX 文件路径，按配置顺序显示本次有内容的结果区；名称使用不带扩展名的 MDX 文件名，左侧按钮可折叠/展开结果，新查询默认展开。
- 词典标题行背景为 `#007175`，名称与左侧三角形同为 `#ecebc2`，三角形按钮背景透明；三角形缩小 20% 至 12.8×12.8，保留原有按钮点击区域。
- 设置使用单选文件名列表，不显示或编辑路径；底部依次为文件夹、上移、下移、减号图标按钮。选文件追加到末尾并选中，排序保持选中；每次成功操作立即保存配置，关闭设置后才刷新主窗口，无保存或取消按钮。最多 8 项且拒绝重复文件。
- 设置页统一字号 14，列表上下内边距为 6，四个按钮为 `32 × 32`，图标和间隔同步缩小。主窗口不设置最大宽高限制，由 Windows 管理正常最大化行为；设置窗口默认 `450 × 320`，最小 `420 × 300`，最大 `500 × 500` 逻辑像素且受工作区限制；禁用最大化按钮，保留边缘缩放。底部按钮距内容区底边 8，错误行在按钮下方且无错误时折叠。
- 圆形底色 `#ddc5b8`、绘制式 M `#106898` 的图标用于窗口、任务栏与 EXE，包含七种尺寸和透明外围。圆形减少留白，M 比首版放大 15%。
- 150 毫秒防抖候选，最多 12 项，支持方向键、回车和鼠标选择。输入框失焦、窗口开始移动或缩放、查询或按 Esc 时关闭并取消待处理候选；仅重新聚焦不自动弹出，再次输入才显示。
- 手动查询、候选和内部词条链接统一更新全部词典，共用最多 50 条内存历史；后退后查新词删除前进分支。
- 每次查询先清空旧结果；未命中、空白内容或失败的词典不显示容器，全部未命中结果区留空且不新增历史。空输入回车也清空显示。手动失败保留编辑文字，内部链接失败恢复此前查询词；错误在底部汇总。
- 每个词典独立读取 CSS、图片和音频，支持同页锚点、折叠、图片缩放与外层滚动。页面统一设置 14px 基础字号，保留词典自身的标题层次和明确指定的字号，不强制覆盖全部文字。
- 资源优先从配套 MDD 读取；未命中时读取当前 MDX 同目录或子目录中的独立 CSS、图片、字体、脚本与音频，拒绝越出词典目录的路径和资源重解析点。
- 鼠标拖选文字并松开后自动复制；普通双击按浏览器规则选词、复制并查询全部词典。普通点击不复制，链接、音频及折叠控件保留原操作。两处提示显示 6 秒后清空并收起，新提示重新计时；两个窗口左上角图标左击不弹出系统菜单。

路径设置保存在 **EXE 同目录的 `dictionaries.json`**，以 UTF-8 无 BOM 保存，中文直接显示，保持缩进。启动只读取，不存在时保持空配置；首次点击设置创建 `[]`，关闭不会撤销成功操作；每次变更按列表顺序写入最多 8 个 MDX 的绝对路径，允许空列表，保存失败保持原列表。已有配置不会因打开设置而被覆盖。程序没有内置词典路径或默认词典列表。命令行 MDX 路径只覆盖本次启动，不改写保存的设置；设置窗口关闭后应用成功变更，重置历史并重新查询当前已显示词；尚未显示的输入文字不自动查询，无变更不刷新。

配套资源按 MDX 文件名自动发现同目录的同名 `.mdd`、`.1.mdd`、`.2.mdd` 等数字分卷，不固定数量。设置中选择的是 **MDX 文件**，不是文件夹。旧版 `%LocalAppData%/MdictViewer/dictionaries.json` 不自动迁移；需要沿用时，将它复制到 EXE 同目录，或在设置中重新选择词典。

## 构建与运行

发布版需要 Windows x64、**.NET 10 Desktop Runtime x64** 和 **WebView2 Runtime**，不需要 SDK。两种运行时均不打包、不自动安装：缺少 .NET 时由原生启动器提示安装；缺少 WebView2 时主窗口提示安装并提供官方下载链接。开发需要 .NET 10 SDK；`global.json` 固定 SDK 10.0.400，允许同功能带的更新补丁。本次验证使用 SDK 10.0.401、运行时 10.0.12。本项目不是旧式 .NET Framework 项目。

使用 PATH 中的 .NET 10 SDK：

```powershell
dotnet run --project .\Mdict.Wpf
```

也可运行 `Mdict.Wpf/run.cmd`。三个 CMD 启动脚本优先使用符合项目 SDK 要求的用户目录 `dotnet.exe`，否则使用 PATH。其他机器若 SDK 10 已在 PATH，可直接使用 `dotnet`。

统一构建并发布为依赖本机运行时的单个 EXE（先关闭正在运行的发布程序）：

```powershell
.\tools\Publish.ps1
```

脚本先在临时目录发布并验证只有 `MdictViewer.exe`，再替换 `dist/MdictViewer` 中的发布程序，保留用户配置；成功后清除旧文件夹发布的已知依赖和三个应用项目的重复 `bin` 输出。应用 DLL、WebView2 托管组件及原生加载器包含在 EXE 中；原生库由 .NET 在运行时解压到缓存，不需要旁放 DLL。浏览器缓存位于 `%LocalAppData%/MdictViewer/WebView2`。保留可再生成的 `obj` 编译缓存，不创建 `artifacts` 或按修改版本命名的发布目录。`dotnet run` 用于开发，会产生常规 `bin/obj` 输出。

## 验证

```powershell
dotnet run --project .\MdictReaderTest -- --self-test
node .\dev-tests\entry_bridge_test.cjs
node .\dev-tests\layout_bridge_test.cjs
dotnet run --project .\dev-tests\Mdict.Wpf.Tests -- "<Oxford MDX 完整路径>"
dotnet run --project .\dev-tests\Mdict.Wpf.Tests -- --local-resources
dotnet run --project .\dev-tests\Mdict.Wpf.Tests -- --settings
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- --notifications
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- --published .\dist\MdictViewer\MdictViewer.exe
```

此前基线：Core 自测 28 项；单个真实 MDX 的 WPF 集成检查 128 项；Entry / Layout 桥接分别 18 / 10 项；独立资源检查 7 项；设置列表与中文配置检查 26 项；发布 EXE 检查 9 项，共 226 项。WPF 测试可额外传入第二个 MDX 路径，增加一项真实多词典检查，不猜测外部文件名。测试使用临时设置，覆盖原有查询、历史、候选、资源和界面，以及配置首次初始化、非法配置、写入失败、异步资源统计、缺少 WebView2、实际单文件运行和缺少 .NET 的原生安装提示。

按修改范围运行：设置修改先跑 `--settings`，资源修改跑 `--local-resources`，查词或浏览器修改跑 WPF 与相应 JS 检查；正式发布前运行 `--published`。发布脚本会清理应用 `bin`，不要与构建或测试同时执行。常规回归不再生成截图，也不检查图标像素数量等外观细节。

这些测试不替代原生键鼠操作和实际音频试听，也不证明所有词典脚本兼容。重新构建默认输出中的主程序前，应关闭对应实例，避免文件占用。

## 代码与记录

- [模块职责与数据流](docs/architecture.md)
- [单文件发布、运行时与用户配置](docs/single-file-deployment.md)
- [仅英文 Warning / Error 的信息提示规则与验证](docs/notification-policy.md)
- [查询刷新、隐藏容器与折叠](docs/result-visibility.md)
- [独立 CSS 与同目录资源读取修复](docs/local-resources.md)
- [最新代码审查、整改与清理记录](docs/code-audit-2026-10-12.md)
- [此前整体复查记录](docs/final-audit.md)
- [启动留空、字号、图标与窗口限制](docs/startup-and-appearance.md)
- [代码审查、整改和清理记录](docs/review-and-cleanup.md)
- [历史 handoff、调试记录和方案](docs/history/README.md)

源码、正式测试、图标工具和历史文档保留；旧构建、旧发布、临时诊断工具、日志和截图已清理，不保留 `artifacts`。发布统一位于 `dist/MdictViewer`，由 `.gitignore` 排除。历史文档中的旧生成文件路径仅作为当时的记录，文件已清理。词典文件、WebView2 缓存和个人 IDE 设置不进入版本管理；合法的图片、HTML 和音频资源不被全局忽略。

暂不实现模糊查询、全文搜索、拼写纠错、新索引系统或历史持久化。精确键映射、前缀线性扫描及现有记录定位保持不变，实际出现瓶颈后再测量和优化。
