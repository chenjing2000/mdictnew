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
        throw 'Publishing failed; existing output has been preserved. Check the build errors or close any application using the published files.'
    }

    # XML documentation is not needed at runtime; never drop a DLL to fake a single-file package.
    Get-ChildItem -LiteralPath $stagingDirectory -Filter '*.xml' -File | Remove-Item -Force
    $output = @(Get-ChildItem -LiteralPath $stagingDirectory -Force)
    if ($output.Count -ne 1 -or $output[0].Name -ne 'MdictViewer.exe') {
        throw 'The output is not a single EXE; the existing release and configuration have been preserved.'
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
            throw "Build directory is outside the workspace: $buildDirectory"
        }
        if (Test-Path -LiteralPath $buildDirectory) {
            $items = @(Get-Item -LiteralPath $buildDirectory) + @(Get-ChildItem -LiteralPath $buildDirectory -Force -Recurse)
            if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
                throw "Cleanup directory contains a reparse point and has been preserved: $buildDirectory"
            }
            Remove-Item -LiteralPath $buildDirectory -Recurse -Force
        }
    }
    Write-Host "Published: $publishDirectory\MdictViewer.exe"
}
finally {
    $stagingDirectory = [IO.Path]::GetFullPath($stagingDirectory)
    if ($stagingDirectory.StartsWith([IO.Path]::GetFullPath($publishDirectory) + '\', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    Pop-Location
}
