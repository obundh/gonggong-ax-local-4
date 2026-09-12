[CmdletBinding()]
param([string]$Version="4.2.0")
$ErrorActionPreference="Stop"
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$release=Join-Path $repo "artifacts\release"
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ("gonggong-series4-package-test-"+[guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $testRoot | Out-Null
foreach($line in Get-Content (Join-Path $release "SHA256SUMS.txt")){
 if($line -notmatch '^([0-9a-f]{64})  (.+)$'){throw "Malformed checksum"}
 if((Get-FileHash -LiteralPath (Join-Path $release $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]){throw "Checksum mismatch"}
}
function Assert-Package([string]$Root,[string]$Label){
 $exe=Join-Path $Root "공공AX-업무매크로.exe"
 foreach($required in @("resources\engine\coreclr.dll","resources\engine\공공AX-업무매크로.exe","resources\app\dist\index.html","LICENSES.chromium.html","소개 만화\02-repeat-wait.png")){
  if(!(Test-Path -LiteralPath (Join-Path $Root $required))){throw "Missing packaged file: $required"}
 }
 if(!(Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.StartsWith($Version)){throw "Incorrect exe metadata"}
 $out=Join-Path $testRoot "$Label.stdout.log"
 $err=Join-Path $testRoot "$Label.stderr.log"
 $process=Start-Process -FilePath $exe -ArgumentList "--smoke-test" -WorkingDirectory $Root -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
 if(!$process.WaitForExit(45000)){
  Stop-Process -Id $process.Id -Force
  throw "Package diagnostic timed out"
 }
 $process.Refresh()
 $receipt=Get-Content -LiteralPath $out -Raw
 if($process.ExitCode -ne 0 -or $receipt -notmatch 'AX_SMOKE (.+)'){throw "Package diagnostic failed: $Label. Inspect logs in $testRoot"}
 $result=$Matches[1] | ConvertFrom-Json
 if(!$result.ok -or !$result.packaged -or $result.version -ne $Version){throw "Invalid package receipt"}
 Write-Output "$Label $receipt"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$unzip=Join-Path $testRoot "포터블"
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $release "GonggongAX-Series4-Portable-x64-v$Version.zip"),$unzip)
$portable=Join-Path $unzip "GonggongAX-Series4-Portable-x64-v$Version"
Assert-Package $portable "portable"
$install=Join-Path $testRoot "설치 경로"
$setup=Join-Path $release "GonggongAX-Series4-Setup-x64-v$Version.exe"
$process=Start-Process -FilePath $setup -ArgumentList @("/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/CURRENTUSER","/NOICONS","/AXSMOKETEST=1","/DIR=`"$install`"") -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw "Setup failed: $($process.ExitCode)"}
Assert-Package $install "installed"
# Uninstall only the exact isolated test directory, never an existing user installation.
$resolved=[IO.Path]::GetFullPath($install)
if(!$resolved.StartsWith($testRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe uninstall target"}
$uninstaller=Join-Path $resolved "unins000.exe"
$process=Start-Process -FilePath $uninstaller -ArgumentList @("/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART") -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw "Uninstall failed"}
if(Test-Path -LiteralPath (Join-Path $resolved "공공AX-업무매크로.exe")){throw "Uninstall left the executable"}
Write-Output "PASS: portable, setup, bundled engine/UI, isolated uninstall. Evidence: $testRoot"
