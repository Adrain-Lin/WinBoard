$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputName WinBoard.next.exe
$compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (('/win32icon:')+(Join-Path $PSScriptRoot 'assets\WinBoard.ico')) (('/resource:')+(Join-Path $PSScriptRoot 'WinBoard.next.exe')+',Payload') (('/out:')+(Join-Path $PSScriptRoot 'dist\WinBoard-Setup-1.2.0.exe')) (Join-Path $PSScriptRoot 'installer\Setup.cs')
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
Get-FileHash (Join-Path $PSScriptRoot 'dist\WinBoard-Setup-1.2.0.exe') -Algorithm SHA256
