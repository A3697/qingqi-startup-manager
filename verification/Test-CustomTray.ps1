$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $PSScriptRoot ('custom-tray-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
Copy-Item -LiteralPath "$project\release\QingQi.Core.dll" -Destination $folder
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$folder\QingQi.Core.dll" "/out:$folder\CustomTrayFixture.exe" "$PSScriptRoot\CustomTrayFixture.cs"
if ($LASTEXITCODE -ne 0) { throw '自定义托盘测试编译失败' }
foreach ($kind in @('id','guid')) {
    & "$folder\CustomTrayFixture.exe" $kind
    if ($LASTEXITCODE -ne 0) { throw "托盘检测失败：$kind" }
}
[ordered]@{ Passed=$true; NumericIdDetected=$true; GuidDetected=$true; RunRoot=$folder } | ConvertTo-Json | Set-Content -LiteralPath "$PSScriptRoot\custom-tray.json" -Encoding utf8
