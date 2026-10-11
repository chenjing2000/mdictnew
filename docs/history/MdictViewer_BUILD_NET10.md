# .NET 10 本机构建记录

日期：2026-10-10。功能版本仍为 v0.6.0。

## 环境与选择

- 使用 `C:\Users\CHEN\.dotnet\dotnet.exe`，SDK 10.0.400。
- 已安装 .NET / Windows Desktop Runtime 10.0.11。
- 系统 PATH 的 `dotnet` 指向 `C:\Program Files\dotnet`，其中仅有 SDK 8.0.424 / Runtime 8.0.30；本次不使用它，也不修改系统 PATH。
- 本机 .NET Framework 注册表 Release 为 533509，对应 4.8.1。但本项目使用现代 .NET WPF，不能同时用旧式 .NET Framework 作为运行框架，因此升级为 .NET 10，而不迁移到 net481。版本映射参见 [Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed)。
- Core、Application、ReaderTest 目标为 `net10.0`；WPF 和 WPF 测试目标为 `net10.0-windows10.0.17763.0`。
- `global.json` 选择 SDK 10.0.400，允许同功能带的更新补丁。运行配置声明 .NET 10.0.0 基线，由主机自动采用已安装补丁；运行证据确认实际采用 10.0.11。

不要在 WPF 项目设置全局 `RuntimeFrameworkVersion=10.0.11`：它也会覆盖 Windows SDK 引用包版本，产生重复下载项并导致 NETSDK1112。保留 SDK 自动解析即可。

## 构建和发布

在项目目录执行：

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build MdictViewer.sln -c Release
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project MdictReaderTest -c Release -- --self-test
& "$env:USERPROFILE\.dotnet\dotnet.exe" run --project dev-tests/Mdict.Wpf.Tests -c Release -- "D:\Softs\Dictionaries\牛津英汉双解词典第10版（淘宝）\牛津高阶（第10版 英汉双解） V14_3.mdx"
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish Mdict.Wpf/Mdict.Wpf.csproj -c Release --no-restore -o dist/MdictViewer-v0.6.0-net10
```

发布入口：`E:\Projects\MdictViewer\dist\MdictViewer-v0.6.0-net10\MdictViewer.exe`。这是依赖框架的发布，需要已安装 .NET 10 Desktop Runtime 和 WebView2 Runtime；运行时未打包进发布目录。本机 `DOTNET_ROOT=C:\Users\CHEN\.dotnet`，直接运行测试 EXE 已验证可以找到该运行时。其它机器需要自行安装相应运行时，或设置对应的 `DOTNET_ROOT`。

原 `dist/MdictViewer-v0.6.0` 为 .NET 8 历史发布，保留不覆盖。

## 回归与发现的修复

- Release 构建：0 警告、0 错误。
- Core 自测：24 项通过。
- EntryBridge：12 项通过。
- LayoutBridge：9 项通过。
- 真实 Oxford WPF 集成测试：78 项通过、0 失败。
- 修复后通过 `dotnet run` 和直接运行测试 EXE 各完成一轮 78 项回归；两者均记录 `.NET 10.0.11`，WPF 程序集实际来自 `Microsoft.WindowsDesktop.App/10.0.11`。
- 已查看真实 Oxford、紧凑多词典结果及设置截图，未见重叠。

复测曾发现 CSSOM 折叠高度更新与宿主尺寸回调碰撞：原布局桥接把它当作视口反馈而忽略，导致结果区偶发不缩短。新增可稳定复现的 Node 回归，确认失败后修复；只抑制视口反馈引起的增长，不丢弃内容缩短消息。集成测试等待实际高度条件而非固定睡眠。词典读者、查词历史、设置和音频逻辑未改。

当前日志：

- `dev-tests/net10-build-results.txt`
- `dev-tests/net10-core-results.txt`
- `dev-tests/net10-entry-results.txt`
- `dev-tests/net10-layout-results.txt`
- `dev-tests/net10-wpf-results.txt`
- `dev-tests/net10-apphost-results.txt`
- `dev-tests/net10-publish-results.txt`

截图位于 `dev-tests/Mdict.Wpf.Tests/bin/Release/net10.0-windows10.0.17763.0/v0.6.0-*.png`。

剩余限制：路由事件和 DOM 自动化不替代人工原生键鼠及音频试听；不保证所有第三方 MDX、复杂视口相对 CSS 和词典脚本兼容。
