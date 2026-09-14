param(
    [string]$Version,
    [ValidateSet('win-x64','win-arm64','win-x86')][string]$Runtime='win-x64',
    [string]$Dotnet='dotnet',
    [string]$OutputMetadataPath,
    [string]$OutputDirectory,
    [string]$MakeNsis
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Version) { [xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'LaunchPad.csproj'); $Version=$project.Project.PropertyGroup.Version }
$parsed=[Version]::Parse($Version)
$assemblyVersion='{0}.{1}.{2}.{3}' -f $parsed.Major,$parsed.Minor,[Math]::Max(0,$parsed.Build),[Math]::Max(0,$parsed.Revision)
if(!$MakeNsis){
    $command=Get-Command makensis -ErrorAction SilentlyContinue
    if($command){$MakeNsis=$command.Source}
    else{$MakeNsis=Join-Path $projectRoot 'bin/BuildTools/nsis-3.12/makensis.exe'}
}
if(!(Test-Path -LiteralPath $MakeNsis)){throw 'Install NSIS 3 (Unicode) or pass -MakeNsis with the path to makensis.exe.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $projectRoot "bin/releases/v$Version"}
$output=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) "LaunchPad-v$Version-$Runtime-setup.exe"
if(Test-Path -LiteralPath $output){throw 'Installer already exists. Use -OutputDirectory for a new build; published release files must not be overwritten.'}
$work=Join-Path $projectRoot ('bin/InstallerBuild/'+[Guid]::NewGuid().ToString('N'))
$publish=Join-Path $work 'application';$payload=Join-Path $work 'payload';$worker=Join-Path $work 'worker';$bundle=Join-Path $work 'nsis-payload'
[IO.Directory]::CreateDirectory($payload)|Out-Null
$restore='-p:RestoreConfigFile='+ (Join-Path $projectRoot 'Tests/NuGet.Config')
& $Dotnet publish (Join-Path $projectRoot 'LaunchPad.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:DebugType=embedded "-p:Version=$Version" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" $restore -o $publish
if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
function Get-Sha([string]$path){$file=[IO.File]::OpenRead($path);$sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash($file)).Replace('-','').ToLowerInvariant()}finally{$file.Dispose();$sha.Dispose()}}
$files=@()
foreach($item in Get-ChildItem -LiteralPath $publish -Recurse -File){$relative=$item.FullName.Substring($publish.Length+1).Replace('\','/');$files+=@{Path=$relative;Size=$item.Length;Sha256=(Get-Sha $item.FullName)}}
# Retain the original payload format for regression tests; NSIS packs the raw files only.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $payload 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($publish,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
@{Format=1;Version=$assemblyVersion;Runtime=$Runtime;Sha256=(Get-Sha $zip);Files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $payload 'payload.json') -Encoding UTF8
& $Dotnet publish (Join-Path $projectRoot 'Installer/Worker/LaunchPad.Setup.Worker.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:DebugType=embedded $restore -o $worker
if($LASTEXITCODE -ne 0){throw 'Installer worker publish failed.'}
[IO.Directory]::CreateDirectory($bundle)|Out-Null
Get-ChildItem -LiteralPath $publish | Copy-Item -Destination $bundle -Recurse
# Runtime files already exist in the application payload. Copy only worker-specific files.
foreach($name in @('LaunchPad.Setup.Worker.exe','LaunchPad.Setup.Worker.dll','LaunchPad.Setup.Worker.deps.json','LaunchPad.Setup.Worker.runtimeconfig.json')){
    Copy-Item -LiteralPath (Join-Path $worker $name) -Destination $bundle
}
# Ensure the shared runtime is byte-identical to the one expected by the worker.
foreach($name in @('hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll')){
    if((Get-Sha (Join-Path $worker $name)) -ne (Get-Sha (Join-Path $publish $name))){throw "Worker/app runtime mismatch: $name"}
}
Copy-Item -LiteralPath (Join-Path $payload 'payload.json') -Destination $bundle
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))|Out-Null
& $MakeNsis /V2 /INPUTCHARSET UTF8 "-DPROJECT=$projectRoot" "-DPAYLOAD=$bundle" "-DOUTPUT=$output" "-DVERSION=$Version" "-DASSEMBLY_VERSION=$assemblyVersion" (Join-Path $projectRoot 'Installer/NSIS/LaunchPad.nsi')
if($LASTEXITCODE -ne 0){throw 'NSIS compilation failed.'}
((Get-Sha $output)+'  '+[IO.Path]::GetFileName($output))|Set-Content -LiteralPath ($output+'.sha256.txt') -Encoding ascii
Write-Output "Installer: $output"
Write-Output "Installer size: $([Math]::Round((Get-Item -LiteralPath $output).Length/1MB,2)) MiB"
Write-Output "Published app for incremental assets: $publish"
if($OutputMetadataPath){@{Installer=$output;Application=$publish;Payload=$payload;Bundle=$bundle;Version=$assemblyVersion;Engine='NSIS'}|ConvertTo-Json|Set-Content -LiteralPath $OutputMetadataPath -Encoding UTF8}
