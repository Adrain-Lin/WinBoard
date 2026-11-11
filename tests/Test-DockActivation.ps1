param([string]$Executable=(Join-Path $PSScriptRoot '../WinBoard.exe'))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$stage=Join-Path ([IO.Path]::GetTempPath()) ('WinBoard-DockActivation-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$name=[Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $Executable)).Name+'.exe'
Copy-Item -LiteralPath $Executable -Destination (Join-Path $stage $name)
Copy-Item -LiteralPath (Join-Path $repo 'ScreenRecorderLib.dll') -Destination $stage
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.Runtime.Serialization.dll','System.Xaml.dll','WPF/PresentationFramework.dll','WPF/PresentationCore.dll','WPF/WindowsBase.dll')
$argsList=@('/nologo','/target:winexe','/platform:x64',('/out:'+(Join-Path $stage 'DockActivationSmoke.exe')),('/reference:'+(Join-Path $stage $name)))
foreach($ref in $refs){$argsList+=('/reference:'+(Join-Path $framework $ref))}
$argsList+=(Join-Path $PSScriptRoot 'DockActivationSmoke.cs')
& (Join-Path $framework 'csc.exe') @argsList
if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
$shell=New-Object -ComObject WScript.Shell
$link=$shell.CreateShortcut((Join-Path $stage 'fixture.lnk'))
$link.TargetPath=Join-Path $stage 'DockActivationSmoke.exe';$link.Arguments='original-shortcut';$link.WorkingDirectory=$stage;$link.Save()
$probe=Start-Process (Join-Path $stage 'DockActivationSmoke.exe') -WorkingDirectory $stage -PassThru
if(!$probe.WaitForExit(15000)){throw "Test timed out: $stage"}
if($probe.ExitCode -ne 0 -or (Test-Path (Join-Path $stage 'error.txt'))){throw "Test failed: $stage"}
Get-Content (Join-Path $stage 'result.txt')
"Test artifacts: $stage"
