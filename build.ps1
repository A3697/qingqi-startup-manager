$ErrorActionPreference = 'Stop'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $PSScriptRoot 'src'
$output = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$common = @('/nologo', '/optimize+', '/utf8output', '/platform:anycpu', '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll', '/reference:Microsoft.CSharp.dll')
$librarySources = @('Core.cs', 'Engine.cs', 'WindowWorker.cs', 'Artwork.cs', 'TrayDetector.cs', 'ScheduledStartup.cs') | ForEach-Object { Join-Path $src $_ }
if (-not (Test-Path -LiteralPath "$PSScriptRoot\assets\Main.ico")) { & "$PSScriptRoot\Make-Icons.ps1" }
& $compiler @common /target:library "/resource:$PSScriptRoot\assets\Main.ico,QingQi.Main.ico" "/resource:$PSScriptRoot\assets\Worker.ico,QingQi.Worker.ico" "/out:$output\QingQi.Core.dll" @librarySources
if ($LASTEXITCODE -ne 0) { throw '核心库编译失败。' }
& $compiler @common /target:winexe "/win32icon:$PSScriptRoot\assets\Main.ico" "/win32manifest:$src\app.manifest" "/reference:$output\QingQi.Core.dll" "/out:$output\轻启.exe" "$src\Program.cs" "$src\Ui.cs" "$src\EntryDialog.cs" "$src\ToggleSwitch.cs" "$src\SoftwareDialog.cs" "$src\UiSurface.cs"
if ($LASTEXITCODE -ne 0) { throw '界面编译失败。' }
& $compiler @common /target:winexe /define:TRAY_HELPER "/win32icon:$PSScriptRoot\assets\Worker.ico" "/win32manifest:$src\app.manifest" "/reference:$output\QingQi.Core.dll" "/out:$output\轻启托盘助手.exe" "$src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw '托盘助手编译失败。' }
Get-ChildItem -LiteralPath $output -File | Select-Object Name, Length
