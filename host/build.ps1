<#
  Vivarium build script
  ---------------------
  Compiles the Windows host with the C# compiler that ships with Windows
  (.NET Framework 4.x), so no SDK, Visual Studio or npm is required.

  Usage:
    powershell -ExecutionPolicy Bypass -File host\build.ps1
    powershell -ExecutionPolicy Bypass -File host\build.ps1 -NoDist

  First run downloads the Microsoft.Web.WebView2 NuGet package into host\.deps.
#>
[CmdletBinding()]
param(
    [switch]$NoDist,
    [switch]$Clean,
    [string]$WebView2Version = "1.0.4314.35"
)

$ErrorActionPreference = "Stop"
$hostDir = $PSScriptRoot
$root = Split-Path -Parent $hostDir
$src = Join-Path $hostDir "src"
$bin = Join-Path $hostDir "bin"
$deps = Join-Path $hostDir ".deps"
$sdk = Join-Path $deps "sdk"

function Write-Step($text) {
    Write-Host ""
    Write-Host "  $text" -ForegroundColor Cyan
}

function Find-Compiler {
    $candidates = @(
        (Join-Path $env:windir "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        (Join-Path $env:windir "Microsoft.NET\Framework\v4.0.30319\csc.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Could not find the in-box C# compiler (csc.exe). Vivarium needs .NET Framework 4.x, which ships with Windows."
}

function Ensure-WebView2Sdk {
    $core = Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.Core.dll"
    $winforms = Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.WinForms.dll"
    $loader = Join-Path $sdk "runtimes\win-x64\native\WebView2Loader.dll"
    if ((Test-Path $core) -and (Test-Path $winforms) -and (Test-Path $loader)) { return }

    Write-Step "Downloading Microsoft.Web.WebView2 $WebView2Version"
    New-Item -ItemType Directory -Force -Path $deps | Out-Null
    $nupkg = Join-Path $deps "webview2.zip"
    $url = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$WebView2Version/microsoft.web.webview2.$WebView2Version.nupkg"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    if (Test-Path $sdk) { Remove-Item -Recurse -Force $sdk }
    Expand-Archive -Path $nupkg -DestinationPath $sdk -Force
    Remove-Item $nupkg -Force
    Write-Host "  WebView2 SDK ready." -ForegroundColor DarkGray
}

function Copy-IfExists($from, $to) {
    if (Test-Path $from) { Copy-Item $from $to -Force }
}

Write-Host ""
Write-Host "  VIVARIUM  ·  build" -ForegroundColor Green
Write-Host "  adaptive wallpaper engine for Windows" -ForegroundColor DarkGray

$csc = Find-Compiler
Write-Host "  compiler: $csc" -ForegroundColor DarkGray
Ensure-WebView2Sdk

if ($Clean -and (Test-Path $bin)) {
    Write-Step "Cleaning"
    Remove-Item -Recurse -Force $bin
}
New-Item -ItemType Directory -Force -Path $bin | Out-Null

Write-Step "Compiling host"
$sources = Get-ChildItem -Path $src -Filter "*.cs" | ForEach-Object { $_.FullName }
$coreDll = Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.Core.dll"
$wfDll = Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.WinForms.dll"
$outExe = Join-Path $bin "vivarium.exe"

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/debug:pdbonly",
    "/langversion:5",
    "/win32manifest:$hostDir\app.manifest",
    "/out:$outExe",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Web.Extensions.dll",
    "/r:$coreDll",
    "/r:$wfDll"
) + $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }
if (-not (Test-Path $outExe)) { throw "Compiler produced no executable." }

Write-Step "Copying runtime files"
Copy-Item (Join-Path $sdk "runtimes\win-x64\native\WebView2Loader.dll") (Join-Path $bin "WebView2Loader.dll") -Force
Copy-Item $coreDll (Join-Path $bin "Microsoft.Web.WebView2.Core.dll") -Force
Copy-Item $wfDll (Join-Path $bin "Microsoft.Web.WebView2.WinForms.dll") -Force
Copy-IfExists (Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.Core.xml") (Join-Path $bin "Microsoft.Web.WebView2.Core.xml")

Write-Host ""
Write-Host "  Built: $outExe" -ForegroundColor Green
$size = [Math]::Round((Get-Item $outExe).Length / 1KB, 1)
Write-Host "  Host size: $size KB" -ForegroundColor DarkGray

if ($NoDist) { return }

Write-Step "Assembling portable package"
$distRoot = Join-Path $root "dist"
$package = Join-Path $distRoot "Vivarium"
if (Test-Path $package) { Remove-Item -Recurse -Force $package }
New-Item -ItemType Directory -Force -Path $package | Out-Null
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

Get-ChildItem -Path $bin -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $package $_.Name) -Force }
Copy-Item (Join-Path $root "wallpapers") (Join-Path $package "wallpapers") -Recurse -Force
Copy-Item (Join-Path $root "app") (Join-Path $package "app") -Recurse -Force
Copy-IfExists (Join-Path $root "LICENSE") (Join-Path $package "LICENSE")
Copy-IfExists (Join-Path $root "README.md") (Join-Path $package "README.md")
Copy-IfExists (Join-Path $root "TRACKER.md") (Join-Path $package "TRACKER.md")
Copy-Item (Join-Path $root "docs") (Join-Path $package "docs") -Recurse -Force

$zip = Join-Path $distRoot "Vivarium-1.0.0-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $package "*") -DestinationPath $zip -CompressionLevel Optimal
$zipSize = [Math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host "  Package: $zip ($zipSize MB)" -ForegroundColor Green
