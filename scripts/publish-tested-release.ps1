# Publish only artifacts whose installer and application hashes passed this run.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content (Join-Path $root 'src/AccessibleOCRStudio/AccessibleOCRStudio.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$bundle = Join-Path $root '.release-build'
$proof = Get-Content "$bundle/artifacts/tested-build.json" -Raw | ConvertFrom-Json
if ($proof.commit -ne $env:GITHUB_SHA -or $proof.version -ne $version) { throw 'Test evidence does not match this source revision.' }
$installer = Join-Path $bundle "artifacts/TextBridge-Setup-$version.exe"
$dll = Join-Path $bundle 'publish/TextBridge.dll'
if ((Get-FileHash $installer).Hash -ne $proof.installerSha256 -or (Get-FileHash $dll).Hash -ne $proof.applicationSha256) { throw 'Downloaded artifacts differ from the tested build.' }
$out = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item $installer "$out/TextBridge-Setup-$version.exe"
Copy-Item $installer "$out/ClearRead-Setup-$version.exe"
./scripts/package-source.ps1
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portable = "$out/TextBridge-$version-win-x64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory("$bundle/publish",$portable,[IO.Compression.CompressionLevel]::Optimal,$false)
$assets = @("TextBridge-Setup-$version.exe","ClearRead-Setup-$version.exe","TextBridge-Source-$version.zip","TextBridge-$version-win-x64.zip")
$records = foreach ($name in $assets) { $file=Get-Item "$out/$name"; [ordered]@{file=$name;bytes=$file.Length;sha256=(Get-FileHash $file.FullName).Hash.ToLowerInvariant()} }
$manifest = [ordered]@{product='TextBridge';version=$version;commit=$env:GITHUB_SHA;readyForPublicRelease=$true;verificationRun="https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$($proof.runId)";assets=@($records)}
$manifest | ConvertTo-Json -Depth 6 | Set-Content "$out/release-manifest-$version.json" -Encoding utf8
$assets += "release-manifest-$version.json"
$lines = foreach ($name in $assets) { "$((Get-FileHash "$out/$name").Hash.ToLowerInvariant())  $name" }
$lines | Set-Content "$out/SHA256SUMS.txt" -Encoding utf8
$assets += 'SHA256SUMS.txt'
$notes = "$out/release-notes.txt"
$content = Get-Content "$root/docs/RELEASE-NOTES-$version.md" -Raw
$content += "`n`nVerified source: $env:GITHUB_SHA`n`nWindows verification: $($manifest.verificationRun)`n"
$content | Set-Content $notes -Encoding utf8
# Never replace an existing public release or its assets.
$existing = & gh release view "v$version" --repo $env:GITHUB_REPOSITORY --json isDraft 2>$null
if ($LASTEXITCODE -eq 0) { throw 'A release with this version already exists; inspect it before retrying.' }
$arguments = @('release','create',"v$version",'--repo',$env:GITHUB_REPOSITORY,'--target',$env:GITHUB_SHA,'--title',"TextBridge $version — installation and accessibility fixes",'--notes-file',$notes,'--draft')
$arguments += @($assets | ForEach-Object { "$out/$_" })
& gh @arguments
if ($LASTEXITCODE -ne 0) { throw 'Draft release creation or upload failed.' }
$uploaded = (& gh api "repos/$env:GITHUB_REPOSITORY/releases/tags/v$version" | ConvertFrom-Json).assets
if ($LASTEXITCODE -ne 0) { throw 'Draft asset query failed.' }
foreach ($name in $assets) {
    $asset = @($uploaded | Where-Object name -eq $name)
    if ($asset.Count -ne 1 -or $asset[0].digest -ne ('sha256:' + (Get-FileHash "$out/$name").Hash.ToLowerInvariant())) { throw "Draft digest mismatch: $name" }
}
& gh release edit "v$version" --repo $env:GITHUB_REPOSITORY --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed.' }
# Anonymous discovery and a full installer download must match the tested bytes.
$verified = $false
for ($attempt=0; $attempt -lt 6; $attempt++) {
    try {
        $latest = Invoke-RestMethod "https://api.github.com/repos/$env:GITHUB_REPOSITORY/releases/latest" -Headers @{'User-Agent'='TextBridge-verification'}
        if ($latest.tag_name -ne "v$version") { throw 'Latest feed is not updated yet.' }
        $asset = @($latest.assets | Where-Object name -eq "TextBridge-Setup-$version.exe")
        if ($asset.Count -ne 1 -or $asset[0].digest -ne ('sha256:' + $proof.installerSha256.ToLowerInvariant())) { throw 'The update feed digest differs from the tested installer.' }
        $download = Join-Path $env:RUNNER_TEMP 'textbridge-public-verification.exe'
        Invoke-WebRequest $asset[0].browser_download_url -OutFile $download
        if ((Get-FileHash $download).Hash -ne $proof.installerSha256) { throw 'Public installer download differs from tested bytes.' }
        $verified = $true
        break
    } catch { if ($attempt -eq 5) { throw }; Start-Sleep -Seconds 10 }
}
if (-not $verified) { throw 'Public update-feed verification failed.' }
Write-Output "PASS: Published $version; anonymous latest-release discovery and complete installer SHA-256 match the tested build."
