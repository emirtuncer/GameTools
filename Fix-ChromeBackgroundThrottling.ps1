<#
.SYNOPSIS
    Stops Chrome from throttling/pausing background tabs (e.g. videos) while a game
    is in the foreground, by adding anti-throttling launch flags to your Chrome shortcuts.

.DESCRIPTION
    Chrome can only pick up these flags when it STARTS. This script rewrites the
    Chrome shortcuts (Desktop, Start Menu, Taskbar pin) so every launch includes them.
    After running, fully close Chrome and start it again from a patched shortcut.

    The flags added:
      --disable-background-timer-throttling      keep JS timers running in background tabs
      --disable-backgrounding-occluded-windows   don't slow tabs that are covered by other windows
      --disable-renderer-backgrounding           don't deprioritize background renderers
      --disable-features=CalculateNativeWinOcclusion   THE big one: stop pausing render when a
                                                       fullscreen game covers the window

.PARAMETER Undo
    Removes the flags from all Chrome shortcuts (restores default behavior).

.PARAMETER Relaunch
    Closes Chrome and reopens it after patching, so the fix takes effect immediately.

.EXAMPLE
    .\Fix-ChromeBackgroundThrottling.ps1
    Patches all Chrome shortcuts.

.EXAMPLE
    .\Fix-ChromeBackgroundThrottling.ps1 -Relaunch
    Patches shortcuts, then closes and reopens Chrome so it works right now.

.EXAMPLE
    .\Fix-ChromeBackgroundThrottling.ps1 -Undo
    Removes the flags from all Chrome shortcuts.
#>

[CmdletBinding()]
param(
    [switch]$Undo,
    [switch]$Relaunch
)

$ErrorActionPreference = 'Stop'

# --- The anti-throttling flags ----------------------------------------------
$Flags = @(
    '--disable-background-timer-throttling'
    '--disable-backgrounding-occluded-windows'
    '--disable-renderer-backgrounding'
    '--disable-features=CalculateNativeWinOcclusion'
)

# --- Where Chrome shortcuts typically live ----------------------------------
$searchRoots = @(
    [Environment]::GetFolderPath('Desktop')
    (Join-Path $env:PUBLIC 'Desktop')
    [Environment]::GetFolderPath('StartMenu')
    [Environment]::GetFolderPath('CommonStartMenu')
    (Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

# --- Find every .lnk that points at chrome.exe ------------------------------
$shell = New-Object -ComObject WScript.Shell
$shortcuts = @()
foreach ($root in $searchRoots) {
    Get-ChildItem -Path $root -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $lnk = $shell.CreateShortcut($_.FullName)
            if ($lnk.TargetPath -and ($lnk.TargetPath.ToLower().EndsWith('chrome.exe'))) {
                $shortcuts += [pscustomobject]@{ Path = $_.FullName; Link = $lnk }
            }
        } catch { }
    }
}

if ($shortcuts.Count -eq 0) {
    Write-Warning "No Chrome shortcuts found. Make sure Chrome is installed and you have a Chrome shortcut on the Desktop, Start Menu, or Taskbar."
    Write-Host  "You can also create one: right-click chrome.exe -> Send to -> Desktop, then re-run this script."
    return
}

# --- Patch or unpatch each shortcut -----------------------------------------
$changed = 0
foreach ($s in $shortcuts) {
    $lnkArgs = $s.Link.Arguments

    # strip any of our flags that are already present (so we never duplicate)
    foreach ($f in $Flags) {
        $lnkArgs = ($lnkArgs -replace [regex]::Escape($f), '').Trim()
    }
    $lnkArgs = ($lnkArgs -replace '\s{2,}', ' ').Trim()

    if (-not $Undo) {
        # append our flags
        $lnkArgs = (@($lnkArgs) + $Flags | Where-Object { $_ }) -join ' '
    }

    if ($s.Link.Arguments -ne $lnkArgs) {
        try {
            $s.Link.Arguments = $lnkArgs
            $s.Link.Save()
            $changed++
            $action = if ($Undo) { 'Cleaned ' } else { 'Patched ' }
            Write-Host ("  {0}{1}" -f $action, $s.Path)
        } catch {
            # All-users shortcuts under C:\ProgramData need admin. Skip, don't abort.
            Write-Warning ("Skipped (needs admin): {0}" -f $s.Path)
        }
    }
}

[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null

if ($Undo) {
    Write-Host "`nRemoved anti-throttling flags from $changed shortcut(s)." -ForegroundColor Yellow
} else {
    Write-Host "`nPatched $($shortcuts.Count) Chrome shortcut(s) ($changed updated)." -ForegroundColor Green
    Write-Host "Close Chrome completely and reopen it from a patched shortcut for the fix to take effect."
}

# --- Optionally relaunch Chrome so it works immediately ---------------------
if ($Relaunch -and -not $Undo) {
    $chrome = $shortcuts[0].Link.TargetPath
    Write-Host "`nRelaunching Chrome..."
    Get-Process -Name 'chrome' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process -FilePath $chrome -ArgumentList ($Flags + '--restore-last-session')
    Write-Host "Chrome restarted with anti-throttling flags." -ForegroundColor Green
}
