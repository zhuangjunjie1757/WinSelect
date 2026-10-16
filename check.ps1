$ErrorActionPreference = 'Stop'
if (Get-Process -Name WinSelect -ErrorAction SilentlyContinue) { throw 'Exit WinSelect from its tray menu before running checks.' }
& (Join-Path $PSScriptRoot 'build.ps1')
foreach ($check in @('--verify','--ui-check')) {
    $process = Start-Process -FilePath (Join-Path $PSScriptRoot 'WinSelect.exe') -ArgumentList $check -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw ($check + ' failed. See work/diagnostics/.') }
    Write-Output ($check + ' passed.')
}
Write-Output 'No display output was changed.'
