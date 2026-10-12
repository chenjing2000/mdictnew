$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $workspace 'dist\MdictViewer'

Push-Location $workspace
try {
    dotnet publish .\Mdict.Wpf\Mdict.Wpf.csproj -c Release -o $publishDirectory --nologo
    if ($LASTEXITCODE -ne 0) {
        throw '发布失败，现有输出已保留。请检查构建错误或关闭占用发布文件的程序。'
    }

    foreach ($project in @('Mdict.Core', 'Mdict.Application', 'Mdict.Wpf')) {
        $buildDirectory = [IO.Path]::GetFullPath((Join-Path $workspace "$project\bin"))
        if (-not $buildDirectory.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "构建目录超出项目范围：$buildDirectory"
        }
        if (Test-Path -LiteralPath $buildDirectory) {
            Remove-Item -LiteralPath $buildDirectory -Recurse -Force
        }
    }
    Write-Host "发布完成：$publishDirectory\MdictViewer.exe"
}
finally {
    Pop-Location
}
