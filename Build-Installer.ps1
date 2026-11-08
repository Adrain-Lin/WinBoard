$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputName WinBoard.next.exe
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (('/win32icon:')+(Join-Path $PSScriptRoot 'assets\WinBoard.ico')) (('/resource:')+(Join-Path $PSScriptRoot 'WinBoard.next.exe')+',Payload') (('/resource:')+(Join-Path $PSScriptRoot 'lib/ScreenRecorderLib/ScreenRecorderLib.dll')+',RecorderPayload') (('/resource:')+(Join-Path $PSScriptRoot 'lib/ScreenRecorderLib/LICENSE')+',RecorderLicense') (('/resource:')+(Join-Path $PSScriptRoot 'LICENSE')+',AppLicense') (('/resource:')+(Join-Path $PSScriptRoot 'README.md')+',AppReadme') (('/resource:')+(Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md')+',ThirdPartyNotices') (('/out:')+(Join-Path $PSScriptRoot 'dist\WinBoard-Setup-1.3.1.exe')) (Join-Path $PSScriptRoot 'installer\Setup.cs')
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
Get-FileHash (Join-Path $PSScriptRoot 'dist\WinBoard-Setup-1.3.1.exe') -Algorithm SHA256
