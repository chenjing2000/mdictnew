# 统一发布流程

在仓库根目录执行，需要 Windows 和 .NET 10 SDK；先关闭正在运行的程序：

```powershell
.\tools\Publish.ps1
```

发布脚本调用 Mdict.Wpf/Properties/PublishProfiles/Portable.pubxml 中的 Portable 配置。四个项目统一采用 Release、win-x64、依赖本机运行时的单文件发布，嵌入原生加载器，不裁剪、不压缩、不分发 PDB 或 XML 文档。正式输出为 dist/MdictViewer.exe。用户需要 .NET 10 Windows Desktop Runtime x64，普通 .NET Runtime 不足以运行 WPF；不需要 SDK。

脚本先在 dist 下的唯一临时目录构建，排除调试符号和 XML 文档，确认剩余产物只有一个 EXE，再在同一卷中原子替换已有 EXE。构建失败、意外的额外依赖或 EXE 被占用时停止；保留旧程序、已有配置和其他用户文件。最终删除临时目录，不清理 bin/obj 构建缓存。同一项目不要同时执行发布、构建或测试。

若 Windows 执行策略阻止脚本，可仅对这次进程使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
```

验证发布保护逻辑，不需要实际编译程序：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
```

测试只替代外部 dotnet 构建过程，实际运行发布脚本并操作临时文件，覆盖失败构建、意外 DLL、EXE 占用和用户配置保留。正式 EXE 需单独验证，结果见[本次体检](Health-Check-2026-10-13.md)。

GitHub 分发统一通过唯一的 portable-* 标签：先审查、验证并提交源码，再推送标签。.github/workflows/release.yml 在 Windows 上执行同样的保护测试和发布脚本，使用 docs/Release-Notes.md 创建 Release 并上传 EXE。普通提交不会触发 Release；后续发新标签前更新发布说明。EXE 作为 Release 附件，不将 dist、用户配置、词库或媒体提交进 Git。
## MdictViewer 旧目录迁移

还需要本机 WebView2 Runtime；浏览器运行时不捆绑进 EXE。

首次统一发布时，如果 dist/MdictViewer/MdictViewer.exe 仍存在，脚本将旧目录中的 dictionaries.json 复制到 dist/dictionaries.json；新旧配置内容不一致时停止，两个文件都保留。成功替换新 EXE 后删除旧 EXE，原配置留在旧目录作为备份，其他用户文件不移动、不删除。迁移后的发布不再读取这份旧备份，避免覆盖新设置。新的启动入口是 dist/MdictViewer.exe。