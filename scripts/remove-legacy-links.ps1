param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
# Run from the desktop: agent processes can see an isolated registry view.
if ($env:CODEX_THREAD_ID -or $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY -or [Security.Principal.WindowsIdentity]::GetCurrent().Name -match 'codexsandbox') {
    throw 'Run data\scripts\remove-legacy-links.cmd from Windows File Explorer.'
}
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$links = @()
foreach ($name in @('julia','typst')) {
    $path = Join-Path $projectRoot $name
    $target = Join-Path $projectRoot ('assets\' + $name)
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $entry = Get-Item -LiteralPath $path -Force
    if ($entry.LinkType -ne 'Junction' -or @($entry.Target).Count -ne 1 -or [IO.Path]::GetFullPath([string]@($entry.Target)[0]).TrimEnd('\') -ine $target.TrimEnd('\')) { throw ('Refusing to delete an unexpected directory: ' + $path) }
    $oldIcon = Join-Path $path ($name + '.ico')
    $newIcon = Join-Path $target ($name + '.ico')
    if ((Get-FileHash -LiteralPath $oldIcon).Hash -ne (Get-FileHash -LiteralPath $newIcon).Hash) { throw 'Old and new icon content differs.' }
    $links += [pscustomobject]@{ path=$path; oldIcon=$oldIcon; newIcon=$newIcon }
}
$changes = @()
# Read Explorer's merged class view, but write only current-user overrides.
foreach ($name in [Microsoft.Win32.Registry]::ClassesRoot.GetSubKeyNames()) {
    $relative = $name + '\DefaultIcon'
    $key = [Microsoft.Win32.Registry]::ClassesRoot.OpenSubKey($relative)
    if (-not $key) { continue }
    try { $before = [string]$key.GetValue('', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); $kind = $key.GetValueKind('').ToString() } catch { continue } finally { $key.Dispose() }
    $after = $before
    foreach ($link in $links) { $after = [regex]::Replace($after, [regex]::Escape($link.oldIcon) + '(?="|,|$)', [System.Text.RegularExpressions.MatchEvaluator]{param($m) $link.newIcon}, [Text.RegularExpressions.RegexOptions]::IgnoreCase) }
    if ($before -eq $after) { continue }
    $userPath = 'Software\Classes\' + $relative
    $userKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($userPath)
    try {
        $hadValue = $userKey -and @($userKey.GetValueNames()) -contains ''
        $changes += [pscustomobject]@{ path=$userPath; classPath=$relative; existed=[bool]$hadValue; previous=$(if($hadValue){$userKey.GetValue('',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null}); kind=$kind; value=$after }
    } finally { if($userKey){$userKey.Dispose()} }
}
$changes | Select-Object path,value | Format-Table -AutoSize
if ($CheckOnly) { Write-Output ('CHECK_OK: ' + $changes.Count + ' icon references; ' + $links.Count + ' junctions. No changes made.'); exit 0 }
$backupDir = Join-Path $projectRoot ('data\legacy-backups\link-cleanup-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
@{ changes=$changes; links=$links } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backupDir 'before.json') -Encoding UTF8
foreach ($change in $changes) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($change.path)
    try { $key.SetValue('', $change.value, ([Microsoft.Win32.RegistryValueKind][Enum]::Parse([Microsoft.Win32.RegistryValueKind], $change.kind))) } finally { $key.Dispose() }
}
foreach ($change in $changes) {
    $key = [Microsoft.Win32.Registry]::ClassesRoot.OpenSubKey($change.classPath)
    try { if ($key.GetValue('') -ne $change.value) { throw 'Icon reference verification failed. Junctions retained.' } } finally { $key.Dispose() }
}
foreach ($link in $links) {
    $entry = Get-Item -LiteralPath $link.path -Force
    if ($entry.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]@($entry.Target)[0]).TrimEnd('\') -ine (Split-Path -Parent $link.newIcon).TrimEnd('\')) { throw 'Junction changed during cleanup; stopping.' }
    # Non-recursive deletion removes only the junction, never its target assets.
    [IO.Directory]::Delete($link.path, $false)
}
Add-Type 'using System; using System.Runtime.InteropServices; public static class IconCleanupNotify { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b); }'
[IconCleanupNotify]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
Write-Output 'DONE: legacy junctions removed; assets preserved. Default applications unchanged.'
Write-Output ('Backup: ' + $backupDir)