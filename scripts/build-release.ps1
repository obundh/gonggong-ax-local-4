[CmdletBinding()]
param(
    [string]$Version,
    [string]$DotNetPath = "dotnet",
    [string]$InnoCompiler,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($null -eq ("System.IO.Compression.ZipFile" -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$publishDir = Join-Path $artifactsRoot "publish\win-x64"
$stagingDir = Join-Path $artifactsRoot "staging"
$releaseDir = Join-Path $artifactsRoot "release"
$sourceWorkDir = Join-Path $artifactsRoot "source\SharpHook-7.1.3"
$sourceArchive = Join-Path $releaseDir "SharpHook-7.1.3-Corresponding-Source.zip"
$projectPath = Join-Path $repoRoot "Series4.Desktop.csproj"
$installerScript = Join-Path $repoRoot "installer\Series4.iss"

function Remove-BuildDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $expectedPrefix = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build output path is outside artifacts: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Get-PeMachine([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        return $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Resolve-DotNetExecutable([string]$RequestedPath) {
    if (Test-Path -LiteralPath $RequestedPath -PathType Leaf) {
        return [IO.Path]::GetFullPath($RequestedPath)
    }
    $command = Get-Command $RequestedPath -ErrorAction Stop
    return $command.Source
}

function Resolve-InnoCompiler([string]$RequestedPath) {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidates += $command.Source
    }
    $candidates += @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    throw "ISCC.exe was not found. Install Inno Setup 6 or pass -InnoCompiler."
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Encoding UTF8
$projectVersion = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}
if ($Version -ne $projectVersion) {
    throw "Requested version $Version does not match project version $projectVersion."
}

$dotnetExe = Resolve-DotNetExecutable $DotNetPath
$isccExe = Resolve-InnoCompiler $InnoCompiler

Remove-BuildDirectory $publishDir
Remove-BuildDirectory $stagingDir
Remove-BuildDirectory $releaseDir
Remove-BuildDirectory $sourceWorkDir
New-Item -ItemType Directory -Path $publishDir, $stagingDir, $releaseDir, (Split-Path -Parent $sourceWorkDir) -Force | Out-Null

$publishArguments = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:Platform=x64",
    "-p:Version=$Version",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-p:ContinuousIntegrationBuild=true",
    "-o", $publishDir
)
if ($NoRestore) {
    $publishArguments += "--no-restore"
}
Invoke-Checked $dotnetExe $publishArguments

$runtimeConfigPath = Get-ChildItem -LiteralPath $publishDir -Filter "*.runtimeconfig.json" -File | Select-Object -First 1
if ($null -eq $runtimeConfigPath) {
    throw "A self-contained runtimeconfig.json was not found."
}
$appAssemblyBaseName = $runtimeConfigPath.Name.Substring(
    0,
    $runtimeConfigPath.Name.Length - ".runtimeconfig.json".Length
)
$appExeName = "$appAssemblyBaseName.exe"
$appDllName = "$appAssemblyBaseName.dll"
$appDepsName = "$appAssemblyBaseName.deps.json"
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
$netCoreVersion = [string](
    $includedFrameworks |
        Where-Object name -eq "Microsoft.NETCore.App" |
        Select-Object -First 1 -ExpandProperty version
)
$windowsDesktopVersion = [string](
    $includedFrameworks |
        Where-Object name -eq "Microsoft.WindowsDesktop.App" |
        Select-Object -First 1 -ExpandProperty version
)
if ([string]::IsNullOrWhiteSpace($netCoreVersion) -or [string]::IsNullOrWhiteSpace($windowsDesktopVersion)) {
    throw "The published .NET/Windows Desktop Runtime versions could not be determined."
}

$globalPackagesOutput = & $dotnetExe nuget locals global-packages --list
if ($LASTEXITCODE -ne 0) {
    throw "The NuGet global-packages path could not be determined."
}
$globalPackagesLine = $globalPackagesOutput | Where-Object { $_ -match '^global-packages:\s*' } | Select-Object -First 1
if ($null -eq $globalPackagesLine) {
    throw "Unexpected NuGet global-packages output: $globalPackagesOutput"
}
$globalPackagesRoot = [IO.Path]::GetFullPath(($globalPackagesLine -replace '^global-packages:\s*', ''))
$netCoreRuntimePack = Join-Path $globalPackagesRoot "microsoft.netcore.app.runtime.win-x64\$netCoreVersion"
$windowsDesktopRuntimePack = Join-Path $globalPackagesRoot "microsoft.windowsdesktop.app.runtime.win-x64\$windowsDesktopVersion"
$runtimeNoticeFiles = @(
    (Join-Path $netCoreRuntimePack "LICENSE.TXT"),
    (Join-Path $netCoreRuntimePack "THIRD-PARTY-NOTICES.TXT"),
    (Join-Path $windowsDesktopRuntimePack "LICENSE")
)
foreach ($requiredFile in $runtimeNoticeFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw ".NET Runtime notice file was not found: $requiredFile"
    }
}

Get-ChildItem -LiteralPath $publishDir -File |
    Where-Object { $_.Extension -in @(".pdb", ".xml") } |
    Remove-Item -Force

$sharpHookCommit = "bcb41a4b4a1901ef6c3d4de129e273c8bdafbbd4"
$libuiohookCommit = "104624bfd3c69e558e56fd8aff11ea61bc24b224"
Invoke-Checked "git" @(
    "clone",
    "--branch", "v7.1.3",
    "--depth", "1",
    "--recurse-submodules",
    "--shallow-submodules",
    "https://github.com/TolikPylypchuk/SharpHook.git",
    $sourceWorkDir
)
$actualSharpHookCommit = (& git -C $sourceWorkDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSharpHookCommit -ne $sharpHookCommit) {
    throw "Unexpected SharpHook source commit: $actualSharpHookCommit"
}
$libuiohookDir = Join-Path $sourceWorkDir "libuiohook"
$actualLibuiohookCommit = (& git -C $libuiohookDir rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualLibuiohookCommit -ne $libuiohookCommit) {
    throw "Unexpected libuiohook source commit: $actualLibuiohookCommit"
}

$revisionText = @"
SharpHook 7.1.3
Repository: https://github.com/TolikPylypchuk/SharpHook
Commit: $sharpHookCommit

Bundled libuiohook submodule
Repository: https://github.com/TolikPylypchuk/libuiohook
Commit: $libuiohookCommit
License: LGPL-3.0-or-later
"@
Set-Content -LiteralPath (Join-Path $sourceWorkDir "SOURCE_REVISIONS.txt") -Value $revisionText -Encoding UTF8
Remove-Item -LiteralPath (Join-Path $sourceWorkDir ".git") -Recurse -Force
$submoduleGitFile = Join-Path $libuiohookDir ".git"
if (Test-Path -LiteralPath $submoduleGitFile) {
    Remove-Item -LiteralPath $submoduleGitFile -Force
}
[IO.Compression.ZipFile]::CreateFromDirectory(
    $sourceWorkDir,
    $sourceArchive,
    [IO.Compression.CompressionLevel]::Optimal,
    $true
)

Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $publishDir "LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $repoRoot "OPEN_SOURCE_COMPONENTS.md") -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\BEGINNER_GUIDE.md") -Destination (Join-Path $publishDir "BEGINNER_GUIDE.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\README-FIRST.txt") -Destination (Join-Path $publishDir "README-FIRST.txt")
Copy-Item -LiteralPath (Join-Path $repoRoot "licenses") -Destination (Join-Path $publishDir "licenses") -Recurse
$dotnetNoticeDir = Join-Path $publishDir "licenses\dotnet"
New-Item -ItemType Directory -Path $dotnetNoticeDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $netCoreRuntimePack "LICENSE.TXT") -Destination (Join-Path $dotnetNoticeDir "Microsoft.NETCore.App-$netCoreVersion-LICENSE.txt")
Copy-Item -LiteralPath (Join-Path $netCoreRuntimePack "THIRD-PARTY-NOTICES.TXT") -Destination (Join-Path $dotnetNoticeDir "Microsoft.NETCore.App-$netCoreVersion-THIRD-PARTY-NOTICES.txt")
Copy-Item -LiteralPath (Join-Path $windowsDesktopRuntimePack "LICENSE") -Destination (Join-Path $dotnetNoticeDir "Microsoft.WindowsDesktop.App-$windowsDesktopVersion-LICENSE.txt")
$correspondingSourceDir = Join-Path $publishDir "licenses\corresponding-source"
New-Item -ItemType Directory -Path $correspondingSourceDir -Force | Out-Null
Copy-Item -LiteralPath $sourceArchive -Destination $correspondingSourceDir

