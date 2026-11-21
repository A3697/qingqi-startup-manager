$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('tray22-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'release\QingQi.Core.dll') -Destination $folder
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$folder\Fixture.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '测试窗口编译失败' }
foreach ($name in @('Native','NoNativeBehavior','Missing','Late','LateBusy','Manual','LateBusyRelease')) { Copy-Item -LiteralPath (Join-Path $folder 'Fixture.exe') -Destination (Join-Path $folder ($name + '.exe')) }
& $compiler /nologo /target:exe /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$folder\QingQi.Core.dll" "/out:$folder\Tray22.exe" (Join-Path $PSScriptRoot 'Tray22.cs')
if ($LASTEXITCODE -ne 0) { throw '托盘回归编译失败' }
& (Join-Path $folder 'Tray22.exe') $folder
if ($LASTEXITCODE -ne 0) { throw "托盘回归失败：$folder" }
$result = Get-Content -LiteralPath (Join-Path $folder 'data\result.json') -Raw | ConvertFrom-Json
$result | Add-Member NoteProperty RunRoot $folder
$result | Add-Member NoteProperty Time (Get-Date).ToString('o')
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'tray22.json') -Encoding utf8
$result | ConvertTo-Json
