# 程序体检 - 2026-10-13

范围：发布流程、词典索引与资源、配置保存、WebView2 桥接、真实 Oxford WPF 集成及实际单文件 EXE。环境：Windows x64，.NET SDK 10.0.401，运行时 10.0.12。

## 结果

| 检查 | 通过 | 失败 |
| --- | ---: | ---: |
| Core 自测 | 28 | 0 |
| Entry Bridge | 18 | 0 |
| Layout Bridge | 10 | 0 |
| 独立资源 | 7 | 0 |
| 配置与设置 | 26 | 0 |
| 通知 | 10 | 0 |
| 真实 Oxford V14.3 WPF 集成 | 129 | 0 |
| 正式发布 EXE | 9 | 0 |
| 发布保护与配置迁移 | 7 | 0 |
| 合计 | 244 | 0 |

正式 EXE 为 25,757,420 字节，约 24.56 MiB。NuGet 已知漏洞检查（含传递依赖）未报告易受攻击的包。

## 检查与修改

实际发布检查覆盖 EXE 单独运行、中文词典配置、文件选择立即保存、查词与渲染、WebView2 原生加载器、缓存目录、缺少 WebView2 的下载入口，以及隔离运行时目录下缺少 .NET 的原生安装提示。真实词典集成覆盖候选取消、历史、词典间资源隔离、布局、原生双击和损坏资源处理；配置来源为已有配置，测试使用独立临时配置，不改写原词典。

旧版 EXE 位于 dist/MdictViewer/MdictViewer.exe。统一发布已将用户配置复制到 dist/dictionaries.json 并移除旧 EXE，旧目录的 dictionaries.json 保留作备份。新旧配置 SHA256 一致。测试覆盖迁移冲突拒绝，以及迁移后继续发布不会用旧备份覆盖新设置。

体检发现原 WPF 测试在异步检查未结束而窗口关闭时可能返回成功。测试入口现在要求完整执行结束，并在注入原生双击前检查目标位于可见结果区域；防止提前关闭误报通过和点击到窗口外。桌面原生输入检查需要独占前台；与其他窗口检查重叠的初次运行没有结束汇总，未算作完整通过。串行重跑输出 Checks: 129; Failures: 0。

发布参数集中到 Portable.pubxml。引用项目仍可能产生编译用 PDB，脚本在临时发布目录排除 PDB/XML 后严格验证剩余文件；没有删除任何运行所需 DLL 来伪造单文件。此前直接命令中的参数已统一由配置提供，业务功能和词典格式未改变。

## 限制

运行仍依赖 .NET 10 Desktop Runtime x64 和 WebView2 Runtime；原生加载器会解压到缓存，单文件分发不等于运行时不写文件。真实声音未人工试听，不能保证兼容所有第三方 MDX/MDD。没有测试第二个真实词典的可选参数；多词典行为已由合成夹具覆盖。

## 复核命令

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
dotnet run -c Release --project .\MdictReaderTest -- --self-test
node .\dev-tests\entry_bridge_test.cjs
node .\dev-tests\layout_bridge_test.cjs
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- --local-resources
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- --settings
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- --notifications
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- "<Oxford V14.3 MDX 完整路径>"
dotnet list Mdict.Wpf/Mdict.Wpf.csproj package --vulnerable --include-transitive
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
dotnet run -c Release --project .\dev-tests\Mdict.Wpf.Tests -- --published .\dist\MdictViewer.exe
```

迁移与分发规则见 [统一发布流程](Publishing.md)。旧审计文档中的发布路径和缓存清理属于对应历史版本，不是当前发布入口。
## Unified testing and acceptance verification

Full: core reader, entry/layout bridges, local resources, settings and notification checks passed, plus all 129 Oxford V14.3 native WPF checks. No Full groups were skipped. Published integration: 9 application-specific checks passed.

Published EXE acceptance passed: isolated EXE-only folder with spaces, renamed executable, a different working directory, correct configuration placement, clean shutdown, and native missing-.NET installation instructions under an isolated runtime root. Installed runtimes and original media were not modified. This verifies automated behavior; human listening and a fresh-machine graphical runtime prompt were not performed.

The orchestration regression passed success, failure rejection, and optional-data handling checks. PowerShell source was verified as English ASCII with no syntax errors. Use [Testing](Testing.md) for the shared entry points; stage counts and individual case counts are reported separately.

## Code audit final verification

Core, entry/layout, local-resource, 26 settings and 11 notification checks passed, together with all 129 Oxford V14.3 WPF integration checks. Nine application-specific published checks also passed. All Full groups passed with zero failures and zero skips. The newly published portable EXE passed isolated renamed-file launch, configuration placement, clean shutdown, and missing-.NET instruction checks. See [Code Review](Code-Review-2026-10-13.md) for module assessment, actual changes and preserved complexity.

Original article files and the selected audio were verified unchanged with 370 SHA-256 comparisons. UI and real playback checks were run serially. Manual listening and a clean-machine graphical runtime prompt were not performed. Regenerable compilation and test output are cleaned after verification; dist, settings and user media are retained.
