param([string]$Dotnet='dotnet',[switch]$SelfContained)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work=Join-Path $root ('bin/PluginExamples/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($name in @('Calculator','PathTools')){
 $output=Join-Path $work $name
 & $Dotnet publish (Join-Path $root "PluginExamples/$name/$name.csproj") -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() "-p:RestoreConfigFile=$root/Tests/NuGet.Config" -o $output
 if($LASTEXITCODE -ne 0){throw 'Plugin build failed'}
 Copy-Item -LiteralPath (Join-Path $root "PluginExamples/$name/plugin.json") -Destination $output
 [IO.Compression.ZipFile]::CreateFromDirectory($output,(Join-Path $work "$name.qdtplugin"))
}
Write-Output "Plugin packages: $work"
