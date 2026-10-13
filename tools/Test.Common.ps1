$script:passedChecks = 0
$script:failedChecks = 0
$script:skippedChecks = 0

function Invoke-TestCommand([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command returned exit code $LASTEXITCODE." }
}

function Invoke-TestCheck([string]$Name, [scriptblock]$Action) {
    Write-Host "RUN $Name"
    try {
        & $Action
        $script:passedChecks++
        Write-Host "PASS $Name"
    }
    catch {
        $script:failedChecks++
        Write-Host "FAIL ${Name}: $_"
    }
}

function Skip-TestCheck([string]$Reason) {
    $script:skippedChecks++
    Write-Host "SKIP $Reason"
}

function Complete-TestRun {
    Write-Host "Passed: $script:passedChecks; Failed: $script:failedChecks; Skipped: $script:skippedChecks"
    if ($script:failedChecks) { throw 'One or more test checks failed.' }
}

function New-TestDirectory {
    $directory = Join-Path ([IO.Path]::GetTempPath()) ('program-tests-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory | Out-Null
    return $directory
}

function Remove-TestDirectory([string]$Directory) {
    if (-not $Directory -or -not (Test-Path -LiteralPath $Directory)) { return }
    $resolved = [IO.Path]::GetFullPath($Directory)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolved) -notlike 'program-tests-*') {
        throw 'Unsafe temporary test cleanup path.'
    }
    $items = @(Get-Item -LiteralPath $resolved) + @(Get-ChildItem -LiteralPath $resolved -Recurse -Force)
    if (@($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Temporary test directory contains a reparse point; cleanup refused.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Test-PowerShellSource([string]$Workspace) {
    foreach ($file in (Get-ChildItem -LiteralPath $Workspace -Recurse -File -Filter '*.ps1' | Where-Object { $_.FullName -notmatch '\\.git\\|\\bin\\|\\obj\\' })) {
        if ([IO.File]::ReadAllText($file.FullName) -match '[^\x00-\x7F]') {
            throw "PowerShell source must contain English ASCII text only: $($file.FullName)"
        }
        $tokens = $null
        $errors = $null
        [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
        if ($errors.Count) { throw "PowerShell parsing failed: $($file.FullName)" }
    }
}