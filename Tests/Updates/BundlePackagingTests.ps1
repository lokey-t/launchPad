param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$work=Join-Path $root ('obj/bundle-package-test/'+[Guid]::NewGuid().ToString('N'))
$old=Join-Path $work 'old'; $new=Join-Path $work 'new'
New-Item -ItemType Directory -Path $old,$new -Force | Out-Null
Copy-Item (Join-Path $root 'Tests/Updates/bin/Debug/net8.0-windows/Baseline.dll') (Join-Path $old 'LaunchPad.dll')
Copy-Item (Join-Path $root 'Tests/Updates/bin/Debug/net8.0-windows/LaunchPad.dll') (Join-Path $new 'LaunchPad.dll')
foreach($directory in @($old,$new)) {
    [IO.File]::WriteAllText((Join-Path $directory 'LaunchPad.exe'),'unchanged executable fixture')
    [IO.File]::WriteAllText((Join-Path $directory 'runtime.dat'),'unchanged runtime fixture')
}
[IO.File]::WriteAllText((Join-Path $new 'added.dat'),'new file')
$output=Join-Path $work 'assets'
& (Join-Path $root 'tools/New-UpdateAssets.ps1') -PublishDirectory $new -BaseDirectory $old -OutputDirectory $output
$assets=@(Get-ChildItem -LiteralPath $output -File)
if($assets.Count -ne 2 -or @($assets | Where-Object Extension -eq '.gz').Count){throw 'Expected exactly a ZIP and a manifest.'}
$manifest=Get-Content -LiteralPath (Join-Path $output 'LaunchPad-update-bundle-win-x64-framework-dependent.json') -Raw | ConvertFrom-Json
if($manifest.Format -ne 2 -or $manifest.Files.Count -ne 4 -or $manifest.Bundle.Paths.Count -ne 2 -or $manifest.Bundle.Paths -contains 'runtime.dat' -or $manifest.Bundle.Paths -notcontains 'added.dat'){throw 'Incorrect changed-file selection.'}
$zipPath=Join-Path $output $manifest.Bundle.Asset
if((Get-FileHash -LiteralPath $zipPath).Hash -ne $manifest.Bundle.Sha256){throw 'Archive hash mismatch.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
try { if($zip.Entries.Count -ne 2){throw 'ZIP should contain changed and added files only.'} } finally {$zip.Dispose()}
& (Join-Path $root 'tools/New-UpdateAssets.ps1') -PublishDirectory $new -OutputDirectory (Join-Path $work 'no-baseline')
if(@(Get-ChildItem -LiteralPath (Join-Path $work 'no-baseline') -File).Count){throw 'First release must use full packages only.'}
$rejected=$false
try { & (Join-Path $root 'tools/New-UpdateAssets.ps1') -PublishDirectory $new -BaseDirectory $new -OutputDirectory (Join-Path $work 'invalid-baseline') } catch {$rejected=$true}
if(!$rejected){throw 'Same/newer baseline must be rejected.'}
Write-Output 'PASS bundled assets: changed and added files only, two attachments, SHA-256, no-baseline fallback, invalid-baseline rejection.'
