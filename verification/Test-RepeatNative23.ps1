$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('repeat-native23-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $project ('release\' + $name)) -Destination $folder }
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$folder\Fixture.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '测试窗口编译失败' }
& $compiler /nologo /target:exe /reference:System.Core.dll "/reference:$folder\QingQi.Core.dll" "/out:$folder\RepeatNative23.exe" (Join-Path $PSScriptRoot 'RepeatNative23.cs')
if ($LASTEXITCODE -ne 0) { throw '重复托盘执行测试编译失败' }
& (Join-Path $folder 'RepeatNative23.exe') $folder
if ($LASTEXITCODE -ne 0) { throw "重复托盘执行测试失败：$folder" }
$result = Get-Content -LiteralPath (Join-Path $folder 'data\result.json') -Raw | ConvertFrom-Json
$result | Add-Member NoteProperty RunRoot $folder
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'repeat-native23.json') -Encoding utf8