$sbom = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "GonggongAX-Series4-$Version-win-x64"
    documentNamespace = "https://github.com/obundh/gonggong-ax-local-4/spdx/v$Version/win-x64"
    creationInfo = [ordered]@{
        created = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        creators = @("Tool: scripts/build-release.ps1")
    }
    packages = @(
        [ordered]@{
            name = "GonggongAX-Series4"
            SPDXID = "SPDXRef-Package-GonggongAX-Series4"
            versionInfo = $Version
            downloadLocation = "https://github.com/obundh/gonggong-ax-local-4"
            filesAnalyzed = $false
            licenseConcluded = "MIT"
            licenseDeclared = "MIT"
            copyrightText = "NOASSERTION"
        },
        [ordered]@{
            name = "ScreenRecorderLib"
            SPDXID = "SPDXRef-Package-ScreenRecorderLib"
            versionInfo = "6.6.0"
            downloadLocation = "https://github.com/sskodje/ScreenRecorderLib/tree/v6.6.0"
            filesAnalyzed = $false
            licenseConcluded = "MIT"
            licenseDeclared = "MIT"
            copyrightText = "Copyright Sverre Kristoffer Skodje"
        },
        [ordered]@{
            name = "SharpHook"
            SPDXID = "SPDXRef-Package-SharpHook"
            versionInfo = "7.1.3"
            downloadLocation = "https://github.com/TolikPylypchuk/SharpHook/tree/$sharpHookCommit"
            filesAnalyzed = $false
            licenseConcluded = "MIT"
            licenseDeclared = "MIT"
            copyrightText = "Copyright 2021 Anatoliy Pylypchuk"
        },
        [ordered]@{
            name = "libuiohook"
            SPDXID = "SPDXRef-Package-libuiohook"
            versionInfo = $libuiohookCommit
            downloadLocation = "https://github.com/TolikPylypchuk/libuiohook/tree/$libuiohookCommit"
            filesAnalyzed = $false
            licenseConcluded = "LGPL-3.0-or-later"
            licenseDeclared = "LGPL-3.0-or-later"
            copyrightText = "NOASSERTION"
        },
        [ordered]@{
            name = "Microsoft.NETCore.App.Runtime.win-x64"
            SPDXID = "SPDXRef-Package-Microsoft-NETCore-App"
            versionInfo = $netCoreVersion
            downloadLocation = "https://github.com/dotnet/dotnet"
            filesAnalyzed = $false
            licenseConcluded = "MIT"
            licenseDeclared = "MIT"
            copyrightText = "Copyright Microsoft Corporation"
        },
        [ordered]@{
            name = "Microsoft.WindowsDesktop.App.Runtime.win-x64"
            SPDXID = "SPDXRef-Package-Microsoft-WindowsDesktop-App"
            versionInfo = $windowsDesktopVersion
            downloadLocation = "https://github.com/dotnet/wpf"
            filesAnalyzed = $false
            licenseConcluded = "MIT"
            licenseDeclared = "MIT"
            copyrightText = "Copyright Microsoft Corporation"
        }
    )
    relationships = @(
        [ordered]@{ spdxElementId = "SPDXRef-DOCUMENT"; relationshipType = "DESCRIBES"; relatedSpdxElement = "SPDXRef-Package-GonggongAX-Series4" },
        [ordered]@{ spdxElementId = "SPDXRef-Package-GonggongAX-Series4"; relationshipType = "DEPENDS_ON"; relatedSpdxElement = "SPDXRef-Package-ScreenRecorderLib" },
        [ordered]@{ spdxElementId = "SPDXRef-Package-GonggongAX-Series4"; relationshipType = "DEPENDS_ON"; relatedSpdxElement = "SPDXRef-Package-SharpHook" },
        [ordered]@{ spdxElementId = "SPDXRef-Package-SharpHook"; relationshipType = "DEPENDS_ON"; relatedSpdxElement = "SPDXRef-Package-libuiohook" },
        [ordered]@{ spdxElementId = "SPDXRef-Package-GonggongAX-Series4"; relationshipType = "DEPENDS_ON"; relatedSpdxElement = "SPDXRef-Package-Microsoft-NETCore-App" },
        [ordered]@{ spdxElementId = "SPDXRef-Package-GonggongAX-Series4"; relationshipType = "DEPENDS_ON"; relatedSpdxElement = "SPDXRef-Package-Microsoft-WindowsDesktop-App" }
    )
}
$sbom | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $publishDir "SBOM.spdx.json") -Encoding UTF8

