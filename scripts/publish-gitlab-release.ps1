[CmdletBinding()]
param([string]$Version="4.2.0", [string]$Commit)
$ErrorActionPreference="Stop"
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$hostUrl="https://gitlab.aigov.go.kr"
$api="$hostUrl/api/v4/projects/217"
$releaseDir=Join-Path $repo "artifacts\release"
if (!$Commit) {throw "Pass the verified release commit"}
$remote=git -C $repo ls-remote gitlab "refs/tags/v$Version^{}"
if($LASTEXITCODE -ne 0 -or ($remote -split '\s+')[0] -ne $Commit){throw "GitLab annotated tag does not match release commit"}
# Never print/store credential helper output. Only send the token to its own HTTPS host.
$credentialLines="protocol=https`nhost=gitlab.aigov.go.kr`n`n" | git -C $repo -c credential.interactive=false credential fill
if($LASTEXITCODE -ne 0){throw "GitLab credential unavailable"}
$credential=$credentialLines -join "`n" | ConvertFrom-StringData
$headers=@{"PRIVATE-TOKEN"=$credential.password}
try {
 $exists=$false
 try { $null=Invoke-RestMethod -Uri "$api/releases/v$Version" -Headers $headers; $exists=$true }
 catch { if([int]$_.Exception.Response.StatusCode -ne 404){throw "Release lookup failed"} }
 if($exists){throw "Release already exists; refusing to replace assets"}
 $names=@(
  "GonggongAX-Series4-Setup-x64-v$Version.exe",
  "GonggongAX-Series4-Portable-x64-v$Version.zip",
  "GonggongAX-Series4-Intro-v$Version.zip",
  "SharpHook-7.1.3-Corresponding-Source.zip",
  "SHA256SUMS.txt"
 )
 $routes=@("/setup.exe","/portable.zip","/intro.zip","/corresponding-source.zip","/SHA256SUMS.txt")
 $links=@()
 for($i=0;$i -lt $names.Count;$i++){
  $file=Join-Path $releaseDir $names[$i]
  if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing asset: $($names[$i])"}
  $url="$api/packages/generic/gonggong-ax-series4/$Version/$($names[$i])"
  $response=Invoke-WebRequest -Uri $url -Method Put -Headers $headers -InFile $file -ContentType "application/octet-stream" -TimeoutSec 1200
  if($response.StatusCode -ne 201){throw "Upload failed: $($names[$i])"}
  $links+=@{name=$names[$i];url=$url;direct_asset_path=$routes[$i];link_type="package"}
  Write-Output "Uploaded: $($names[$i])"
 }
 $body=@{name="공공 AX 로컬 시리즈 4 · v$Version";tag_name="v$Version";description=(Get-Content (Join-Path $repo "docs\RELEASE_4.2.0.md") -Raw)+"`n`n소스 커밋: $Commit";assets=@{links=$links}}|ConvertTo-Json -Depth 8
 $created=Invoke-RestMethod -Uri "$api/releases" -Method Post -Headers $headers -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($body))
 [pscustomobject]@{tag=$created.tag_name;url=$created._links.self;asset_count=$created.assets.links.Count}|ConvertTo-Json -Compress
} finally {
 $headers.Clear();$credential=$null;$credentialLines=$null
}
