$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('notice22-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $project ('release\' + $name)) -Destination $folder }
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$folder\NoticeTarget.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '提示测试目标编译失败' }
& $compiler /nologo /target:exe /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$folder\QingQi.Core.dll" "/reference:$folder\轻启.exe" "/out:$folder\Notice22.exe" (Join-Path $PSScriptRoot 'Notice22.cs')
if ($LASTEXITCODE -ne 0) { throw '提示测试编译失败' }
& (Join-Path $folder 'Notice22.exe') $folder
if ($LASTEXITCODE -ne 0) { throw "提示生命周期失败：$folder" }
$result = Get-Content -LiteralPath (Join-Path $folder 'data\result.json') -Raw | ConvertFrom-Json
$result | Add-Member NoteProperty RunRoot $folder
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'notice22.json') -Encoding utf8
