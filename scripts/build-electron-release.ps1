[CmdletBinding()]
param([string]$DotNetPath="dotnet", [string]$InnoCompiler)
$ErrorActionPreference="Stop"
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifacts=Join-Path $repo "artifacts"
[xml]$project=Get-Content (Join-Path $repo "Series4.Desktop.csproj")
$version=[string]$project.Project.PropertyGroup.Version
if (!$InnoCompiler) {$InnoCompiler=Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"}
if (!(Test-Path -LiteralPath $InnoCompiler)) {throw "Pass -InnoCompiler with the Inno Setup 6 compiler path."}
Push-Location (Join-Path $repo "studio")
try {
    & npm.cmd ci
    if ($LASTEXITCODE -ne 0) {throw "npm ci failed"}
    # Some enterprise npm configurations disable lifecycle scripts.
    if (!(Test-Path -LiteralPath "node_modules\electron\dist\electron.exe")) {
        & node node_modules/electron/install.js
        if ($LASTEXITCODE -ne 0) {throw "Electron runtime download failed"}
    }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) {throw "Frontend build failed"}
    $pkg=Get-Content package.json -Raw | ConvertFrom-Json
    if ($pkg.version -ne $version) {throw "Frontend/native versions differ"}
} finally {Pop-Location}
& (Join-Path $PSScriptRoot "build-release.ps1") -DotNetPath $DotNetPath -InnoCompiler $InnoCompiler -EngineOnly
$package=Join-Path $artifacts ("electron-"+[guid]::NewGuid().ToString("N")+"\GonggongAX-Series4-Portable-x64-v"+$version)
& node (Join-Path $repo "studio\package-electron.mjs") $package
if ($LASTEXITCODE -ne 0) {throw "Electron packaging failed"}
$release=Join-Path $artifacts "release"
$zip=Join-Path $release "GonggongAX-Series4-Portable-x64-v$version.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
& $InnoCompiler "/Qp" "/DAppVersion=$version" "/DSourceDir=$package" "/DOutputDir=$release" (Join-Path $repo "installer\Series4.iss")
if ($LASTEXITCODE -ne 0) {throw "Installer build failed"}
$comic=Join-Path $release "GonggongAX-Series4-Intro-v$version.zip"
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $repo "docs\assets\series4-comic\intro-20260912"),$comic)
$hashes=Get-ChildItem -LiteralPath $release -File | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+"  "+$_.Name
}
$hashes | Set-Content (Join-Path $release "SHA256SUMS.txt") -Encoding ascii
Write-Host "PACKAGE_DIR=$package"
Get-ChildItem -LiteralPath $release -File | Select-Object Name,Length
