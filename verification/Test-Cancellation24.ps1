$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('cancel24-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $project ('release\' + $name)) -Destination $folder }
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$folder\LateCancel.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '测试窗口编译失败' }
Copy-Item -LiteralPath "$folder\LateCancel.exe" -Destination "$folder\Delayed.exe"
& $compiler /nologo /target:exe /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$folder\QingQi.Core.dll" "/out:$folder\Cancellation24.exe" (Join-Path $PSScriptRoot 'Cancellation24.cs')
if ($LASTEXITCODE -ne 0) { throw '取消测试编译失败' }
& "$folder\Cancellation24.exe" $folder
if ($LASTEXITCODE -ne 0) { throw "取消测试失败：$folder" }
$result = Get-Content -LiteralPath "$folder\data\result.json" -Raw | ConvertFrom-Json
$result | Add-Member NoteProperty RunRoot $folder
$result | ConvertTo-Json | Set-Content -LiteralPath "$PSScriptRoot\cancel24.json" -Encoding utf8
