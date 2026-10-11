# 设置页外观更新

日期：2026-10-10。保留 v0.6.0 功能版本，使用 .NET 10。

## 改动

原设置页使用系统默认按钮、输入框和字体图标。现在采用窗口内的局部样式，不影响主界面：

- 所有按钮和路径输入框为 6px 圆角。
- 添加、删除、浏览按钮固定 40 x 40，背景为 `#f5f2e9`。
- 三个图标为 24 x 24 的 WPF DrawingImage。加号、减号是居中的 3px 圆头线条；打开的文件夹为 2.5px 圆头线条，颜色均为 `#ba5b49`。
- 图标没有使用键盘字符、图标字体或新增依赖；矢量图片可以随 DPI 缩放。
- 按钮增加悬停、按下、禁用状态，按钮和输入框具有蓝色键盘焦点边框。
- 保存和取消按钮保持文本，统一圆角和间距。

只修改 `Mdict.Wpf/SettingsWindow.xaml`、`SettingsWindow.xaml.cs` 以及对应测试和说明。路径浏览、添加删除、校验、保存取消、最多 8 个路径、查词与历史行为不变。

## 验证

- 新增 7 项外观检查先在旧版上验证失败；原有 78 项功能检查全部通过。
- 修改后首次 85 项 WPF 检查全部通过。补充最小窗口、8 行滚动和按钮焦点检查后，最终 87 项全部通过。
- Core 24 项、EntryBridge 12 项、LayoutBridge 9 项全部通过，共 132 项不同检查。
- 独立目录 Release 构建为 0 警告、0 错误，发布成功。
- 测试记录确认实际使用 .NET 10.0.11 和 WindowsDesktop 10.0.11。
- 已查看正常设置页及最小窗口 8 个路径的截图，按钮图案清晰居中，路径列表滚动，底部操作没有重叠。

日志为 `dev-tests/settings-style-build-results.txt`、`settings-style-wpf-results.txt`、`settings-style-core-results.txt`、`settings-style-publish-results.txt`。失败基线保留在 `dev-tests/settings-style-red-results.txt`。

截图位于 `dev-tests/artifacts-settings-style/bin/Mdict.Wpf.Tests/release/v0.6.0-settings.png` 和 `settings-style-eight-compact.png`，使用测试路径，不修改用户配置。

## 发布入口

`E:\Projects\MdictViewer\dist\MdictViewer-v0.6.0-net10-settings\MdictViewer.exe`

旧程序仍在运行，默认构建目录的 EXE 被它锁定，因此使用独立产物目录构建，未关闭旧窗口、未覆盖旧发布。

复现构建与发布：

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build MdictViewer.sln -c Release --artifacts-path dev-tests/artifacts-settings-style
& "$env:USERPROFILE\.dotnet\dotnet.exe" publish Mdict.Wpf/Mdict.Wpf.csproj -c Release --no-restore --artifacts-path dev-tests/artifacts-settings-style -o dist/MdictViewer-v0.6.0-net10-settings
```

自动化使用 WPF 路由事件及模板、绘图对象检查，不替代真实鼠标、文件选择对话框和其它 DPI 下的人工验收。文件选择对话框及原生窗口标题栏仍由 Windows 绘制，本次未修改。
