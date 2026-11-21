$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
& "$project\build.ps1"
$suites = [ordered]@{
    'Test-Ui22.ps1' = 'ui22.json'
    'Test-StartupUi23.ps1' = 'startup-ui23.json'
    'Test-Cancellation24.ps1' = 'cancel24.json'
    'Test-CustomTray.ps1' = 'custom-tray.json'
    'Test-Integration.ps1' = 'five-modes.json'
    'Test-FollowAndLifecycle.ps1' = 'follow-lifecycle.json'
    'Test-WindowEdges.ps1' = 'window-edges.json'
    'Test-Tray22.ps1' = 'tray22.json'
    'Test-Tray23.ps1' = 'tray23.json'
    'Test-RepeatNative23.ps1' = 'repeat-native23.json'
    'Test-RestoreRace23.ps1' = 'restore-race23.json'
    'Test-Notice22.ps1' = 'notice22.json'
}
$started = Get-Date
$results = @()
foreach ($suite in $suites.GetEnumerator()) {
    $suiteStart = Get-Date
    Write-Output "Running $($suite.Key)"
    & (Join-Path $PSScriptRoot $suite.Key)
    $report = Get-Item -LiteralPath (Join-Path $PSScriptRoot $suite.Value)
    if ($report.LastWriteTime -lt $suiteStart) { throw "报告未更新：$($suite.Key)" }
    $data = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json
    if (-not $data.Passed) { throw "验证未通过：$($suite.Key)" }
    $results += [ordered]@{ Suite=$suite.Key; Seconds=[math]::Round(((Get-Date)-$suiteStart).TotalSeconds,2); Result=$data }
}
$hashes = foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { $file=Get-Item -LiteralPath "$project\release\$name"; [ordered]@{Name=$name; Size=$file.Length; SHA256=(Get-FileHash -LiteralPath $file.FullName).Hash} }
[ordered]@{Passed=$true; Started=$started.ToString('o'); Finished=(Get-Date).ToString('o'); Suites=$results; Binaries=$hashes} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath "$PSScriptRoot\full-suite.json" -Encoding utf8
Write-Output "PASS: $($results.Count) suites"