$requiredPublishFiles = @(
    $appExeName,
    $appDllName,
    $appDepsName,
    $runtimeConfigPath.Name,
    "ScreenRecorderLib.dll",
    "SharpHook.dll",
    "uiohook.dll",
    "LICENSE.txt",
    "THIRD_PARTY_NOTICES.md",
    "OPEN_SOURCE_COMPONENTS.md",
    "SBOM.spdx.json",
    "licenses\dotnet\Microsoft.NETCore.App-$netCoreVersion-THIRD-PARTY-NOTICES.txt",
    "licenses\dotnet\Microsoft.WindowsDesktop.App-$windowsDesktopVersion-LICENSE.txt",
    "licenses\corresponding-source\SharpHook-7.1.3-Corresponding-Source.zip"
)
foreach ($relativePath in $requiredPublishFiles) {
    $candidate = Join-Path $publishDir $relativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Required release file is missing: $relativePath"
    }
}

$x64Machine = 0x8664
foreach ($relativePath in @($appExeName, "ScreenRecorderLib.dll", "uiohook.dll")) {
    $candidate = Join-Path $publishDir $relativePath
    $machine = Get-PeMachine $candidate
    if ($machine -ne $x64Machine) {
        throw "Release binary is not PE x64: $relativePath (0x$($machine.ToString('x4')))"
    }
}

