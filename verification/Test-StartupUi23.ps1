$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('startup-ui23-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $project ('release\' + $name)) -Destination $folder }
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /reference:System.Core.dll "/reference:$folder\QingQi.Core.dll" "/out:$folder\StartupUi23.exe" (Join-Path $PSScriptRoot 'StartupUi23.cs')
if ($LASTEXITCODE -ne 0) { throw '启动界面测试编译失败' }
& (Join-Path $folder 'StartupUi23.exe') $folder
if ($LASTEXITCODE -ne 0) { throw "启动界面回归失败：$folder" }
$result = Get-Content -LiteralPath (Join-Path $folder 'data\result.json') -Raw | ConvertFrom-Json
$result | Add-Member NoteProperty RunRoot $folder
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'startup-ui23.json') -Encoding utf8
