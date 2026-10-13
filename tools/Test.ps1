param(
    [ValidateSet('Core', 'UI', 'Full')][string]$Suite = 'Core',
    [string]$DictionaryPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource (Split-Path -Parent $PSScriptRoot) }
    if ($DictionaryPath -and -not (Test-Path -LiteralPath $DictionaryPath -PathType Leaf)) {
        throw 'The supplied dictionary file does not exist.'
    }
    Invoke-TestCheck 'Publishing safeguards' {
        Invoke-TestCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/Test-Publish.ps1')
    }
    Invoke-TestCheck 'Core reader' { Invoke-TestCommand 'dotnet' @('run', '--project', 'MdictReaderTest', '-c', 'Release', '--', '--self-test') }
    Invoke-TestCheck 'Entry bridge' { Invoke-TestCommand 'node' @('dev-tests/entry_bridge_test.cjs') }
    Invoke-TestCheck 'Layout bridge' { Invoke-TestCommand 'node' @('dev-tests/layout_bridge_test.cjs') }
    Invoke-TestCheck 'Local resources' { Invoke-TestCommand 'dotnet' @('run', '--project', 'dev-tests/Mdict.Wpf.Tests', '-c', 'Release', '--', '--local-resources') }
    if ($Suite -ne 'Core') {
        foreach ($category in @('--settings', '--notifications')) {
            Invoke-TestCheck $category { Invoke-TestCommand 'dotnet' @('run', '--project', 'dev-tests/Mdict.Wpf.Tests', '-c', 'Release', '--', $category) }
        }
    }
    if ($Suite -eq 'Full') {
        if ($DictionaryPath) {
            Invoke-TestCheck 'Oxford V14.3 integration' { Invoke-TestCommand 'dotnet' @('run', '--project', 'dev-tests/Mdict.Wpf.Tests', '-c', 'Release', '--', $DictionaryPath) }
        }
        else { Skip-TestCheck 'Real Oxford V14.3 integration: supply -DictionaryPath to enable it.' }
    }
    Complete-TestRun
}
finally { Pop-Location }
