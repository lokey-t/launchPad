param(
    [string]$Version,
    [ValidateSet('win-x64','win-arm64','win-x86')][string]$Runtime='win-x64',
    [string]$Dotnet='dotnet',
    [string]$OutputMetadataPath
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Version) { [xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'LaunchPad.csproj'); $Version=$project.Project.PropertyGroup.Version }
$parsed=[Version]::Parse($Version)
$assemblyVersion='{0}.{1}.{2}.{3}' -f $parsed.Major,$parsed.Minor,[Math]::Max(0,$parsed.Build),[Math]::Max(0,$parsed.Revision)
$work=Join-Path $projectRoot ('bin\InstallerBuild\'+[Guid]::NewGuid().ToString('N'))
$publish=Join-Path $work 'application';$payload=Join-Path $work 'payload';$bootstrap=Join-Path $work 'setup'
[IO.Directory]::CreateDirectory($payload)|Out-Null
$restore='-p:RestoreConfigFile='+ (Join-Path $projectRoot 'Tests\NuGet.Config')
& $Dotnet publish (Join-Path $projectRoot 'LaunchPad.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:DebugType=embedded "-p:Version=$Version" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" $restore -o $publish
if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
function Get-Sha([string]$path){$file=[IO.File]::OpenRead($path);$sha=[Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash($file)).Replace('-','').ToLowerInvariant()}finally{$file.Dispose();$sha.Dispose()}}
$files=@()
foreach($item in Get-ChildItem -LiteralPath $publish -Recurse -File){$relative=$item.FullName.Substring($publish.Length+1).Replace('\','/');$files+=@{Path=$relative;Size=$item.Length;Sha256=(Get-Sha $item.FullName)}}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $payload 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($publish,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
@{Format=1;Version=$assemblyVersion;Runtime=$Runtime;Sha256=(Get-Sha $zip);Files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $payload 'payload.json') -Encoding UTF8
& $Dotnet restore (Join-Path $projectRoot 'Installer\LaunchPad.Setup.csproj') -r $Runtime $restore -v quiet
if($LASTEXITCODE -ne 0){throw 'Installer restore failed.'}
& $Dotnet clean (Join-Path $projectRoot 'Installer\LaunchPad.Setup.csproj') -c Release -r $Runtime -v quiet
if($LASTEXITCODE -ne 0){throw 'Installer clean failed.'}
& $Dotnet publish (Join-Path $projectRoot 'Installer\LaunchPad.Setup.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded "-p:PayloadDirectory=$payload" "-p:Version=$Version" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" $restore -o $bootstrap
if($LASTEXITCODE -ne 0){throw 'Installer publish failed.'}
$releaseDirectory=Join-Path $projectRoot "bin\releases\v$Version"
[IO.Directory]::CreateDirectory($releaseDirectory)|Out-Null
$output=Join-Path $releaseDirectory "LaunchPad-v$Version-$Runtime-setup.exe"
Copy-Item -LiteralPath (Join-Path $bootstrap 'LaunchPad.Setup.exe') -Destination $output
((Get-Sha $output)+'  '+[IO.Path]::GetFileName($output))|Set-Content -LiteralPath ($output+'.sha256.txt') -Encoding ascii
Write-Output "Installer: $output"
Write-Output "Published app for incremental assets: $publish"
if($OutputMetadataPath){@{Installer=$output;Application=$publish;Payload=$payload;Version=$assemblyVersion}|ConvertTo-Json|Set-Content -LiteralPath $OutputMetadataPath -Encoding UTF8}