$versionInfo = (Get-Item -LiteralPath (Join-Path $publishDir $appExeName)).VersionInfo
if (-not $versionInfo.ProductVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
    throw "Executable version $($versionInfo.ProductVersion) does not match $Version."
}

if (Get-ChildItem -LiteralPath $publishDir -Filter "*.pdb" -File) {
    throw "PDB files must not be present in the public package."
}
if (Get-ChildItem -LiteralPath $publishDir -Filter "*Tests*" -File) {
    throw "Test binaries must not be present in the public package."
}

$portableFolderName = "GonggongAX-Series4-Portable-x64-v$Version"
$portableFolder = Join-Path $stagingDir $portableFolderName
Copy-Item -LiteralPath $publishDir -Destination $portableFolder -Recurse
$portableArchive = Join-Path $releaseDir "$portableFolderName.zip"
[IO.Compression.ZipFile]::CreateFromDirectory(
    $portableFolder,
    $portableArchive,
    [IO.Compression.CompressionLevel]::Optimal,
    $true
)
$portableZip = [IO.Compression.ZipFile]::OpenRead($portableArchive)
try {
    $portableEntryNames = @(
        $portableZip.Entries |
            ForEach-Object { $_.FullName.Replace('\', '/') }
    )
    $duplicateEntries = @(
        $portableEntryNames |
            Group-Object |
            Where-Object Count -gt 1
    )
    if ($duplicateEntries.Count -gt 0) {
        throw "Portable archive contains duplicate paths."
    }
    foreach ($appFileName in @($appExeName, $appDllName, $appDepsName, $runtimeConfigPath.Name)) {
        $expectedEntry = "$portableFolderName/$appFileName"
        if ($portableEntryNames -notcontains $expectedEntry) {
            throw "Portable archive did not preserve the application filename: $expectedEntry"
        }
    }
}
finally {
    $portableZip.Dispose()
}

Invoke-Checked $isccExe @(
    "/Qp",
    "/DAppVersion=$Version",
    "/DSourceDir=$publishDir",
    "/DOutputDir=$releaseDir",
    $installerScript
)

$installerPath = Join-Path $releaseDir "GonggongAX-Series4-Setup-x64-v$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer was not created: $installerPath"
}

$checksumAssets = Get-ChildItem -LiteralPath $releaseDir -File | Sort-Object Name
$checksumLines = foreach ($asset in $checksumAssets) {
    $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($asset.Name)"
}
Set-Content -LiteralPath (Join-Path $releaseDir "SHA256SUMS.txt") -Value $checksumLines -Encoding ASCII

Write-Host "Release package ready: $releaseDir"
Get-ChildItem -LiteralPath $releaseDir -File | Select-Object Name, Length
