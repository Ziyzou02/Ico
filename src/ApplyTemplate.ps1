param([switch]$ValidateOnly, [switch]$Restore)
$ErrorActionPreference = 'Stop'
$config = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('@@PAYLOAD@@')) | ConvertFrom-Json
$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Json($path, $value) {
    $temp = $path + '.tmp'
    [IO.File]::WriteAllText($temp, ($value | ConvertTo-Json -Depth 12), $utf8)
    Move-Item -LiteralPath $temp -Destination $path -Force
}
function Read-Value($path, $name = '') {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
    try {
        $exists = $key -and @($key.GetValueNames()) -contains $name
        $value = $null; $kind = 'String'
        if ($exists) { $value = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); $kind = $key.GetValueKind($name).ToString() }
        return [pscustomobject]@{ path=$path; name=$name; keyExisted=($null -ne $key); existed=[bool]$exists; value=$value; kind=$kind }
    } finally { if ($key) { $key.Dispose() } }
}
function Set-Value($path, $value, $name = '', $kind = 'String') {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path)
    try { $key.SetValue($name, $value, [Microsoft.Win32.RegistryValueKind]::$kind) } finally { $key.Dispose() }
}
function Restore-Values($entries) {
    foreach ($entry in $entries) {
        if ($entry.existed) { Set-Value $entry.path $entry.value $entry.name $entry.kind }
        else {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry.path, $true)
            if ($key) { try { $key.DeleteValue($entry.name, $false) } finally { $key.Dispose() } }
        }
    }
    # Remove only newly-created, empty registry keys; never delete an extension tree.
    foreach ($entry in ($entries | Sort-Object { $_.path.Length } -Descending)) {
        if (-not $entry.keyExisted) {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($entry.path)
            if ($key) {
                try { $empty = $key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0 } finally { $key.Dispose() }
                if ($empty) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($entry.path, $false) }
            }
        }
    }
}
function Get-Choice($base) {
    foreach ($suffix in @('UserChoiceLatest\ProgId', 'UserChoice')) {
        $entry = Read-Value ($base + '\' + $suffix) 'ProgId'
        if ($entry.existed -and $entry.value) { return [string]$entry.value }
    }
    return ''
}
function Clear-Choice($base) {
    foreach ($suffix in @('UserChoiceLatest', 'UserChoice')) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree(($base + '\' + $suffix), $false)
    }
}
function Receipt($status, $message, $backup) {
    Write-Json (Join-Path $PSScriptRoot 'receipt.json') ([ordered]@{ revisionId=$config.revisionId; status=$status; completedAt=[DateTime]::UtcNow.ToString('o'); message=$message; backupFolder=$backup })
}
function Notify-Shell {
    if (-not ('IconControllerNotify' -as [type])) {
        Add-Type 'using System; using System.Runtime.InteropServices; public static class IconControllerNotify { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b); }'
    }
    [IconControllerNotify]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
}
function Check-Config {
    if ($config.extension -notmatch '^\.[a-z0-9][a-z0-9_+-]{0,31}$' -or $config.extension -in @('.exe','.com','.lnk','.dll','.sys','.cpl','.scr')) { throw 'Unsupported extension.' }
    if (-not [IO.Path]::IsPathRooted($config.application) -or [IO.Path]::GetExtension($config.application) -ine '.exe' -or $config.application -match '["\r\n%]') { throw 'Invalid application path.' }
    if (-not (Test-Path -LiteralPath $config.application -PathType Leaf)) { throw ('Application not found: ' + $config.application) }
    if ($config.bundledIcon) { $script:sourceIcon = Join-Path $PSScriptRoot 'icon.ico' }
    else { $script:sourceIcon = $config.iconPath }
    if (-not [IO.Path]::IsPathRooted($sourceIcon) -or [IO.Path]::GetExtension($sourceIcon) -notin @('.ico','.exe','.dll') -or $sourceIcon -match '["\r\n%]') { throw 'Invalid icon path.' }
    if (-not (Test-Path -LiteralPath $sourceIcon -PathType Leaf)) { throw ('Icon not found: ' + $sourceIcon) }
    if ($config.iconIndex -lt -100000 -or $config.iconIndex -gt 100000) { throw 'Invalid icon index.' }
}
if ($ValidateOnly) { Check-Config; Write-Output 'VALIDATION_OK: no registry changes made.'; exit 0 }
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY -or $env:CODEX_THREAD_ID -or [Security.Principal.WindowsIdentity]::GetCurrent().Name -match 'codexsandbox') {
    Write-Error 'Run apply.cmd from Windows File Explorer. Agent commands use an isolated registry.'
    exit 1
}
$extPath = 'Software\Classes\' + $config.extension
$typeId = 'IconController.' + $config.extension.Substring(1)
$typePath = 'Software\Classes\' + $typeId
$choicePath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\' + $config.extension
$statePath = Join-Path $PSScriptRoot 'state.json'
$backup = ''
$state = $null
$mutationStarted = $false
try {
    if ($Restore) {
        if (-not (Test-Path -LiteralPath $statePath)) { throw 'No applied configuration to restore.' }
        $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($state.revisionId -ne $config.revisionId -or $state.typeId -ne $typeId) { throw 'Backup does not match this script.' }
        if ($state.restored) { Write-Output 'Already restored.'; exit 0 }
        $currentChoice = Get-Choice $choicePath
        if ((Read-Value $extPath).value -ne $typeId -or (Read-Value ($typePath + '\DefaultIcon')).value -ne $state.installedIcon -or
            (Read-Value ($typePath + '\shell\open\command')).value -ne $state.installedCommand -or
            ($currentChoice -and $currentChoice -ne $typeId)) {
            throw 'This extension was changed after this script ran. Use the latest backup; no changes made.'
        }
        # The old hashed UserChoice cannot be safely recreated. Restore its effective type as the extension default.
        if ($currentChoice -eq $typeId) { Clear-Choice $choicePath }
        Restore-Values $state.entries
        if ($state.previousChoice) { Set-Value $extPath $state.previousChoice }
        $state.restored = $true
        Write-Json $statePath $state
        Notify-Shell
        Receipt 'restored' 'Previous association restored. Verify in File Explorer.' $state.backupFolder
        Write-Output 'Restored. Check the extension in File Explorer.'
        exit 0
    }
    Check-Config
    $effectiveIcon = $sourceIcon
    if ($config.bundledIcon) {
        $hash = (Get-FileHash -LiteralPath $sourceIcon -Algorithm SHA256).Hash.ToLowerInvariant()
        $iconDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'IconController\Icons'
        New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
        $effectiveIcon = Join-Path $iconDir ($hash + '.ico')
        if (-not (Test-Path -LiteralPath $effectiveIcon)) { Copy-Item -LiteralPath $sourceIcon -Destination $effectiveIcon }
    }
    $iconValue = '"' + $effectiveIcon + '",' + $config.iconIndex
    $command = '"' + $config.application + '" "%1"'
    $previousChoice = Get-Choice $choicePath
    if (Test-Path -LiteralPath $statePath) {
        $previous = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $previous.restored -and (Read-Value $extPath).value -eq $typeId -and
            (Read-Value ($typePath + '\DefaultIcon')).value -eq $iconValue -and
            (Read-Value ($typePath + '\shell\open\command')).value -eq $command -and
            (-not $previousChoice -or $previousChoice -eq $typeId)) {
            Notify-Shell; Receipt 'applied' 'Already applied.' $previous.backupFolder; Write-Output 'Already applied; original backup preserved.'; exit 0
        }
    }
    $backup = Join-Path $PSScriptRoot ('backups\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $paths = @($extPath, $typePath, $choicePath)
    $i = 0
    foreach ($path in $paths) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
        if ($key) {
            $key.Dispose()
            & reg.exe export ('HKCU\' + $path) (Join-Path $backup ($i.ToString() + '.reg')) /y | Out-Null
            if ($LASTEXITCODE -ne 0) { throw ('Backup failed: ' + $path) }
        }
        $i++
    }
    $writes = @(
        [pscustomobject]@{path=$typePath; value=($config.extension.Substring(1).ToUpperInvariant() + ' file')}
        [pscustomobject]@{path=($typePath + '\DefaultIcon'); value=$iconValue}
        [pscustomobject]@{path=($typePath + '\shell\open\command'); value=$command}
        [pscustomobject]@{path=$extPath; value=$typeId}
        [pscustomobject]@{path=($extPath + '\DefaultIcon'); value=$iconValue}
    )
    $entries = @($writes | ForEach-Object { Read-Value $_.path })
    $state = [ordered]@{ revisionId=$config.revisionId; typeId=$typeId; previousChoice=$previousChoice; entries=$entries; installedIcon=$iconValue; installedCommand=$command; backupFolder=$backup; restored=$false }
    Write-Json (Join-Path $backup 'state.json') $state
    $mutationStarted = $true
    foreach ($write in $writes) { Set-Value $write.path $write.value }
    if ($previousChoice -and $previousChoice -ne $typeId) { Clear-Choice $choicePath }
    Write-Json $statePath $state
    Notify-Shell
    Receipt 'applied' 'Registry updated. Visual verification is required.' $backup
    Write-Output ('Applied ' + $config.extension)
    Write-Output ('Application: ' + $config.application)
    Write-Output ('Backup: ' + $backup)
    Write-Output 'Check File Explorer. Return to Icon Controller and refresh to see this result.'
} catch {
    $message = $_.Exception.Message
    if ($mutationStarted -and $state) {
        try {
            Restore-Values $state.entries
            if ($state.previousChoice -and -not (Get-Choice $choicePath)) { Set-Value $extPath $state.previousChoice }
            Notify-Shell
            $message += ' Changes rolled back.'
        } catch { $message += (' Rollback needs attention: ' + $_.Exception.Message) }
    }
    try { Receipt 'failed' $message $backup } catch { }
    Write-Host $message -ForegroundColor Red
    exit 1
}
