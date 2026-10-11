# MdictViewer v0.6.0 调试与交接

日期：2026-10-10。基于 v0.5.2，按用户已确认的多词典方案实现。

## 问题与原因

- 原查询会禁用输入框，导致回车查词后焦点转移。
- 原窗口只有单词典、单浏览器，没有前进、词典配置或独立多结果区。
- 普通 WPF WebView2 的原生窗口不适合外层滚动裁剪；改用已有 WebView2 包的组合式控件。其 WinRT 引用要求显式 Windows SDK 目标，项目改为 `net8.0-windows10.0.17763.0`。

## 改动与理由

- 输入框右侧依次为后退、前进、设置图标，颜色和形状按确认方案。移除查词按钮及顶部词典名。
- 查询时输入框只读但不禁用，保持焦点。回车、候选和词条链接统一查询全部词典。
- 设置对话框支持有效路径后出现加号、删除、浏览、最多 8 个 MDX；校验重复/不存在路径，按顺序原子保存到 `%LocalAppData%/MdictViewer/dictionaries.json`。取消不影响设置。
- 每个词典有独立 DictionaryService、WebViewController 与浏览器，标题在结果上方。共享外层滚动，但不共享文档、CSS 或 MDD 资源。
- 共用 50 条成功查词历史，支持后退/前进，后退后新查词丢弃前进分支。配置变更清空历史并重查当前已显示词。
- 候选按词典顺序合并、不区分大小写去重、最多 12 个、150 毫秒防抖。同一时刻只运行一个聚合候选请求，过时结果不会弹出。
- 部分未命中/失败在对应区域提示；全部未命中保留页面和历史。沿用旧规则：手动/候选失败保留编辑文字，内部链接失败恢复已显示词。
- 新增 LayoutBridge，报告正文高度、滚轮和锚点。解决重复锚点、缩放高度、视口高度反馈及纯 CSS/动画高度更新。消息校验来源、类型、有限数值和旧页面状态。
- 窗口关闭/配置替换释放浏览器；防止异步 Open 晚于 Close 重新打开废弃读者，保护尚在等待的资源回调。
- Core/Application 读者、AudioBridge 和 EntryBridge 未改。不新增词典解析依赖。

## 验证

- `dotnet build MdictViewer.sln -c Release`：0 警告、0 错误。
- `dotnet publish Mdict.Wpf/Mdict.Wpf.csproj -c Release --no-restore -o dist/MdictViewer-v0.6.0`：成功。
- Core：24 项自测通过。EntryBridge：12 项脚本测试通过；LayoutBridge 另有 8 项确定性检查。
- WPF Release：**78 项检查，0 失败**。实际执行日志：`dev-tests/v0.6.0-wpf-results.txt`。
- 实际 MDX：用户目录中的 V14_3 主词典及 V3_1 反查词典，分别显示对应查询结果。
- 合成 v2 MDX/MDD：验证两个词典同名 CSS/MP3 路径的资源隔离、8 个独立浏览器、部分缺失/打开失败、合并候选、统一链接和历史、设置保存/取消/无效路径、50 条上限。
- 浏览器真实 DOM：验证高度折叠、CSSOM 更新、Web Animation、`min-height:100vh` 稳定、1.5 倍缩放、重复锚点、统一滚轮、无效布局消息。
- 生命周期：5 次异步打开时立即关闭，及持有资源读锁/浏览器 deferral 时替换配置，均完成且未出现未处理 Dispatcher 异常。
- 已人工查看渲染截图：真实 Oxford 页面、两个独立结果、620×450 小窗口工具栏、设置窗口。截图在 `dev-tests/screenshots/`。
- 独立审查发现的高度反馈、重复锚点、缩放及关闭竞态已修正；纯 CSS 更新的二次审查问题已复现并修正。

## 运行

启动 `dist/MdictViewer-v0.6.0/MdictViewer.exe`，在齿轮设置中选择完整 `.mdx` 文件路径；匹配 MDD 分卷继续放在 MDX 同目录。发布目录不包含任何词典数据，依赖本机 .NET 8 Desktop Runtime 与 WebView2 Runtime。

也可临时传入主词典：

```powershell
.\dist\MdictViewer-v0.6.0\MdictViewer.exe "D:\Softs\Dictionaries\牛津英汉双解词典第10版（淘宝）\牛津高阶（第10版 英汉双解） V14_3.mdx"
```

命令行路径仅覆盖本次启动，不自动保存。设置中的路径保存后下次启动恢复；历史不持久化。

## 剩余风险

- 自动化使用真实 WPF 路由事件与 WebView2 DOM，不等同于原生鼠标/键盘的完整人工验收。
- 已验证音频资源隔离与返回字节，尚需人工试听英美音/例句、验收真实图片放大和原词典交互。
- 任意第三方 MDX 压缩/加密/脚本兼容性未承诺；继续沿用当前 Oxford 读者。视口高度相对布局会抑制宿主扩展造成的二次增高，防止无限循环，但不保证任意 `vh`/百分比超屏版式完整适配。大量超长词条和其他 Windows/显卡环境未做压力/兼容性测试。
- 未改动用户词典文件；测试设置与合成词典放在临时目录并清理。本工作目录没有 Git，未创建提交。

技术参考：[Microsoft WPF WebView2 文档](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf)。
