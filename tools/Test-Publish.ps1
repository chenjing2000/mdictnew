$ErrorActionPreference = 'Stop'
$exeName = 'MdictViewer.exe'
$settingsName = 'dictionaries.json'
$legacyDirectory = 'MdictViewer'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('publish-tests-' + [Guid]::NewGuid().ToString('N'))
$scriptPath = Join-Path $PSScriptRoot 'Publish.ps1'
$script:passed = 0
$script:failed = 0

# Replace only the expensive build process; exercise the real publishing script and filesystem.
function global:dotnet {
    $outputIndex = [Array]::IndexOf($args, '-o')
    if ($outputIndex -lt 0) { throw 'Publish must supply a staging output directory.' }
    $output = $args[$outputIndex + 1]
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $output $exeName), 'new-release')
    if ($global:publishTestBuildMode -eq 'extra') {
        [IO.File]::WriteAllText((Join-Path $output 'unexpected.dll'), 'dependency')
    }
    if ($global:publishTestBuildMode -eq 'symbols') {
        [IO.File]::WriteAllText((Join-Path $output 'ReferencedProject.pdb'), 'debug-symbols')
        [IO.File]::WriteAllText((Join-Path $output 'Package.xml'), 'documentation')
    }
    $global:LASTEXITCODE = if ($global:publishTestBuildMode -eq 'failure') { 1 } else { 0 }
}

function Check($condition, $message) {
    if (-not $condition) { throw $message }
}

function Run-Case($name, [scriptblock]$body) {
    $caseRoot = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
    $tools = Join-Path $caseRoot 'tools'
    $dist = Join-Path $caseRoot 'dist'
    New-Item -ItemType Directory -Path $tools, $dist -Force | Out-Null
    Copy-Item -LiteralPath $scriptPath -Destination (Join-Path $tools 'Publish.ps1')
    $release = Join-Path $dist $exeName
    $settings = Join-Path $dist $settingsName
    [IO.File]::WriteAllText($release, 'old-release')
    [IO.File]::WriteAllText($settings, 'user-settings')
    [IO.File]::WriteAllText((Join-Path $dist 'user-notes.txt'), 'keep-me')
    $global:publishTestBuildMode = 'success'
    try {
        & $body
        Check ((Get-Content -LiteralPath (Join-Path $dist 'user-notes.txt') -Raw) -eq 'keep-me') 'User files changed.'
        Check (@(Get-ChildItem -LiteralPath $dist -Directory -Filter '.publish-*').Count -eq 0) 'Staging directory leaked.'
        $script:passed++
        Write-Host "PASS $name"
    }
    catch {
        $script:failed++
        Write-Host "FAIL ${name}: $_"
    }
}

try {
    Run-Case 'successful update preserves user configuration' {
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'new-release') 'Release was not updated in dist.'
        Check ((Get-Content -LiteralPath $settings -Raw) -eq 'user-settings') 'Settings changed.'
    }
    Run-Case 'reference symbols and XML documentation are excluded from distribution' {
        $global:publishTestBuildMode = 'symbols'
        & (Join-Path $tools 'Publish.ps1')
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'new-release') 'Release with build symbols was rejected.'
        Check (-not (Test-Path -LiteralPath (Join-Path $dist 'ReferencedProject.pdb'))) 'Symbols were distributed.'
    }
    Run-Case 'failed build preserves the existing EXE and settings' {
        $global:publishTestBuildMode = 'failure'
        $rejected = $false
        try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
        Check $rejected 'Failed build was accepted.'
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Failed build replaced release.'
        Check ((Get-Content -LiteralPath $settings -Raw) -eq 'user-settings') 'Failed build changed settings.'
    }
    Run-Case 'unexpected dependency rejects the package without replacing the EXE' {
        $global:publishTestBuildMode = 'extra'
        $rejected = $false
        try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
        Check $rejected 'Multi-file output was accepted.'
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Invalid package replaced release.'
    }
    Run-Case 'locked release is preserved' {
        $lock = [IO.File]::Open($release, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try {
            $rejected = $false
            try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
            Check $rejected 'Locked release was accepted.'
        }
        finally { $lock.Dispose() }
        Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Locked release changed.'
    }
    if ($legacyDirectory) {
        Run-Case 'legacy configuration is copied to the new location and retained as a backup' {
            Remove-Item -LiteralPath $settings
            $legacy = Join-Path $dist $legacyDirectory
            New-Item -ItemType Directory -Path $legacy | Out-Null
            $legacySettings = Join-Path $legacy $settingsName
            [IO.File]::WriteAllText($legacySettings, 'legacy-settings')
            [IO.File]::WriteAllText((Join-Path $legacy $exeName), 'legacy-release')
            & (Join-Path $tools 'Publish.ps1')
            Check ((Get-Content -LiteralPath $settings -Raw) -eq 'legacy-settings') 'Legacy settings were not migrated.'
            Check ((Get-Content -LiteralPath $legacySettings -Raw) -eq 'legacy-settings') 'Legacy backup changed.'
            Check (-not (Test-Path -LiteralPath (Join-Path $legacy $exeName))) 'Obsolete EXE remains.'
            [IO.File]::WriteAllText($settings, 'updated-settings')
            & (Join-Path $tools 'Publish.ps1')
            Check ((Get-Content -LiteralPath $settings -Raw) -eq 'updated-settings') 'Later update restored a stale backup.'
        }
        Run-Case 'conflicting old and new configurations stop the update' {
            $legacy = Join-Path $dist $legacyDirectory
            New-Item -ItemType Directory -Path $legacy | Out-Null
            [IO.File]::WriteAllText((Join-Path $legacy $settingsName), 'different-settings')
            [IO.File]::WriteAllText((Join-Path $legacy $exeName), 'legacy-release')
            $rejected = $false
            try { & (Join-Path $tools 'Publish.ps1') } catch { $rejected = $true }
            Check $rejected 'Conflicting configurations were accepted.'
            Check ((Get-Content -LiteralPath $release -Raw) -eq 'old-release') 'Conflict replaced release.'
            Check ((Get-Content -LiteralPath $settings -Raw) -eq 'user-settings') 'Conflict overwrote settings.'
        }
    }
}
finally {
    Remove-Item Function:\dotnet
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
Write-Host "Passed: $script:passed; Failed: $script:failed"
if ($script:failed) { exit 1 }
