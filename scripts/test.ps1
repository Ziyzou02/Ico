$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Tests
$output=Join-Path $projectRoot ('build\test-run-'+[guid]::NewGuid().ToString('N'))
$runner=Join-Path $projectRoot 'build\IconController.Tests.exe'
& $runner $output
$result=$LASTEXITCODE
$report=Join-Path $output 'results.txt'
if(Test-Path -LiteralPath $report){Copy-Item -LiteralPath $report -Destination (Join-Path $projectRoot 'build\test-results.txt') -Force}
if($result -ne 0){throw "Tests failed; fixtures retained at $output"}
$absolute=[IO.Path]::GetFullPath($output)
$buildRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'build'))+'\'
if(-not $absolute.StartsWith($buildRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe test cleanup path'}
Remove-Item -LiteralPath $absolute -Recurse -Force
