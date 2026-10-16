$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.8 is required.' }
$workFolder = Join-Path $projectRoot 'work'
New-Item -ItemType Directory -Path $workFolder -Force | Out-Null
$iconBuilder = Join-Path $workFolder 'IconBuilder.exe'
& $compiler /nologo /target:exe ('/out:'+$iconBuilder) ('/reference:'+(Join-Path $frameworkRoot 'System.Drawing.dll')) (Join-Path $projectRoot 'tools/IconBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon builder compilation failed.' }
& $iconBuilder $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Xaml.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll')
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001',('/out:'+(Join-Path $projectRoot 'WinSelect.exe')),('/win32manifest:'+(Join-Path $projectRoot 'app.manifest')),('/win32icon:'+(Join-Path $projectRoot 'WinSelect.ico')))
foreach ($reference in $refs) { $arguments += '/reference:' + (Join-Path $frameworkRoot $reference) }
$arguments += (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs').FullName
$arguments += '/resource:' + (Join-Path $projectRoot 'WinSelect.ico') + ',WinSelect.Icon'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output ('Built '+(Join-Path $projectRoot 'WinSelect.exe'))
