# Vivarium installation script
# Portable per-user install — no admin rights required, just places files and creates shortcuts.
# Run as: powershell -ExecutionPolicy Bypass -File .\install.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $root) { $root = Get-Location }
$root = Resolve-Path -Path $root

$exeDir = Join-Path $root "dist\Vivarium"
$exe = Join-Path $exeDir "vivarium.exe"
$lnk = Join-Path $env:USERPROFILE "Desktop\Vivarium.lnk"
$startDir = Join-Path $env:USERPROFILE "AppData\Local\Vivarium"
$logDir = Join-Path $startDir
$cfgPath = Join-Path $startDir "settings.json"

Write-Host "`nVivarium — portable install`n"

# Create data folder
if (-not (Test-Path $startDir)) {
    New-Item -ItemType Directory -Path $startDir | Out-Null
    Write-Host "  Created data folder: $startDir"
} else {
    Write-Host "  Data folder already exists: $startDir"
}

# Copy host + wallpapers + docs (overwrite existing)
Write-Host "  Copying host binaries..."
Copy-Item -Path (Join-Path $exeDir "vivarium.exe") -Destination $exe -Force

# The ZIP already contains everything, but if running from source:
if (Test-Path (Join-Path $root "docs")) {
    Copy-Item -Path (Join-Path $root "docs") -Destination (Join-Path $root "dist\Vivarium\docs") -Recurse -Force
}
Copy-Item -Path (Join-Path $root "LICENSE") -Destination (Join-Path $exeDir "LICENSE") -Force

# Create a minimal settings.json if it doesn't exist
if (-not (Test-Path $cfgPath)) {
    $sample = @{ wallpaper = "moss-vale"; system = { startup = false; pauseFullscreen = true; fps = 30; monitors = "all"; reducedMotion = false }; video = $null; image = $null } | ConvertTo-Json -Depth 3
    $sample | Out-File -FilePath $cfgPath -Encoding UTF8
    Write-Host "  Created default settings: $cfgPath"
} else {
    Write-Host "  Using existing settings: $cfgPath"
}

# Create Desktop shortcut (per-user)
if (-not (Test-Path $lnk)) {
    $wsh = New-Object -ComObject Wscript.Shell
    $shortcut = $wsh.CreateShortcut($lnk)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $exeDir
    $shortcut.Arguments = "--autostart"
    $shortcut.Save()
    Write-Host "  Created desktop shortcut: $lnk"
} else {
    Write-Host "  Desktop shortcut already exists: $lnk"
}

Write-Host "`nInstall complete!`n"
Write-Host "  Executable: $exe"
Write-Host "  Data folder: $startDir"
Write-Host "  Desktop shortcut: $lnk"
Write-Host "`nTo uninstall, run: powershell -ExecutionPolicy Bypass -File .\uninstall.ps1`n"