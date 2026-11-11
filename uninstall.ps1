# Vivarium uninstall script
# Removes per-user shortcuts, data folder, and the dist package. Does NOT delete personal settings so the user can reinstall and keep their config.

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $root) { $root = Get-Location }
$root = Resolve-Path -Path $root

$exeDir = Join-Path $root "dist\Vivarium"
$exe = Join-Path $exeDir "vivarium.exe"
$lnk = Join-Path $env:USERPROFILE "Desktop\Vivarium.lnk"
$startDir = Join-Path $env:USERPROFILE "AppData\Local\Vivarium"

Write-Host "`nVivarium — portable uninstall`n"

# Remove desktop shortcut
if (Test-Path $lnk) {
    Remove-Item -Path $lnk -Force
    Write-Host "  Removed desktop shortcut: $lnk"
} else {
    Write-Host "  Desktop shortcut not found (already removed or never created): $lnk"
}

# Remove data folder (ask before deleting contents)
if (Test-Path $startDir) {
    Write-Host "  Data folder: $startDir"
    Write-Host "  This will DELETE all settings and cached data inside this folder."
    $confirm = Read-Host "  Are you sure you want to remove it? (y/n)"
    if ($confirm -match '^[Yy]$') {
        Remove-Item -Path $startDir -Recurse -Force
        Write-Host "  Removed data folder: $startDir"
    } else {
        Write-Host "  Kept data folder: $startDir"
    }
} else {
    Write-Host "  Data folder not found: $startDir"
}

# Optionally remove the dist directory entirely
$distRoot = Join-Path $root "dist"
if (Test-Path $distRoot) {
    $confirm = Read-Host "  Also remove the dist package at $distRoot? (y/n)"
    if ($confirm -match '^[Yy]$') {
        Remove-Item -Path $distRoot -Recurse -Force
        Write-Host "  Removed dist package: $distRoot"
    } else {
        Write-Host "  Kept dist package: $distRoot"
    }
}

Write-Host "`nUninstall complete!`n`
The following manual cleanup is optional:
  • Remove the per-user registry entry (if you used Start with Windows):
    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Vivarium /f
`n`