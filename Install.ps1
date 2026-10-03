<#
.SYNOPSIS
    Installs BigPictureTV so it starts hidden every time you sign in.

.DESCRIPTION
    Copies BigPictureTV.ps1 to %LOCALAPPDATA%\BigPictureTV and registers a
    per-user scheduled task that runs it at logon. No admin rights needed.

.PARAMETER TvName
    Part of the TV's monitor name. If omitted, you are asked to pick from the
    connected displays.

.PARAMETER ExtraProcesses
    Passed through to BigPictureTV.ps1 (emulators to keep on the TV).

.PARAMETER Uninstall
    Stop BigPictureTV, remove the scheduled task and the installed files, and
    restore the desktop layout if it was left on the TV.

.EXAMPLE
    .\Install.ps1
.EXAMPLE
    .\Install.ps1 -TvName "LG TV" -ExtraProcesses retroarch
.EXAMPLE
    .\Install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$TvName,
    [string[]]$ExtraProcesses = @(),
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$TaskName   = 'BigPictureTV'
$InstallDir = Join-Path $env:LOCALAPPDATA 'BigPictureTV'
$Script     = Join-Path $InstallDir 'BigPictureTV.ps1'
$Source     = Join-Path $PSScriptRoot 'BigPictureTV.ps1'

function Stop-Watcher {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
        Where-Object { $_.CommandLine -like '*BigPictureTV.ps1*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

if ($Uninstall) {
    Stop-Watcher
    if ((Test-Path $Script) -and (Test-Path (Join-Path $InstallDir 'saved-layout.json'))) { & $Script -Restore }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
    Write-Host 'BigPictureTV removed.'
    return
}

if (-not (Test-Path $Source)) { throw "BigPictureTV.ps1 not found next to Install.ps1 ($Source)." }

if (-not $TvName) {
    Write-Host ''
    Write-Host 'Connected displays:'
    & $Source -ListDisplays
    $TvName = Read-Host 'Type the TV name (or part of it) from the list above'
    if (-not $TvName) { throw 'No TV name given.' }
}

Stop-Watcher
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Force $Source $Script

$argList = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Script`" -TvName `"$TvName`""
if ($ExtraProcesses.Count -gt 0) {
    $argList += " -ExtraProcesses `"$($ExtraProcesses -join ',')`""
}

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argList
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host ''
Write-Host "Installed. BigPictureTV is running and will start at every sign-in (TV: '$TvName')."
Write-Host "Log: $InstallDir\BigPictureTV.log"
