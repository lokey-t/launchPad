param([Parameter(Mandatory=$true)][string]$Installer,[Parameter(Mandatory=$true)][string]$Manifest)
$ErrorActionPreference='Stop'
$installerPath=[IO.Path]::GetFullPath($Installer)
$target=Join-Path ([IO.Path]::GetTempPath()) ('LaunchPad-NSISTest-'+[Guid]::NewGuid().ToString('N')+' 测试目录')
$manifestData=Get-Content -LiteralPath $Manifest -Raw|ConvertFrom-Json
$config=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'LaunchPad/config.json'
$before=if(Test-Path -LiteralPath $config){(Get-FileHash -LiteralPath $config).Hash}else{''}
$registration=(Get-ItemProperty -LiteralPath 'HKCU:\Software\LaunchPad\Installation' -ErrorAction SilentlyContinue|Select-Object InstallLocation,Version|ConvertTo-Json -Compress)
function Run-Setup {
    $start=[Diagnostics.ProcessStartInfo]::new($installerPath)
    $start.Arguments='/S /TEST /D='+$target # NSIS requires /D last, with its path unquoted.
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle='Hidden'
    $process=[Diagnostics.Process]::Start($start)
    if(!$process.WaitForExit(120000)){throw 'Installer timed out.'}
    return $process.ExitCode
}
function Assert($condition,[string]$label){if(!$condition){throw $label};Write-Output "PASS $label"}
Assert ((Run-Setup) -eq 0) 'NSIS fresh install with a Unicode/space path'
foreach($file in $manifestData.Files){if((Get-FileHash -LiteralPath (Join-Path $target $file.Path)).Hash -ne $file.Sha256){throw "File hash mismatch: $($file.Path)"}}
Write-Output "PASS all $($manifestData.Files.Count) application file hashes"
Assert (!(Test-Path -LiteralPath (Join-Path $target 'LaunchPad.Setup.Worker.exe'))) 'installer worker is not installed with the app'
[IO.File]::WriteAllText((Join-Path $target 'user-note.txt'),'keep me')
[IO.File]::WriteAllText((Join-Path $target 'config.json'),'keep configuration')
[IO.File]::WriteAllText((Join-Path $target 'System.Threading.Timer.dll'),'old file')
Assert ((Run-Setup) -eq 0) 'NSIS update/repair succeeds'
Assert ([IO.File]::ReadAllText((Join-Path $target 'user-note.txt')) -eq 'keep me') 'unrelated files preserved'
Assert ([IO.File]::ReadAllText((Join-Path $target 'config.json')) -eq 'keep configuration') 'installation directory configuration preserved'
[IO.File]::WriteAllText((Join-Path $target 'coreclr.dll'),'rollback sentinel')
$locked=[IO.File]::Open((Join-Path $target 'WindowsBase.dll'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try{Assert ((Run-Setup) -ne 0) 'locked file returns installation failure'}finally{$locked.Dispose()}
Assert ([IO.File]::ReadAllText((Join-Path $target 'coreclr.dll')) -eq 'rollback sentinel') 'NSIS failure rolls back earlier replacements'
Assert ((Run-Setup) -eq 0) 'retry after closing the locked file succeeds'
$after=if(Test-Path -LiteralPath $config){(Get-FileHash -LiteralPath $config).Hash}else{''}
Assert ($before -eq $after) 'real user configuration unchanged'
Assert ($registration -eq (Get-ItemProperty -LiteralPath 'HKCU:\Software\LaunchPad\Installation' -ErrorAction SilentlyContinue|Select-Object InstallLocation,Version|ConvertTo-Json -Compress)) 'test installation does not register shortcuts or installation'
Write-Output "Isolated installation: $target"
