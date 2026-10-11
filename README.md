# MdictViewer

C# / .NET 10 / WPF + WebView2 的本地 MDX 查词程序。已用 Oxford 第 10 版 V14.3 MDX 和五个配套 MDD 分卷进行集成测试，不保证兼容所有第三方词典格式。

当前发布入口：`dist/MdictViewer/MdictViewer.exe`。

## 当前功能

- 启动输入框和结果页留空，只打开配置的词典，不查示例词、不恢复旧词、不读取词条索引。
- 回车查词，输入框保留焦点；顶部包含后退、前进和设置图标。
- 设置最多 8 个 MDX 文件路径，按配置顺序纵向显示独立结果区，每区显示词典名称。
- 圆角路径输入框和按钮，绘制的加号、减号与文件夹图标。
- 设置页统一字号 15。主窗口最大尺寸等于所在显示器的可用工作区，不设固定上限；设置窗口仍最大 `1000 × 700` 逻辑像素且受工作区限制。
- 圆形底色 `#ddc5b8`、绘制式 M `#106898` 的图标用于窗口、任务栏与 EXE，包含七种尺寸和透明外围。圆形减少留白，M 比首版放大 15%。
- 150 毫秒防抖候选，最多 12 项，支持方向键、回车、鼠标选择和 Esc 关闭。
- 手动查询、候选和内部词条链接统一更新全部词典，共用最多 50 条内存历史；后退后查新词删除前进分支。
- 部分未命中或失败只影响对应结果区；全部未命中保留页面和历史。手动失败保留编辑文字，内部链接失败恢复已显示词。
- 每个词典独立读取 CSS、图片和音频，支持同页锚点、折叠、图片缩放与外层滚动。

路径设置保存在 `%LocalAppData%/MdictViewer/dictionaries.json`。命令行 MDX 路径只覆盖本次启动，不改写保存的设置；修改词典配置重置历史并重新查询当前已显示词。

Oxford 的 MDX 同目录应包含同名 `.mdd`、`.1.mdd` 至 `.4.mdd`。设置中选择的是 **MDX 文件**，不是文件夹。

## 构建与运行

需要 Windows、.NET 10 SDK / Desktop Runtime 和 WebView2 Runtime。`global.json` 固定 SDK 10.0.400，允许同功能带的更新补丁；本机使用 SDK 10.0.400、运行时 10.0.11。本项目不是旧式 .NET Framework 项目。

本机 PATH 中仍是 .NET 8，使用已安装的用户目录 SDK：

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet build .\MdictViewer.sln -c Release --artifacts-path artifacts/build
& $dotnet run --project .\Mdict.Wpf
```

也可运行 `Mdict.Wpf/run.cmd`。三个 CMD 启动脚本优先使用符合项目 SDK 要求的用户目录 `dotnet.exe`，否则使用 PATH。其他机器若 SDK 10 已在 PATH，可直接使用 `dotnet`。

发布为依赖本机运行时的程序：

```powershell
& $dotnet publish .\Mdict.Wpf\Mdict.Wpf.csproj -c Release --artifacts-path artifacts/build -o dist/MdictViewer
```

## 验证

```powershell
& $dotnet run --project .\MdictReaderTest -- --self-test
node .\dev-tests\entry_bridge_test.cjs
node .\dev-tests\layout_bridge_test.cjs
& $dotnet run --project .\dev-tests\Mdict.Wpf.Tests -- "<Oxford MDX 完整路径>"
```

Core 自测 28 项；WPF 集成检查 115 项；Entry / Layout 桥接分别 12 / 9 项，共 164 项。WPF 测试会短暂打开窗口，使用独立临时设置，并验证真实词典、启动留空、窗口限制、图标、事件路由、DOM、候选取消、统一历史、多词典、设置界面与浏览器初始化失败后的重试。

这些测试不替代原生键鼠操作和实际音频试听，也不证明所有词典脚本兼容。重新构建默认输出中的主程序前，应关闭对应实例，避免文件占用。

## 代码与记录

- [模块职责与数据流](docs/architecture.md)
- [最新整体复查与清理记录](docs/final-audit.md)
- [启动留空、字号、图标与窗口限制](docs/startup-and-appearance.md)
- [代码审查、整改和清理记录](docs/review-and-cleanup.md)
- [历史 handoff、调试记录和方案](docs/history/README.md)

源码、测试和历史文档保留；旧构建、旧发布和旧测试缓存已清理。当前验证日志位于 `artifacts/final-audit`，发布统一位于 `dist/MdictViewer`，均由 `.gitignore` 排除。词典文件、WebView2 缓存和个人 IDE 设置不进入版本管理；合法的图片、HTML 和音频资源不被全局忽略。忽略规则已通过 40 项正反例检查。

暂不实现模糊查询、全文搜索、拼写纠错、新索引系统或历史持久化。现有线性搜索保持不变，实际出现瓶颈后再测量和优化。
