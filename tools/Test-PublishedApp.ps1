param([string]$ExePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
$workspace = Split-Path -Parent $PSScriptRoot
$exeName = 'MdictViewer.exe'
$settingsName = 'dictionaries.json'
$createsSettingsAtStartup = $false
$integrationProject = 'dev-tests/Mdict.Wpf.Tests'
if (-not $ExePath) { $ExePath = Join-Path $workspace "dist\$exeName" }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
$temporary = New-TestDirectory
$script:publishedProcess = $null
Push-Location $workspace
try {
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource $workspace }
    $appDirectory = Join-Path $temporary 'app with spaces'
    $workingDirectory = Join-Path $temporary 'other working directory'
    New-Item -ItemType Directory -Path $appDirectory, $workingDirectory | Out-Null
    $exe = Join-Path $appDirectory 'Renamed App.exe'
    Copy-Item -LiteralPath $ExePath -Destination $exe
    Invoke-TestCheck 'Isolated renamed EXE and configuration location' {
        $script:publishedProcess = Start-Process -FilePath $exe -WorkingDirectory $workingDirectory -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $script:publishedProcess.Refresh()
        } while (-not $script:publishedProcess.HasExited -and $script:publishedProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        if ($script:publishedProcess.HasExited -or $script:publishedProcess.MainWindowHandle -eq 0) { throw 'The application did not create its main window.' }
        $settings = Join-Path $appDirectory $settingsName
        if ($createsSettingsAtStartup) {
            while (-not (Test-Path -LiteralPath $settings) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
            Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json | Out-Null
        }
        elseif (Test-Path -LiteralPath $settings) { throw 'Configuration was unexpectedly created at startup.' }
        if (@(Get-ChildItem -LiteralPath $workingDirectory -File).Count) { throw 'Application files followed the working directory.' }
        $script:publishedProcess.CloseMainWindow() | Out-Null
        if (-not $script:publishedProcess.WaitForExit(5000)) { throw 'The main window did not close cleanly.' }
        $script:publishedProcess.Dispose()
        $script:publishedProcess = $null
    }
    # Use a valid hostfxr with no frameworks, leaving installed runtimes untouched.
    Invoke-TestCheck 'Native installation instructions when .NET is missing' {
        $runtime = Join-Path $temporary 'empty-runtime'
        New-Item -ItemType Directory -Path (Join-Path $runtime 'host') | Out-Null
        $dotnetRoot = Split-Path -Parent (Get-Command dotnet.exe).Source
        Copy-Item -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Destination (Join-Path $runtime 'host\fxr') -Recurse
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardError = $true
        $start.RedirectStandardOutput = $true
        $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $runtime
        $start.EnvironmentVariables['DOTNET_ROOT'] = $runtime
        $start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
        $missing = [Diagnostics.Process]::Start($start)
        try {
            if (-not $missing.WaitForExit(10000)) { $missing.Kill(); throw 'The runtime check timed out.' }
            $message = $missing.StandardError.ReadToEnd()
            if ($missing.ExitCode -eq 0 -or $message -notmatch 'You must install or update .NET') {
                throw 'Expected native .NET installation instructions.'
            }
        }
        finally { $missing.Dispose() }
    }
    if ($integrationProject) {
        Invoke-TestCheck 'Application-specific published integration' {
            Invoke-TestCommand 'dotnet' @('run', '-c', 'Release', '--project', $integrationProject, '--', '--published', $ExePath)
        }
    }
    Complete-TestRun
}
finally {
    if ($script:publishedProcess) {
        if (-not $script:publishedProcess.HasExited) { $script:publishedProcess.Kill(); $script:publishedProcess.WaitForExit() }
        $script:publishedProcess.Dispose()
    }
    Remove-TestDirectory $temporary
    Pop-Location
}
