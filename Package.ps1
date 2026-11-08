param([string]$Executable='WinBoard.exe')
$ErrorActionPreference='Stop'
$version='1.3.4'
$stage=Join-Path ([IO.Path]::GetTempPath()) ('WinBoard-Package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot $Executable) -Destination (Join-Path $stage 'WinBoard.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ScreenRecorderLib.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'lib/ScreenRecorderLib/LICENSE') -Destination (Join-Path $stage 'ScreenRecorderLib-LICENSE.txt')
foreach($file in @('启动WinBoard.cmd','使用说明.md','README.md','LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage
}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
$archive=Join-Path $PSScriptRoot ("dist/WinBoard-$version-win-x64.zip")
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
# Keep the temporary staging folder available for inspection; no user data is included.
Get-FileHash -LiteralPath $archive -Algorithm SHA256
