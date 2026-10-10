# MdictViewer v0.4.0

纯 C# / .NET 8 / WPF + WebView2，适配已经实测的 Oxford Advanced Learner's Dictionary 10 V14.3 MDX 及其五个 MDD 分卷。

## v0.4.0：Oxford 内部词条跳转及返回历史

基于 Windows 实机验证通过的 v0.3.2（HTML、原词典 CSS/JS、插图、英美音、F9）。本版新增：

- 点击 Oxford 释义中的 `entry://cooking-apple`、`entry://the-big-apple`、`entry://appleCC` 等链接时，使用原有 `DictionaryService.Lookup()` 打开新词条并更新搜索框。
- 增加搜索框左侧的 **← 返回上一词条** 按钮，统一记录成功的手动查词和内部链接跳转；上限 50 条。F9、发音、图片和 `#...` 页内锚点不进入历史。
- 找不到目标词条时保留原页面和历史，只在状态栏提示；**不会**将失败查词追加历史。
- 返回时重新查词并从页面顶部显示；不恢复旧页面的滚动/折叠状态。返回后新查词会丢弃旧的前进分支。
- 在 `NavigationStarting` 中同步取消内部 `/lookup` 中转导航，然后经 WPF Dispatcher 在事件之外调度新查词；被主动取消的中间导航不会进入普通词条的完成/失败判定。
- `EntryBridge.js` 只捕获 `entry://` 的点击，按原始 href 解码一次后编码转发。完整保留 `@`、`&`、`+` 等 Key 标识，不把它们错误拆解为 URL 主机/参数。
- **不修改** `Mdict.Core`、`Mdict.Application`、原有 `AudioBridge.js`、`WebResourceHandler.cs` 及其 24 项 Core 自测。

## 代码与模块边界

- `Mdict.Core`：原 Oxford MDX/MDD 解析算法、内存 Key 索引及按需资源读取。
- `Mdict.Application`：现有 `DictionaryService` 负责查词和资源访问，仍然序列化 Reader 调用。
- `Mdict.Wpf/WebView/EntryBridge.js`：仅识别/转换用户点击的内部词条链接。
- `Mdict.Wpf/WebView/WebViewController.cs`：仅负责浏览器导航、内部 `/lookup` 拦截、向 UI 发 `EntryLookupRequested` 事件；不管理历史或读 MDX。
- `Mdict.Wpf/MainWindow.xaml.cs`：单一查词入口、搜索框同步、50 条内存历史及返回按钮。
- `MdictReaderTest`：保持已验证的 24 项回归测试，直接引用 Core，无第二份解析源码。

`entry://...` → EntryBridge → `https://mdict.invalid/lookup?key=...` → WebViewController（取消内部导航并通知 UI）→ MainWindow → DictionaryService → Core → WebView2 显示新词条。

普通 `sound://`、`#anchor`、Oxford 折叠/图片放大仍由 v0.3.2 的原有机制处理。不需要打开 WebMessage 或暴露高权限 C# 对象。

## Windows 验证步骤

在 Windows 10/11，安装 .NET 8 SDK、WebView2 Runtime。解压后从解决方案根目录执行：

```powershell
dotnet restore .\MdictViewer.sln
dotnet build .\MdictViewer.sln
dotnet run --project .\MdictReaderTest -- --self-test
dotnet run --project .\Mdict.Wpf
```

打开 Oxford V14.3 MDX（其同名 `.mdd`、`.1.mdd` 至 `.4.mdd` 在同文件夹），尝试：

1. 查询 `apple`，点击 “see also” 的 `cooking apple`，新页面应显示，对应搜索框更新；点击 “←” 返回 apple。
2. 点击 `Big Apple` 和 `appleCC` 等词典内部链接；某些 Oxford 私有标识可能不存在对应的 MDX Key，失败时应保留原词条并提示。
3. 手动查 `banana`、点击内部词条后多次返回；手动查词与内部跳转共用历史。当前词条重复查询不产生连续重复记录。
4. 点击 `Idioms` 仅滚动到习语；点击英美音仍正常发声；折叠和图片放大均正常。
5. 未找到词条应只更新状态栏，原页面仍显示；F9 自测后按“查词”应恢复 Oxford 页面。原有 24 项核心测试全部通过。
6. 测试用户快速连续点击内部链接；不应出现 `OperationCanceled`、白屏或把旧词条显示为新词条。

可选（有 Node.js 的开发电脑）：

```powershell
node .\dev-tests\entry_bridge_test.cjs
```

这是不依赖 Oxford 文件、Node 内置模块驱动的脚本事件模拟测试，不是运行程序的必需项。

## 暂不支持

- 前进按钮、历史持久化、滚动位置恢复、全文搜索、多词典模式。
- 未经实测的 Oxford 特殊 `@@@LINK` 链式重定向；如在真实内部链接中遇到，再根据明确样本处理。
- `$env:MDICT_DISABLE_SCRIPTS='1'` 会同时关闭 Oxford 脚本、音频桥接和内部链接桥接（用于调试脚本问题）。
- 已有的“资源成功 7 / 失败 1”尚未确定具体失败请求，不修改已通过的资源读取行为。

## 验证说明

此交付环境未安装 Windows/.NET SDK/WebView2 Runtime，不能在此执行 Windows WPF 程序，也不能在真实 Oxford 上实测该版本。已检查 XAML / 项目 XML、嵌入资源名称、JS 语法、模拟 `entry://` 点击和 ZIP 完整性；仍需根据 Windows 设备的构建和交互结果最终验收。
