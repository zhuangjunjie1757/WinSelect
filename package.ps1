$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
& (Join-Path $projectRoot 'build.ps1')

$exe = Join-Path $projectRoot 'WinSelect.exe'
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$version = '{0}.{1}.{2}' -f $versionInfo.FileMajorPart, $versionInfo.FileMinorPart, $versionInfo.FileBuildPart
$outputFolder = Join-Path $projectRoot ('dist/v' + $version)
New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null

$packageFiles = @('WinSelect.exe', 'WinSelect.exe.config', 'README.md', 'LICENSE')
foreach ($name in $packageFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $outputFolder $name) -Force
}

$archive = Join-Path $outputFolder ('WinSelect-v' + $version + '-win-x64.zip')
$archiveFiles = @($packageFiles | ForEach-Object { Join-Path $outputFolder $_ })
Compress-Archive -LiteralPath $archiveFiles -DestinationPath $archive -CompressionLevel Optimal -Force

$releaseFiles = @((Join-Path $outputFolder 'WinSelect.exe'), (Join-Path $outputFolder 'WinSelect.exe.config'), $archive)
$checksums = foreach ($file in $releaseFiles) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($file)
}
$checksums | Set-Content -LiteralPath (Join-Path $outputFolder 'SHA256SUMS.txt') -Encoding Ascii
Write-Output ('Release files: ' + $outputFolder)
