param([switch]$TestInstall, [switch]$SelectiveInstall)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Compiler = Join-Path $Root '.tools\inno\ISCC.exe'
if (-not (Test-Path $Compiler)) {
    $Command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $KnownCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (-not $Command -and (Test-Path -LiteralPath $KnownCompiler)) { $Command = Get-Item -LiteralPath $KnownCompiler | Select-Object @{Name='Source';Expression={$_.FullName}} }
    if (-not $Command) { throw 'Inno Setup compiler is missing. Install Inno Setup 6 or prepare the portable compiler in .tools\inno.' }
    $Compiler = $Command.Source
}
 if (-not (Test-Path (Join-Path $Root 'publish\TextBridge.exe'))) { throw 'Build the app with build-windows.ps1 first.' }
$Arguments = @('/Qp')
if ($TestInstall -or $SelectiveInstall) { $Arguments += '/DTestInstall' }
if ($SelectiveInstall) { $Arguments += '/DSelectiveInstall' }
$Arguments += Join-Path $Root 'installer\AccessibleOCRStudio.iss'
& $Compiler @Arguments
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
