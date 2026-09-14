param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$language=if([Globalization.CultureInfo]::CurrentUICulture.Name.StartsWith('zh')){'zh'}else{'en'}
$theme='Light'
try {
    $config=Get-Content -LiteralPath (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'LaunchPad/config.json') -Raw|ConvertFrom-Json
    if($config.Language -eq 'en-US'){$language='en'}elseif($config.Language -eq 'zh-CN'){$language='zh'}
    if($config.Theme -eq 'Dark'){$theme='Dark'}
} catch {}
$runningDirectory=''
foreach($appProcess in Get-Process -Name LaunchPad -ErrorAction SilentlyContinue){
    try {
        $file=$appProcess.MainModule.FileName
        if([Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductName -eq 'LaunchPad'){$runningDirectory=[IO.Path]::GetDirectoryName($file);break}
    } catch {}
}
[IO.File]::WriteAllLines($OutputPath,@('[Preferences]',"Language=$language","Theme=$theme","RunningDirectory=$runningDirectory"),[Text.Encoding]::Unicode)
