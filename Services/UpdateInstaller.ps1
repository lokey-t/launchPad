param([Parameter(Mandatory=$true)][string]$JobPath)
$ErrorActionPreference = 'Stop'
$job = Get-Content -LiteralPath $JobPath -Raw -Encoding UTF8 | ConvertFrom-Json
$stageRoot = [IO.Path]::GetFullPath($job.Stage).TrimEnd('\') + '\'
$installRoot = [IO.Path]::GetFullPath($job.Root).TrimEnd('\') + '\'
$history = [Collections.Generic.List[object]]::new()
$success = $false
$mayRestart = $false
function File-Hash([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Checked-Path([string]$root, [string]$relative) {
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('\') -or $relative -match '(^|/)(\.|\.\.)($|/)' -or $relative -match '[:*?"<>|]') { throw 'Invalid update path' }
    $target = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $relative))
    if (!$target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escapes root' }
    $ancestor = $target
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked update path' }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $target
}
function Install-File([string]$source, [string]$destination) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
    $temporary = $destination + '.qdtnew-' + [Guid]::NewGuid().ToString('N')
    try {
        [IO.File]::Copy($source, $temporary, $false)
        if ([IO.File]::Exists($destination)) { [IO.File]::Replace($temporary, $destination, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $destination) }
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
try {
    if ($stageRoot -eq $installRoot) { throw 'Invalid staging directory' }
    # Verify every byte again before requesting application shutdown.
    foreach ($file in $job.Files) {
        $source = Checked-Path $stageRoot $file.Path
        $null = Checked-Path $installRoot $file.Path
        if ((Get-Item -LiteralPath $source).Length -ne $file.Size -or (File-Hash $source) -ne $file.Sha256) { throw 'Update checksum mismatch' }
    }
    $parent = Get-Process -Id $job.Pid -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText((Join-Path $stageRoot 'ready'), 'ready')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath (Join-Path $stageRoot 'go'))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Update handoff timed out' }
        Start-Sleep -Milliseconds 100
    }
    if ($parent -and !$parent.WaitForExit(120000)) { throw 'Application did not exit' }
    $mayRestart = $true
    # Keep all rollback copies before replacing any file.
    foreach ($file in $job.Files) {
        $target = Checked-Path $installRoot $file.Path
        $backup = Checked-Path ($stageRoot + 'rollback\') $file.Path
        $existed = [IO.File]::Exists($target)
        if ($existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
            [IO.File]::Copy($target, $backup, $false)
        }
        $history.Add(@{ Target=$target; Backup=$backup; Existed=$existed; Applied=$false; Relative=$file.Path })
    }
    for ($i=0; $i -lt $job.Files.Count; $i++) {
        $file = $job.Files[$i]
        $target = Checked-Path $installRoot $file.Path
        $history[$i].Applied = $true
        Install-File (Checked-Path $stageRoot $file.Path) $target
        if ((File-Hash $target) -ne $file.Sha256) { throw 'Installed checksum mismatch' }
    }
    $success = $true
    @{ Success=$true; Message=''; Stage=$job.Stage } | ConvertTo-Json | Set-Content -LiteralPath $job.Result -Encoding UTF8
} catch {
    $failure = $_.Exception.Message
    $rollbackOk = $true
    for ($i=$history.Count-1; $i -ge 0; $i--) {
        $entry = $history[$i]
        if (!$entry.Applied) { continue }
        try {
            $null = Checked-Path $installRoot $entry.Relative
            if ($entry.Existed) {
                # A failed atomic replace may have left the original untouched (for example, a locked file).
                if (![IO.File]::Exists($entry.Target) -or (File-Hash $entry.Target) -ne (File-Hash $entry.Backup)) { Install-File $entry.Backup $entry.Target }
            }
            elseif ([IO.File]::Exists($entry.Target)) { [IO.File]::Delete($entry.Target) }
        } catch { $rollbackOk = $false; $failure += '; rollback: ' + $_.Exception.Message }
    }
    if (!$rollbackOk) { $mayRestart = $false }
    @{ Success=$false; Message=$failure; Stage=$job.Stage; RollbackOk=$rollbackOk } | ConvertTo-Json | Set-Content -LiteralPath $job.Result -Encoding UTF8
} finally {
    if ($mayRestart -and $job.Restart) {
        try { Start-Process -FilePath (Join-Path $installRoot 'LaunchPad.exe') -WorkingDirectory $installRoot -WindowStyle Hidden }
        catch { $_.Exception.Message | Set-Content -LiteralPath (Join-Path $stageRoot 'restart-error.txt') -Encoding UTF8 }
    }
}
