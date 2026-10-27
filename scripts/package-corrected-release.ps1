$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root 'artifacts'
$Build = '1.1.0.1'
$ManifestPath = Join-Path $Artifacts "release-manifest-$Build.json"
if ((Test-Path $ManifestPath) -and (Get-Content $ManifestPath -Raw | ConvertFrom-Json).published) { throw 'This corrected update build is already published.' }
$Log = Get-Content (Join-Path $Artifacts 'editable-pages-final-tests.log') -Raw
if ($Log -notmatch 'All 397 checks passed\.') { throw 'The corrected-build functional test evidence is missing.' }
$Installer = Join-Path $Artifacts 'TextBridge-Setup-1.1.0.exe'
if ((Get-Item $Installer).VersionInfo.FileVersion.Trim() -ne $Build) { throw 'Incorrect installer build.' }
foreach ($Name in @("TextBridge-Setup-$Build.exe", "ClearRead-Setup-$Build.exe")) { Copy-Item -LiteralPath $Installer -Destination (Join-Path $Artifacts $Name) -Force }
& (Join-Path $PSScriptRoot 'package-source.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Portable = Join-Path $Artifacts 'TextBridge-1.1.0-win-x64.zip'
if (Test-Path $Portable) { Remove-Item -LiteralPath $Portable -Force }
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $Root 'publish'), $Portable, [IO.Compression.CompressionLevel]::Optimal, $false)
$Names = @('TextBridge-Setup-1.1.0.exe', "TextBridge-Setup-$Build.exe", "ClearRead-Setup-$Build.exe", 'TextBridge-Source-1.1.0.zip', 'TextBridge-1.1.0-win-x64.zip')
$Assets = foreach ($Name in $Names) {
    $File = Get-Item -LiteralPath (Join-Path $Artifacts $Name)
    [ordered]@{ file=$Name; bytes=$File.Length; sha256=(Get-FileHash -LiteralPath $File.FullName).Hash.ToLowerInvariant() }
}
[ordered]@{ product='TextBridge'; version=$Build; displayVersion='1.1.0'; published=$false; readyForPublicRelease=$false; functionalChecksPassed=397; assets=@($Assets) } |
    ConvertTo-Json -Depth 8 | Set-Content $ManifestPath -Encoding utf8
$Sums = foreach ($Asset in $Assets) { "$($Asset.sha256)  $($Asset.file)" }
$Sums += "$((Get-FileHash $ManifestPath).Hash.ToLowerInvariant())  release-manifest-$Build.json"
$Sums | Set-Content (Join-Path $Artifacts 'SHA256SUMS.txt') -Encoding utf8
Write-Host 'Corrected build packaged. Installer QA and exact-commit CI must pass before publication.'
