param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$BaseDirectory,
    [ValidateSet('win-x64','win-arm64','win-x86')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\') + '\'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + '\'
if ($outputRoot.StartsWith($publishRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the publish directory.' }
if (Test-Path -LiteralPath $outputRoot) { throw 'Use a new output directory.' }
if (!(Test-Path -LiteralPath (Join-Path $publishRoot 'LaunchPad.exe'))) { throw 'Publish LaunchPad first.' }
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publishRoot 'LaunchPad.dll')).Version
$flavor = if (Test-Path -LiteralPath (Join-Path $publishRoot 'coreclr.dll')) { 'self-contained' } else { 'framework-dependent' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
if (!$BaseDirectory) { Write-Output 'No baseline supplied: full-package release only (compatible with older updaters).'; return }
$baseRoot = [IO.Path]::GetFullPath($BaseDirectory).TrimEnd('\') + '\'
$baseVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $baseRoot 'LaunchPad.dll')).Version
$baseFlavor = if (Test-Path -LiteralPath (Join-Path $baseRoot 'coreclr.dll')) { 'self-contained' } else { 'framework-dependent' }
if ($baseVersion -ge $version -or $baseFlavor -ne $flavor) { throw 'Baseline must be an older version with the same deployment flavor.' }
function Read-Files([string]$root) {
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force | Sort-Object FullName) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked paths cannot be published.' }
        if ($item.PSIsContainer) { continue }
        $relative = $item.FullName.Substring($root.Length).Replace('\','/')
        if ($relative -in @('README.md','README.en.md','LICENSE','LICENSE.txt')) { continue }
        if ($item.Extension -notin @('.exe','.dll','.json','.pdb','.dat','.bin') -or $relative -match '(^|/)(config.json|ThemeAssets|Backups)($|/)' -or $relative -match '[^a-zA-Z0-9_./-]') { throw "Unexpected publish file: $relative" }
        @{ Path=$relative; Size=$item.Length; Sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$files = @(Read-Files $publishRoot)
$baseFiles = @{}
foreach ($file in @(Read-Files $baseRoot)) { $baseFiles[$file.Path] = $file }
# Retired program files are retained by the existing transactional installer; never remove user data.
$changed = @($files | Where-Object { !$baseFiles.ContainsKey($_.Path) -or $baseFiles[$_.Path].Sha256 -ne $_.Sha256 })
if (!$changed.Count) { throw 'No changed files found.' }
$asset = "LaunchPad-delta-$baseVersion-to-$version-$Runtime-$flavor.zip"
$destination = Join-Path $outputRoot $asset
$zip = [IO.Compression.ZipFile]::Open($destination,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $changed) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $publishRoot $file.Path),$file.Path,[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
} finally { $zip.Dispose() }
@{ Format=2; Version=$version.ToString(); BaseVersion=$baseVersion.ToString(); Runtime=$Runtime; Flavor=$flavor; Files=$files;
   Bundle=@{Asset=$asset; Size=(Get-Item -LiteralPath $destination).Length; Sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant(); Paths=@($changed | ForEach-Object {$_.Path})}
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot "LaunchPad-update-bundle-$Runtime-$flavor.json") -Encoding UTF8
Write-Output "Created one delta ZIP with $($changed.Count) changed files out of $($files.Count), plus one manifest."
