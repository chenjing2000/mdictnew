$ErrorActionPreference = 'Stop'
$projectPath = 'Mdict.Wpf\Mdict.Wpf.csproj'
$exeName = 'MdictViewer.exe'
$settingsName = 'dictionaries.json'
$legacyDirectory = 'MdictViewer'
$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$publishDirectory = Join-Path $workspace 'dist'
$stagingDirectory = Join-Path $publishDirectory ('.publish-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $publishDirectory $exeName

Push-Location $workspace
try {
    dotnet publish $projectPath -p:PublishProfile=Portable -o $stagingDirectory --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Publishing failed; the existing EXE and configuration have been preserved.'
    }

    # Debug symbols and XML documentation are not runtime dependencies; retain every DLL for validation.
    Get-ChildItem -LiteralPath $stagingDirectory -File | Where-Object { $_.Extension -in @('.xml', '.pdb') } | Remove-Item -Force
    $output = @(Get-ChildItem -LiteralPath $stagingDirectory -Force)
    if ($output.Count -ne 1 -or $output[0].Name -ne $exeName -or $output[0].PSIsContainer) {
        throw 'Expected a single EXE; the existing release has been preserved.'
    }

    $legacyExe = $null
    $legacySettings = $null
    $settings = Join-Path $publishDirectory $settingsName
    if ($legacyDirectory) {
        $legacyPath = Join-Path $publishDirectory $legacyDirectory
        $legacyExe = Join-Path $legacyPath $exeName
        # Once the old EXE is removed, its saved configuration is only a backup.
        if (Test-Path -LiteralPath $legacyExe) {
            $legacySettings = Join-Path $legacyPath $settingsName
        }
        if ($legacySettings -and (Test-Path -LiteralPath $legacySettings) -and (Test-Path -LiteralPath $settings) -and
            (Get-FileHash -LiteralPath $legacySettings).Hash -ne (Get-FileHash -LiteralPath $settings).Hash) {
            throw 'Old and new configurations differ. Resolve the conflict before publishing; neither file was changed.'
        }
    }

    # Fail before configuration migration if a current or legacy executable is in use.
    foreach ($exe in @($destination, $legacyExe)) {
        if ($exe -and (Test-Path -LiteralPath $exe)) {
            $handle = [IO.File]::Open($exe, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $handle.Dispose()
        }
    }
    if ($legacySettings -and (Test-Path -LiteralPath $legacySettings) -and -not (Test-Path -LiteralPath $settings)) {
        Copy-Item -LiteralPath $legacySettings -Destination $settings
        Write-Host 'Copied legacy configuration to dist; the original is retained as a backup.'
    }

    # Same-volume replacement leaves the previous EXE intact when replacement fails.
    if (Test-Path -LiteralPath $destination) {
        [IO.File]::Replace($output[0].FullName, $destination, [System.Management.Automation.Language.NullString]::Value)
    }
    else {
        [IO.File]::Move($output[0].FullName, $destination)
    }
    if ($legacyExe -and (Test-Path -LiteralPath $legacyExe)) {
        Remove-Item -LiteralPath $legacyExe -Force
    }
    Write-Host "Published: $destination"
}
finally {
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    $expectedRoot = [IO.Path]::GetFullPath($publishDirectory).TrimEnd('\') + '\'
    if (-not $resolvedStaging.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging directory is outside dist; cleanup refused.'
    }
    try {
        if (Test-Path -LiteralPath $resolvedStaging) {
            $items = @(Get-Item -LiteralPath $resolvedStaging) + @(Get-ChildItem -LiteralPath $resolvedStaging -Force -Recurse)
            if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
                throw 'Staging directory contains a reparse point; cleanup refused.'
            }
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
    }
    finally { Pop-Location }
}
