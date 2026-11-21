$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$release = Join-Path $project 'release'
$root = Join-Path $PSScriptRoot ('follow-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bin = Join-Path $root 'bin'
$data = Join-Path $root 'data'
New-Item -ItemType Directory -Path $bin,$data -Force | Out-Null
foreach ($name in @('轻启.exe','轻启托盘助手.exe','QingQi.Core.dll')) { Copy-Item -LiteralPath (Join-Path $release $name) -Destination $bin }
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$bin\FollowFixture.exe" (Join-Path $PSScriptRoot 'Fixture.cs')
if ($LASTEXITCODE -ne 0) { throw '测试目标编译失败。' }
function Get-RunFingerprint {
    $rows = foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)) {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
            try {
                $key = $base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$false)
                if ($null -eq $key) { continue }
                try { foreach ($name in $key.GetValueNames()) { "$hive|$view|$name|$($key.GetValue($name))" } } finally { $key.Dispose() }
            } finally { $base.Dispose() }
        }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes((($rows | Sort-Object) -join [Environment]::NewLine))
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}
$before = Get-RunFingerprint
$entry = [ordered]@{ Id='follow';Enabled=$true;Name='原启动模拟';Path=(Join-Path $bin 'FollowFixture.exe');Arguments=('"' + $root + '" 30000');WorkingDirectory=$bin;WindowPath='';FollowExisting=$true;Priority=1;DelaySeconds=0;Mode=2;ShowSeconds=2;CpuPriority='Default';Source='外部启动模拟' }
$config = [ordered]@{Version=1;ExitAfterRun=$false;ObserveSeconds=6;Entries=@($entry)}
$configPath = Join-Path $data '配置.json'
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8
$exe = Join-Path $bin '轻启.exe'
$main = Start-Process -FilePath $exe -ArgumentList @('--startup','--data-dir',('"' + $data + '"')) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 2
if (Test-Path -LiteralPath (Join-Path $root 'FollowFixture.log')) { throw '跟随模式擅自启动了目标。' }
$external = Start-Process -FilePath $entry.Path -ArgumentList $entry.Arguments -WorkingDirectory $bin -WindowStyle Normal -PassThru
$deadline = [datetime]::UtcNow.AddSeconds(12)
while (-not (Test-Path -LiteralPath (Join-Path $data '最近运行.json')) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
$result = Get-Content -LiteralPath (Join-Path $data '最近运行.json') -Raw | ConvertFrom-Json
if ($result.Started -ne 0 -or $result.Followed -ne 1 -or $result.Failed -ne 0) { throw '跟随模式结果异常。' }
$states = Get-Content -LiteralPath (Join-Path $root 'FollowFixture.log')
if (@($states | Where-Object { $_ -like 'START|*' }).Count -ne 1 -or -not ($states -match '\|True\|Minimized')) { throw '跟随窗口管理失败或目标重复启动。' }
Start-Sleep -Seconds 1
if ($main.HasExited) { throw '关闭自动退出选项后，主程序仍退出了。' }
$lastWrite = (Get-Item -LiteralPath (Join-Path $data '最近运行.json')).LastWriteTimeUtc
$second = Start-Process -FilePath $exe -ArgumentList @('--startup','--data-dir',('"' + $data + '"')) -WindowStyle Hidden -PassThru
if (-not $second.WaitForExit(5000) -or $second.ExitCode -ne 0) { throw '单实例验证失败。' }
$second.Dispose()
if ((Get-Item -LiteralPath (Join-Path $data '最近运行.json')).LastWriteTimeUtc -ne $lastWrite) { throw '重复实例重新执行了任务。' }
$guarded = Get-Process -Id $main.Id
if ($guarded.Path -ne $exe) { throw '测试进程身份发生变化，拒绝结束。' }
Stop-Process -Id $main.Id
$main.Dispose()
$entry.FollowExisting = $false; $entry.Mode = 0
$missing = [ordered]@{ Id='missing';Enabled=$true;Name='缺失目标';Path=(Join-Path $bin 'Missing.exe');Arguments='';WorkingDirectory=$bin;WindowPath='';FollowExisting=$false;Priority=1;DelaySeconds=0;Mode=0;ShowSeconds=2;CpuPriority='Default';Source='隔离测试' }
$config.ExitAfterRun = $true; $config.Entries = @($missing,$entry)
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8
$test = Start-Process -FilePath $exe -ArgumentList @('--startup','--data-dir',('"' + $data + '"')) -WindowStyle Hidden -PassThru
if (-not $test.WaitForExit(7000) -or $test.ExitCode -ne 1) { throw '故障退出码验证失败。' }
$test.Dispose()
$result2 = Get-Content -LiteralPath (Join-Path $data '最近运行.json') -Raw | ConvertFrom-Json
if ($result2.Failed -ne 1 -or $result2.AlreadyRunning -ne 1) { throw '故障隔离或防重复拉起失败。' }
$config.Version = 999
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8
$hash = (Get-FileHash -LiteralPath $configPath).Hash
$bad = Start-Process -FilePath $exe -ArgumentList @('--startup','--data-dir',('"' + $data + '"')) -WindowStyle Hidden -PassThru
if (-not $bad.WaitForExit(5000) -or $bad.ExitCode -ne 1) { throw '坏配置处理失败。' }
$bad.Dispose()
if ((Get-FileHash -LiteralPath $configPath).Hash -ne $hash) { throw '坏配置被覆盖。' }
$after = Get-RunFingerprint
if ($before -ne $after) { throw '测试期间 Run 启动项发生变化，需要复核。' }
$proof = [ordered]@{Time=(Get-Date).ToString('o');Passed=$true;RunRoot=$root;FollowNeverLaunchesTarget=$true;LateExternalStartupHandled=$true;KeepLauncherOpenVerified=$true;SingleInstanceVerified=$true;DuplicateSuppressed=$true;FailureDoesNotStopOtherItems=$true;BadConfigPreserved=$true;RunRegistryFingerprintBefore=$before;RunRegistryFingerprintAfter=$after}
$proof | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'follow-lifecycle.json') -Encoding utf8
$proof | ConvertTo-Json -Depth 4
