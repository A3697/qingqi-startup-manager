$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$root = Join-Path $PSScriptRoot ('edges-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $root | Out-Null
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$root\Fixture.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败。' }
function New-Case([string]$name, [int]$mode, [int]$observe, [bool]$follow, [string]$extra = '') {
    $folder = Join-Path $root $name
    $bin = Join-Path $folder 'bin'; $data = Join-Path $folder 'data'
    New-Item -ItemType Directory -Path $bin,$data -Force | Out-Null
    foreach ($file in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $project ('release\' + $file)) -Destination $bin }
    $fixture = Join-Path $bin ($name + '.exe')
    Copy-Item -LiteralPath (Join-Path $root 'Fixture.exe') -Destination $fixture
    $entry = [ordered]@{ Id=$name;Enabled=$true;Name=$name;Path=$fixture;Arguments=('"' + $folder + '" 22000 ' + $extra);WorkingDirectory=$bin;WindowPath='';FollowExisting=$follow;Priority=10;DelaySeconds=0;Mode=$mode;ShowSeconds=3;CpuPriority='Default';Source='边界测试' }
    $config = [ordered]@{Version=1;ExitAfterRun=$true;ObserveSeconds=$observe;Entries=@($entry)}
    $config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $data '配置.json') -Encoding utf8
    @{ Folder=$folder;Bin=$bin;Data=$data;Exe=(Join-Path $bin '轻启.exe');Entry=$entry;Config=$config;Log=(Join-Path $folder ($name + '.log')) }
}
function Start-Case($case) {
    Start-Process -FilePath $case.Exe -ArgumentList @('--startup','--data-dir',('"' + $case.Data + '"')) -WindowStyle Hidden -PassThru
}
function Get-Worker($case) { @(Get-Process -Name '轻启托盘助手' -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $case.Bin '轻启托盘助手.exe')) }
function Wait-Result($process,$case,[int]$seconds,[int]$exitCode) {
    if (-not $process.WaitForExit($seconds * 1000)) { throw "未按时退出：$($case.Entry.Name)" }
    if ($process.ExitCode -ne $exitCode) { throw "退出码不符：$($case.Entry.Name)，$($process.ExitCode)" }
    $process.Dispose()
    Get-Content -LiteralPath (Join-Path $case.Data '最近运行.json') -Raw | ConvertFrom-Json
}
function Wait-Empty($case,[int]$seconds=9) {
    $until = [datetime]::UtcNow.AddSeconds($seconds)
    while (@(Get-Worker $case).Count -ne 0 -and [datetime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 200 }
    if (@(Get-Worker $case).Count -ne 0) { throw "恢复后助手未退出：$($case.Entry.Name)" }
    $state = @(Get-Content -LiteralPath $case.Log | Where-Object { $_ -like 'STATE|*' })[-1]
    if ($state -notmatch '\|True\|(Normal|Maximized)') { throw "窗口未恢复：$($case.Entry.Name)" }
}

$late = New-Case 'Late' 4 4 $true
$main = Start-Case $late
Start-Sleep -Seconds 3
$fixture = Start-Process -FilePath $late.Entry.Path -ArgumentList $late.Entry.Arguments -WorkingDirectory $late.Bin -WindowStyle Normal -PassThru
$fixture.Dispose()
$result = Wait-Result $main $late 12 0
$lines = Get-Content -LiteralPath $late.Log
$first = ($lines | Where-Object { $_ -like 'START|*' } | Select-Object -First 1) -split '\|'
$minimized = ($lines | Where-Object { $_ -match '\|True\|Minimized' } | Select-Object -First 1) -split '\|'
if ($minimized.Count -lt 3 -or ([datetime]$minimized[1] - [datetime]$first[1]).TotalSeconds -lt 2.8 -or $result.WindowFailures -ne 0) { throw '观察期末尾的延后最小化没有完成。' }

$external = New-Case 'ExternalShow' 3 2 $false 'selfshow=4000'
$result = Wait-Result (Start-Case $external) $external 10 0
Wait-Empty $external
if (@(Get-Content -LiteralPath (Join-Path $external.Data '托盘窗口.json') -Raw | ConvertFrom-Json).Count -ne 0) { throw '软件自行恢复后仍有托盘记录。' }

$hung = New-Case 'HungRestore' 3 1 $false 'hang=6500'
$result = Wait-Result (Start-Case $hung) $hung 10 1
if ($result.WindowFailures -ne 1 -or @(Get-Worker $hung).Count -ne 1) { throw '延迟隐藏超时没有正确报告或丢失恢复助手。' }
$records = @(Get-Content -LiteralPath (Join-Path $hung.Data '托盘窗口.json') -Raw | ConvertFrom-Json)
if ($records.Count -ne 1) { throw '延迟隐藏的恢复记录被提前删除。' }
$restore = Start-Process -FilePath $hung.Exe -ArgumentList @('--data-dir',('"' + $hung.Data + '"'),'--restore-entry','HungRestore') -WindowStyle Hidden -PassThru
if (-not $restore.WaitForExit(5000) -or $restore.ExitCode -ne 0) { throw '排队恢复请求失败。' }
$restore.Dispose()
Wait-Empty $hung

$change = New-Case 'HungChange' 3 1 $false 'hang=6500'
$result = Wait-Result (Start-Case $change) $change 10 1
$change.Entry.Mode = 1; $change.Config.ObserveSeconds = 4
$change.Config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $change.Data '配置.json') -Encoding utf8
$result = Wait-Result (Start-Case $change) $change 12 0
Wait-Empty $change
$last = @(Get-Content -LiteralPath $change.Log | Where-Object { $_ -like 'STATE|*' })[-1]
if ($last -notmatch '\|True\|Maximized') { throw '托盘改最大化没有完成。' }

$missing = New-Case 'WorkerExit' 2 1 $true
'internal static class Program { private static void Main() {} }' | Set-Content -LiteralPath (Join-Path $missing.Folder 'EmptyWorker.cs') -Encoding utf8
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe "/out:$($missing.Bin)\轻启托盘助手.exe" (Join-Path $missing.Folder 'EmptyWorker.cs')
if ($LASTEXITCODE -ne 0) { throw '故障模拟助手编译失败。' }
$result = Wait-Result (Start-Case $missing) $missing 14 1
if ($result.WindowFailures -ne 1 -or $result.WindowError -notlike '*没有返回完成结果*') { throw '助手异常退出被误报为成功。' }

$minDelay = New-Case 'MinimizedDelay' 4 2 $true 'startmin hang=1500'
$fixture = Start-Process -FilePath $minDelay.Entry.Path -ArgumentList $minDelay.Entry.Arguments -WorkingDirectory $minDelay.Bin -WindowStyle Normal -PassThru
$fixture.Dispose()
$result = Wait-Result (Start-Case $minDelay) $minDelay 14 0
$lines = Get-Content -LiteralPath $minDelay.Log
$opened = ($lines | Where-Object { $_ -match '\|True\|Normal' } | Select-Object -First 1) -split '\|'
$closed = @($lines | Where-Object { $_ -match '\|True\|Minimized' } | Select-Object -Last 1) -split '\|'
if ($opened.Count -lt 3 -or $closed.Count -lt 3 -or ([datetime]$closed[1] - [datetime]$opened[1]).TotalSeconds -lt 2.8) { throw '停留时间没有从真正展开窗口后计时。' }

$stuck = New-Case 'RestoreTimeout' 3 1 $false 'hangafter=4000:6000'
$result = Wait-Result (Start-Case $stuck) $stuck 10 0
$until = [datetime]::UtcNow.AddSeconds(4)
while (-not ((Get-Content -LiteralPath $stuck.Log) -match '^HANG\|') -and [datetime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 100 }
if (-not ((Get-Content -LiteralPath $stuck.Log) -match '^HANG\|')) { throw '恢复超时模拟未开始。' }
$stuck.Entry.Mode = 1
$stuck.Config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stuck.Data '配置.json') -Encoding utf8
$result = Wait-Result (Start-Case $stuck) $stuck 5 1
if ($result.WindowFailures -ne 1 -or @(Get-Worker $stuck).Count -ne 1) { throw '恢复一直未确认却报告成功或丢失助手。' }
Wait-Empty $stuck

$proof = [ordered]@{Time=(Get-Date).ToString('o');Passed=$true;RunRoot=$root;LateWindowCompletesDisplayDuration=$true;ExternalRestoreReleasesHelper=$true;SlowHideKeepsRecovery=$true;QueuedRestoreIsVerified=$true;ModeChangeRestoresBeforeRelease=$true;HelperFailureIsReported=$true;DwellStartsAfterActualRestore=$true;UnconfirmedRestoreIsFailure=$true}
$proof | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'window-edges.json') -Encoding utf8
$proof | ConvertTo-Json -Depth 4
