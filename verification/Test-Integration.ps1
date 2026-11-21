$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$release = Join-Path $project 'release'
$runRoot = Join-Path $PSScriptRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bin = Join-Path $runRoot 'bin'
$data = Join-Path $runRoot 'data'
New-Item -ItemType Directory -Path $bin,$data -Force | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $release $name) -Destination $bin }
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$bin\Fixture.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败。' }
$entries = @()
$modes = @(@('Max',1),@('Min',2),@('Tray',3),@('AfterMin',4),@('AfterTray',5))
foreach ($pair in $modes) {
    $target = Join-Path $bin ($pair[0] + '.exe')
    Copy-Item -LiteralPath (Join-Path $bin 'Fixture.exe') -Destination $target
    $entries += [ordered]@{
        Id = $pair[0]; Enabled = $true; Name = $pair[0]; Path = $target
        Arguments = '"' + $runRoot + '" 25000'
        WorkingDirectory = $bin; WindowPath = ''; FollowExisting = $false
        Priority = $pair[1] * 10; DelaySeconds = 0; Mode = $pair[1]
        ShowSeconds = 2; CpuPriority = 'Default'; Source = '隔离测试'
    }
}
$entries[1].DelaySeconds = 1
$entries[1].CpuPriority = 'BelowNormal'
$config = [ordered]@{ Version = 1; ExitAfterRun = $true; ObserveSeconds = 5; Entries = @($entries[4],$entries[2],$entries[0],$entries[3],$entries[1]) }
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $data '配置.json') -Encoding utf8
$main = Start-Process -FilePath (Join-Path $bin '轻启.exe') -ArgumentList @('--startup','--data-dir',('"' + $data + '"')) -WorkingDirectory 'C:\Windows' -WindowStyle Hidden -PassThru
$watch = [Diagnostics.Stopwatch]::StartNew()
if (-not $main.WaitForExit(20000)) { throw "五模式测试：启动器没有退出，PID $($main.Id)" }
if ($main.ExitCode -ne 0) { throw "五模式测试：退出码 $($main.ExitCode)" }
$main.Dispose()
$result = Get-Content -LiteralPath (Join-Path $data '最近运行.json') -Raw | ConvertFrom-Json
if ($result.Started -ne 5 -or $result.Failed -ne 0) { throw '未正确拉起 5 个目标。' }
$starts = @()
foreach ($pair in $modes) {
    $lines = Get-Content -LiteralPath (Join-Path $runRoot ($pair[0] + '.log'))
    $first = ($lines | Where-Object { $_ -like 'START|*' } | Select-Object -First 1) -split '\|'
    if ($first[2] -ne $bin) { throw '目标工作目录不正确。' }
    $states = @($lines | Where-Object { $_ -like 'STATE|*' })
    if ($pair[0] -eq 'Max' -and -not ($states -match '\|True\|Maximized')) { throw '最大化未生效。' }
    if ($pair[0] -eq 'Min' -and -not ($states -match '\|True\|Minimized')) { throw '最小化未生效。' }
    if ($pair[0] -match 'Tray' -and -not ($states -match '\|False\|')) { throw '托盘隐藏未生效。' }
    if ($pair[0] -eq 'AfterMin' -and -not ($states -match '\|True\|Minimized')) { throw '延时最小化未生效。' }
    if ($pair[0] -like 'After*') {
        $normal = @($states | Where-Object { $_ -match '\|True\|Normal' })
        if ($normal.Count -lt 10) { throw '先打开窗口的停留时间不足。' }
        $firstAction = ($states | Where-Object { if ($pair[0] -eq 'AfterTray') { $_ -match '\|False\|' } else { $_ -match '\|True\|Minimized' } } | Select-Object -First 1) -split '\|'
        if (([datetime]$firstAction[1] - [datetime]$first[1]).TotalSeconds -lt 1.8) { throw '延后窗口操作过早。' }
    }
    $starts += [datetime]$first[1]
}
for ($i=1; $i -lt $starts.Count; $i++) { if ($starts[$i] -lt $starts[$i-1]) { throw '启动顺序错误。' } }
if (($starts[1] - $starts[0]).TotalSeconds -lt 1.3) { throw '独立启动等待未生效。' }
$minProc = Get-Process -Name 'Min' | Where-Object Path -eq (Join-Path $bin 'Min.exe')
if ($minProc.PriorityClass -ne 'BelowNormal') { throw 'CPU 优先级未生效。' }
$beforeRestore = @(Get-Process -Name '轻启托盘助手' -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $bin '轻启托盘助手.exe'))
if ($beforeRestore.Count -ne 1) { throw '主程序退出后，托盘助手未保留。' }
foreach ($id in @('Tray','AfterTray')) {
    $p = Start-Process -FilePath (Join-Path $bin '轻启.exe') -ArgumentList @('--data-dir',('"' + $data + '"'),'--restore-entry',$id) -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(5000) -or $p.ExitCode -ne 0) { throw '恢复托盘窗口命令失败。' }
    $p.Dispose()
}
Start-Sleep -Seconds 2
foreach ($id in @('Tray','AfterTray')) {
    $last = @(Get-Content -LiteralPath (Join-Path $runRoot ($id + '.log')) | Where-Object { $_ -like 'STATE|*' })[-1]
    if ($last -notmatch '\|True\|Normal') { throw "托盘窗口恢复失败：$id" }
}
$workerAfter = @(Get-Process -Name '轻启托盘助手' -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $bin '轻启托盘助手.exe'))
if ($workerAfter.Count -ne 0) { throw '恢复所有窗口后，托盘助手仍在运行。' }
$verification = [ordered]@{
    Time = (Get-Date).ToString('o'); Passed = $true; RunRoot = $runRoot
    FiveModesVerified = $true; PerAppOrderAndDelayVerified = $true
    WorkingDirectoryVerified = $true; CpuPriorityVerified = $true
    LauncherExitVerified = $true; TrayHelperSurvivesLauncher = $true
    TrayRestoreVerified = $true; TrayHelperExitsWhenEmpty = $true
    ElapsedSeconds = [math]::Round($watch.Elapsed.TotalSeconds,2)
}
$verification | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'five-modes.json') -Encoding utf8
$verification | ConvertTo-Json -Depth 4
