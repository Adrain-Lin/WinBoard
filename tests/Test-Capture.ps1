param([string]$Executable=(Join-Path $PSScriptRoot '../WinBoard.exe'))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$stage=Join-Path ([IO.Path]::GetTempPath()) ('WinBoard-CaptureTest-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$assemblyName=[Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $Executable)).Name+'.exe'
Copy-Item -LiteralPath $Executable -Destination (Join-Path $stage $assemblyName)
Copy-Item -LiteralPath (Join-Path $repo 'lib/ScreenRecorderLib/ScreenRecorderLib.dll') -Destination $stage
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.Drawing.dll','System.Xaml.dll','WPF/PresentationFramework.dll','WPF/PresentationCore.dll','WPF/WindowsBase.dll')
$argsList=@('/nologo','/target:winexe','/platform:x64',('/out:'+(Join-Path $stage 'CaptureSmoke.exe')),('/reference:'+(Join-Path $stage $assemblyName)))
foreach($ref in $refs){$argsList+=('/reference:'+(Join-Path $framework $ref))}
$argsList+=(Join-Path $PSScriptRoot 'CaptureSmoke.cs')
& (Join-Path $framework 'csc.exe') @argsList
if($LASTEXITCODE -ne 0){throw 'Smoke test compilation failed'}
$probe=Start-Process -FilePath (Join-Path $stage 'CaptureSmoke.exe') -WorkingDirectory $stage -PassThru
if(!$probe.WaitForExit(35000)){throw "Capture test did not complete. Inspect $stage and process $($probe.Id)."}
if($probe.ExitCode -ne 0 -or (Test-Path (Join-Path $stage 'error.txt'))){throw "Capture test failed; inspect $stage"}
Get-Content -LiteralPath (Join-Path $stage 'results/pass.txt')
"Test artifacts: $stage"
