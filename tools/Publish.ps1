$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $workspace 'dist\MdictViewer'
$stagingDirectory = Join-Path $publishDirectory ('.publish-' + [Guid]::NewGuid().ToString('N'))

Push-Location $workspace
try {
    dotnet publish .\Mdict.Wpf\Mdict.Wpf.csproj -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false `
        -p:PublishDocumentationFiles=false -o $stagingDirectory --nologo
    if ($LASTEXITCODE -ne 0) {
        throw '发布失败，现有输出已保留。请检查构建错误或关闭占用发布文件的程序。'
    }

    # XML documentation is not needed at runtime; never drop a DLL to fake a single-file package.
    Get-ChildItem -LiteralPath $stagingDirectory -Filter '*.xml' -File | Remove-Item -Force
    $output = @(Get-ChildItem -LiteralPath $stagingDirectory -Force)
    if ($output.Count -ne 1 -or $output[0].Name -ne 'MdictViewer.exe') {
        throw '发布输出不是单个 EXE，现有发布及配置已保留。'
    }
    Move-Item -LiteralPath $output[0].FullName -Destination (Join-Path $publishDirectory 'MdictViewer.exe') -Force

    # Remove only known files from the former folder deployment; preserve user configuration.
    foreach ($stem in @('Mdict.Core', 'Mdict.Application', 'MdictViewer',
        'Microsoft.Web.WebView2.Core', 'Microsoft.Web.WebView2.WinForms', 'Microsoft.Web.WebView2.Wpf',
        'Microsoft.Windows.SDK.NET', 'WinRT.Runtime')) {
        foreach ($extension in @('.dll', '.pdb', '.xml', '.deps.json', '.runtimeconfig.json')) {
            $oldFile = Join-Path $publishDirectory ($stem + $extension)
            if (Test-Path -LiteralPath $oldFile) { Remove-Item -LiteralPath $oldFile -Force }
        }
    }

    $cleanupDirectories = @('Mdict.Core\bin', 'Mdict.Application\bin', 'Mdict.Wpf\bin',
        'dist\MdictViewer\runtimes', 'dist\MdictViewer\MdictViewer.exe.WebView2')
    foreach ($relativePath in $cleanupDirectories) {
        $buildDirectory = [IO.Path]::GetFullPath((Join-Path $workspace $relativePath))
        if (-not $buildDirectory.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "构建目录超出项目范围：$buildDirectory"
        }
        if (Test-Path -LiteralPath $buildDirectory) {
            $items = @(Get-Item -LiteralPath $buildDirectory) + @(Get-ChildItem -LiteralPath $buildDirectory -Force -Recurse)
            if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
                throw "清理目录含重解析点，已保留：$buildDirectory"
            }
            Remove-Item -LiteralPath $buildDirectory -Recurse -Force
        }
    }
    Write-Host "发布完成：$publishDirectory\MdictViewer.exe"
}
finally {
    $stagingDirectory = [IO.Path]::GetFullPath($stagingDirectory)
    if ($stagingDirectory.StartsWith([IO.Path]::GetFullPath($publishDirectory) + '\', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    Pop-Location
}
