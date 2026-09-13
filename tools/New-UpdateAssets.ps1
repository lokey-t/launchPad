param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('win-x64','win-arm64','win-x86')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\') + '\'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + '\'
if ($outputRoot.StartsWith($publishRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the publish directory.' }
if (Test-Path -LiteralPath $outputRoot) { throw 'Use a new output directory to avoid including stale release assets.' }
$assemblyPath = Join-Path $publishRoot 'LaunchPad.dll'
if (!(Test-Path -LiteralPath (Join-Path $publishRoot 'LaunchPad.exe'))) { throw 'Publish LaunchPad first.' }
$version = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version.ToString()
$flavor = if (Test-Path -LiteralPath (Join-Path $publishRoot 'coreclr.dll')) { 'self-contained' } else { 'framework-dependent' }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$files = @()
foreach ($item in Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Sort-Object FullName) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked files cannot be published.' }
    $relative = $item.FullName.Substring($publishRoot.Length).Replace('\','/')
    if ($relative -in @('README.md','README.en.md','LICENSE','LICENSE.txt')) { continue }
    if ($item.Extension -notin @('.exe','.dll','.json','.pdb','.dat','.bin') -or $relative -match '(^|/)(config.json|ThemeAssets|Backups)($|/)') { throw "Unexpected publish file: $relative" }
    $hashStream = [IO.File]::OpenRead($item.FullName)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash($hashStream)).Replace('-','').ToLowerInvariant() }
    finally { $hashStream.Dispose(); $sha.Dispose() }
    $asset = 'lp-' + $hash + '.gz'
    $destination = Join-Path $outputRoot $asset
    if (!(Test-Path -LiteralPath $destination)) {
        $inputStream = [IO.File]::OpenRead($item.FullName)
        $outputStream = [IO.File]::Create($destination)
        $gzip = [IO.Compression.GZipStream]::new($outputStream,[IO.Compression.CompressionLevel]::Optimal)
        try { $inputStream.CopyTo($gzip) } finally { $gzip.Dispose(); $outputStream.Dispose(); $inputStream.Dispose() }
    }
    $files += @{ Path=$relative; Size=$item.Length; Sha256=$hash; Asset=$asset }
}
@{ Format=1; Version=$version; Runtime=$Runtime; Flavor=$flavor; Files=$files } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $outputRoot "LaunchPad-update-$Runtime-$flavor.json") -Encoding UTF8
Write-Output "Created $($files.Count) incremental file assets for version $version ($Runtime, $flavor). Upload every file to the matching release on both hosts."
