param([string]$OutputName = 'WinBoard.exe')
$ErrorActionPreference = 'Stop'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @('WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll','WPF\PresentationFramework.dll','WPF\PresentationCore.dll','WPF\WindowsBase.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Runtime.Serialization.dll','System.Net.Http.dll','System.Management.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64',('/out:' + (Join-Path $PSScriptRoot $OutputName)))
$compilerArgs += ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\WinBoard.ico'))
$compilerArgs += ('/resource:' + (Join-Path $PSScriptRoot 'assets\WinBoard.png') + ',WinBoard.Brand.png')
$compilerArgs += ('/resource:' + (Join-Path $PSScriptRoot 'assets\WinBoard.ico') + ',WinBoard.Brand.ico')
foreach ($ref in $refs) { $compilerArgs += ('/reference:' + (Join-Path $framework $ref)) }
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Dashboard.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Widgets.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Appearance.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Glass.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Controls.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Shell.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Dock.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.DockPreview.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Taskbar.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.NativeTray.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.RunningApps.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.WidgetLayout.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.DockVisibility.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.AppSettings.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.DualWindows.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.ProjectColors.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.PresentationOptions.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Connections.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Recycle.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.RecycleAnimation.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.AppIcons.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Power.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.Capture.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.CaptureRecording.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'WinBoard.ScreenshotOperation.cs')
$recorder=Join-Path $PSScriptRoot 'lib/ScreenRecorderLib/ScreenRecorderLib.dll'
$compilerArgs += ('/reference:' + $recorder)
$runtime=Join-Path $PSScriptRoot 'ScreenRecorderLib.dll'
if(!(Test-Path $runtime) -or (Get-FileHash $runtime).Hash -ne (Get-FileHash $recorder).Hash){Copy-Item -LiteralPath $recorder -Destination $runtime -Force}

& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
