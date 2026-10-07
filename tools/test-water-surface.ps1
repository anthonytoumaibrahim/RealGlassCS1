param(
    [ValidateSet('d3d11', 'glcore')][string]$Api = 'd3d11',
    [string]$Unity = 'C:\Program Files\Unity\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\UnityProject')).Path
& (Join-Path $PSScriptRoot 'preview\sync-sim.ps1')
$output = Join-Path $project 'water-surface-test-output.txt'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
$arguments = @('-batchmode', '-quit', "-force-$Api", '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'WaterSurfaceTest.Run', '-logFile', ('"' + (Join-Path $project 'water-surface-test.log') + '"'))
$process = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList $arguments
if (Test-Path -LiteralPath $output) { Get-Content -LiteralPath $output }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output) -or
    -not (Select-String -LiteralPath $output -Pattern '^WATER SURFACE TEST OK$' -Quiet)) {
    throw 'Water surface GPU regression failed; see UnityProject/water-surface-test.log.'
}
