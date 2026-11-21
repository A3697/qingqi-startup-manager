param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & "$PSScriptRoot\build.ps1" }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo("$PSScriptRoot\release\轻启.exe").FileVersion -replace '\.0$',''
$dist = Join-Path $PSScriptRoot 'dist'
$stage = Join-Path $dist ('stage-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$portable = Join-Path $stage 'QingQi'
New-Item -ItemType Directory -Path "$portable\references" -Force | Out-Null
foreach ($file in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath "$PSScriptRoot\release\$file" -Destination $portable }
foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','使用说明.txt')) { Copy-Item -LiteralPath "$PSScriptRoot\$file" -Destination $portable }
foreach ($file in @('StartupManager-MIT.txt','SmartSystemMenu-MIT.txt')) { Copy-Item -LiteralPath "$PSScriptRoot\references\$file" -Destination "$portable\references" }
New-Item -ItemType Directory -Path "$portable\docs" -Force | Out-Null
Get-ChildItem -LiteralPath "$PSScriptRoot\docs" -Filter '*.md' -File | Copy-Item -Destination "$portable\docs"
$zip = Join-Path $dist "QingQi-$version-portable.zip"
if (Test-Path -LiteralPath $zip) { throw "发行包已存在，请保留或重命名后重新打包：$zip" }
Compress-Archive -LiteralPath $portable -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $zip)" | Set-Content -LiteralPath "$dist\SHA256SUMS.txt" -Encoding ascii
Get-Item -LiteralPath $zip | Select-Object Name,Length
Get-Content -LiteralPath "$dist\SHA256SUMS.txt"
