param([Parameter(Mandatory=$true)][string]$Version,[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($Version -notmatch '^\d+\.\d+(\.\d+){0,2}$'){throw 'Use a numeric version, for example 0.97.'}
[xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'LaunchPad.csproj')
if($project.Project.PropertyGroup.Version -ne $Version){throw 'Update the project version and documentation before building a release.'}
$output=Join-Path $projectRoot "bin/releases/v$Version"
if(Test-Path -LiteralPath $output){throw 'Release output already exists. Inspect it; use a fresh version or move the existing output aside before rebuilding.'}
$work=Join-Path $projectRoot ('bin/ReleaseBuild/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($work)|Out-Null
$metadata=Join-Path $work 'installer.json'
& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -Version $Version -Dotnet $Dotnet -OutputMetadataPath $metadata
$built=Get-Content -LiteralPath $metadata -Raw|ConvertFrom-Json
$small=Join-Path $work 'framework-dependent'
& $Dotnet publish (Join-Path $projectRoot 'LaunchPad.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=embedded "-p:RestoreConfigFile=$projectRoot/Tests/NuGet.Config" -o $small
if($LASTEXITCODE -ne 0){throw 'Framework-dependent publish failed.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($flavor in @(@{Directory=$built.Application;Suffix=''},@{Directory=$small;Suffix='-framework-dependent'})){
    $actual=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $flavor.Directory 'LaunchPad.dll')).Version
    if($actual.ToString() -ne $built.Version){throw 'Published assembly version mismatch.'}
    [IO.Compression.ZipFile]::CreateFromDirectory($flavor.Directory,(Join-Path $output "LaunchPad-v$Version-win-x64$($flavor.Suffix).zip"),[IO.Compression.CompressionLevel]::Optimal,$false)
    $incremental=Join-Path $work ('incremental'+$flavor.Suffix)
    & (Join-Path $PSScriptRoot 'New-UpdateAssets.ps1') -PublishDirectory $flavor.Directory -OutputDirectory $incremental
    foreach($file in Get-ChildItem -LiteralPath $incremental -File){
        $destination=Join-Path $output $file.Name
        if(Test-Path -LiteralPath $destination){if((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw 'Duplicate asset differs.'}}
        else{Copy-Item -LiteralPath $file.FullName -Destination $destination}
    }
}
$checksums=foreach($file in Get-ChildItem -LiteralPath $output -File|Sort-Object Name){((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$file.Name)}
$checksums|Set-Content -LiteralPath (Join-Path $output "LaunchPad-v$Version-sha256.txt") -Encoding ascii
Write-Output "Release assets: $output"
Write-Output "Build metadata: $metadata"
