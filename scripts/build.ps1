param()
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw 'Windows .NET Framework compiler was not found.'}
$output=$projectRoot
$common=@('/nologo','/platform:anycpu','/optimize+','/reference:System.Runtime.Serialization.dll',
    ('/resource:'+(Join-Path $projectRoot 'src\ApplyTemplate.ps1')+',ApplyTemplate'),
    ('/resource:'+(Join-Path $projectRoot 'assets\catalog.json')+',BuiltinCatalog'),
    ('/resource:'+(Join-Path $projectRoot 'assets\julia\julia.ico')+',Builtin.Julia'),
    ('/resource:'+(Join-Path $projectRoot 'assets\typst\typst.ico')+',Builtin.Typst'))
$appArgs=$common+@('/target:winexe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    ('/win32icon:'+(Join-Path $projectRoot 'assets\app\icon-controller.ico')),
    ('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')),
    ('/out:'+(Join-Path $output 'IconController.exe')),
    (Join-Path $projectRoot 'src\App.cs'),(Join-Path $projectRoot 'src\Core.cs'))
& $compiler @appArgs
if($LASTEXITCODE -ne 0){throw 'Application build failed.'}
Write-Output (Join-Path $output 'IconController.exe')
