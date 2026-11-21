$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('ui22-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $project ('release\' + $name)) -Destination $folder }
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$folder\QingQi.Core.dll" "/reference:$folder\轻启.exe" "/out:$folder\Ui22.exe" (Join-Path $PSScriptRoot 'Ui22.cs')
if ($LASTEXITCODE -ne 0) { throw '界面测试编译失败' }
& (Join-Path $folder 'Ui22.exe') $folder
if ($LASTEXITCODE -ne 0) { throw "界面规则测试失败：$folder" }
$result = Get-Content -LiteralPath (Join-Path $folder 'data\result.json') -Raw | ConvertFrom-Json
$result | Add-Member NoteProperty RunRoot $folder
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ui22.json') -Encoding utf8
