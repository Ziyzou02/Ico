param([switch]$Tests)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw 'Windows .NET Framework compiler was not found.'}
$output=Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$common=@('/nologo','/platform:anycpu','/optimize+','/reference:System.Runtime.Serialization.dll',
    ('/resource:'+(Join-Path $projectRoot 'src\ApplyTemplate.ps1')+',ApplyTemplate'),
    ('/resource:'+(Join-Path $projectRoot 'assets\catalog.json')+',BuiltinCatalog'),
    ('/resource:'+(Join-Path $projectRoot 'assets\julia\julia.ico')+',Builtin.Julia'),
    ('/resource:'+(Join-Path $projectRoot 'assets\typst\typst.ico')+',Builtin.Typst'))
$appArgs=$common+@('/target:winexe','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    ('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')),
    ('/out:'+(Join-Path $output 'IconController.exe')),
    (Join-Path $projectRoot 'src\App.cs'),(Join-Path $projectRoot 'src\Core.cs'))
& $compiler @appArgs
if($LASTEXITCODE -ne 0){throw 'Application build failed.'}
if($Tests){
    $testArgs=$common+@('/target:exe',('/out:'+(Join-Path $output 'IconController.Tests.exe')),
        (Join-Path $projectRoot 'src\Core.cs'),(Join-Path $projectRoot 'tests\Tests.cs'),(Join-Path $projectRoot 'tests\TestProgram.cs'))
    & $compiler @testArgs
    if($LASTEXITCODE -ne 0){throw 'Test build failed.'}
}
Write-Output (Join-Path $output 'IconController.exe')
