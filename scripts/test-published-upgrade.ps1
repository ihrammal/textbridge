# Only run on a disposable Windows CI runner: uses the real production AppId.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Published-version upgrade testing requires a disposable Windows runner.' }
$root = Split-Path -Parent $PSScriptRoot
$target = [IO.Path]::GetFullPath((Join-Path $root '.tools\published-upgrade-test'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root '.tools')) + '\'
if (-not $target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path $target)) { throw 'Upgrade test directory must be new and isolated.' }
$subkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A9C570A-BA44-49B6-95E4-73B724DD0A60}_is1'
foreach ($hive in @([Microsoft.Win32.Registry]::CurrentUser,[Microsoft.Win32.Registry]::LocalMachine)) {
    $existing = $hive.OpenSubKey($subkey)
    if ($null -ne $existing) { $existing.Dispose(); throw 'Existing production registration must not be overwritten.' }
}
[xml]$project = Get-Content (Join-Path $root 'src/AccessibleOCRStudio/AccessibleOCRStudio.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$old = Join-Path $root 'artifacts\published-1.1.0-installer.exe'
Invoke-WebRequest 'https://github.com/ihrammal/textbridge/releases/download/v1.1.0/TextBridge-Setup-1.1.0.exe' -OutFile $old
if ((Get-FileHash $old -Algorithm SHA256).Hash -ne '3E77C2B043C568321C760BEB032F2487D82F6A6DB3F142D3DBF557A5ECCDF129') { throw 'Published 1.1.0 installer checksum changed.' }
function Install-Version($file, $components, $log) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /TASKS="" /DIR="' + $target + '" /LOG="' + $log + '"'
    if ($components) { $arguments += ' /COMPONENTS="' + $components + '"' }
    $process = Start-Process $file -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode). See $log" }
}
try {
    Install-Version $old 'paddle' (Join-Path $root 'artifacts\published-upgrade-old-test.log')
    Write-Output 'PASS: Published 1.1.0 installer installs with the legacy parent-only Paddle selection.'
    Install-Version (Join-Path $root "artifacts\TextBridge-Setup-$version.exe") '' (Join-Path $root 'artifacts\published-upgrade-new-test.log')
    foreach ($model in @('arabic_PP-OCRv5_mobile_rec','en_PP-OCRv5_mobile_rec','latin_PP-OCRv5_mobile_rec')) {
        if (-not (Test-Path "$target\paddle\models\$model\inference.pdiparams")) { throw "Upgrade lost $model" }
    }
    $ActualVersion = (Get-Item "$target\TextBridge.exe").VersionInfo
    $ExpectedBuild = [string]$project.Project.PropertyGroup.FileVersion
    # SDK builds in a Git checkout append the source revision to ProductVersion.
    if ($ActualVersion.ProductVersion.Trim().Split('+')[0] -ne $version -or $ActualVersion.FileVersion.Trim() -ne $ExpectedBuild) {
        throw "Upgrade metadata mismatch: product=$($ActualVersion.ProductVersion), build=$($ActualVersion.FileVersion); expected $version / $ExpectedBuild"
    }
    foreach ($Binary in @('TextBridge.exe','TextBridge.dll')) {
        if ((Get-FileHash (Join-Path $target $Binary)).Hash -ne (Get-FileHash (Join-Path $root "publish/$Binary")).Hash) { throw "Upgrade did not install the tested $Binary" }
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subkey)
    try { if ($null -eq $key -or $key.GetValue('DisplayVersion') -ne $version) { throw 'Upgrade registration is incorrect.' } } finally { if ($key) { $key.Dispose() } }
    Write-Output 'PASS: Real published 1.1.0 upgrades to the candidate with all language models and per-user registration retained.'
    $report = Join-Path $root 'artifacts\published-upgrade-ocr.json'
    $info = [Diagnostics.ProcessStartInfo]::new("$target\TextBridge.exe")
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    foreach ($argument in @('--verify-ocr',(Join-Path $root '.tools\installed-ocr-fixture.png'),'eng',$report)) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(180000)) { $process.Kill(); throw 'Upgraded OCR timed out.' }
    if ($process.ExitCode -ne 0 -or (Get-Content $report -Raw | ConvertFrom-Json).text -notmatch 'quick brown fox') { throw 'Upgraded OCR failed.' }
    Write-Output 'PASS: Upgraded application recognizes text and exports a searchable PDF.'
} finally {
    if (Test-Path "$target\unins000.exe") {
        $process = Start-Process "$target\unins000.exe" -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Upgrade test uninstall failed.' }
        Write-Output 'PASS: Upgraded installation uninstalls successfully.'
    }
}
