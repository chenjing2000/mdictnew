$ErrorActionPreference = 'Stop'
$testScript = Join-Path $PSScriptRoot 'Test.ps1'
if (-not (Test-Path -LiteralPath $testScript)) { throw 'The unified Test.ps1 entry point is missing.' }
$global:workflowForceFailure = $false
function global:dotnet { $global:LASTEXITCODE = if ($global:workflowForceFailure) { 1 } else { 0 } }
function global:node { $global:LASTEXITCODE = 0 }
function global:powershell.exe { $global:LASTEXITCODE = 0 }
try {
    $output = & $testScript -Suite Core *>&1 | Out-String
    if ($output -notmatch 'Failed: 0') { throw 'Core checks did not report success.' }
    Write-Host 'PASS successful commands produce a passing summary'
    $global:workflowForceFailure = $true
    $rejected = $false
    try { & $testScript -Suite Core *> $null } catch { $rejected = $true }
    if (-not $rejected) { throw 'A failed native test was accepted.' }
    Write-Host 'PASS failed native commands reject the test run'
    $global:workflowForceFailure = $false
    $output = & $testScript -Suite Full *>&1 | Out-String
    if ($output -notmatch 'Skipped: [1-9]') { throw 'Missing external media was not reported as skipped.' }
    Write-Host 'PASS unavailable optional integration data is explicitly skipped'
}
finally {
    Remove-Item Function:\dotnet, Function:\node, Function:\powershell.exe
    Remove-Variable workflowForceFailure -Scope Global
}
