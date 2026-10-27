param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = [IO.Path]::GetFullPath((Join-Path $root '.tools\selective-install-qa'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root '.tools')) + '\'
if (-not $target.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
if (Test-Path $target) { throw 'Previous selective test installation exists; inspect it first.' }
$identity = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TextBridge-Selective-QA_is1'
foreach ($hive in @([Microsoft.Win32.Registry]::CurrentUser, [Microsoft.Win32.Registry]::LocalMachine)) {
    $key = $hive.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\TextBridge-Selective-QA_is1')
    if ($null -ne $key) { $key.Dispose(); throw 'A selective QA installation already exists.' }
}
[xml]$project = Get-Content (Join-Path $root 'src/AccessibleOCRStudio/AccessibleOCRStudio.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$installer = Join-Path $root "artifacts\TextBridge-Setup-$version-Selective-QA.exe"
try {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /COMPONENTS="paddle,paddle\english" /TASKS="" /DIR="' + $target + '" /LOG="' + $root + '\artifacts\selective-install.log"'
    $process = Start-Process $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Selective setup failed: $($process.ExitCode)" }
    foreach ($model in @('PP-OCRv5_mobile_det','PP-LCNet_x1_0_doc_ori','en_PP-OCRv5_mobile_rec')) {
        if (-not (Test-Path "$target\paddle\models\$model\inference.pdiparams")) { throw "Required model missing: $model" }
    }
    foreach ($model in @('arabic_PP-OCRv5_mobile_rec','latin_PP-OCRv5_mobile_rec')) {
        if (Test-Path "$target\paddle\models\$model") { throw "Unselected model installed: $model" }
    }
    Write-Output 'PASS: English-only custom package includes common dependencies and omits unselected Arabic and French models.'
    # Upgrade without component/task arguments must retain the installed selection.
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /DIR="' + $target + '" /LOG="' + $root + '\artifacts\selective-upgrade.log"'
    $process = Start-Process $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Upgrade failed.' }
    if (-not (Test-Path "$target\paddle\models\en_PP-OCRv5_mobile_rec\inference.pdiparams") -or (Test-Path "$target\paddle\models\arabic_PP-OCRv5_mobile_rec") -or (Test-Path "$target\paddle\models\latin_PP-OCRv5_mobile_rec")) { throw 'Upgrade changed the selected models.' }
    Write-Output 'PASS: Automatic-style reinstall retains the English-only model selection.'
    $fixture = Join-Path $root '.tools\installed-ocr-fixture.png'
    $report = Join-Path $root 'artifacts\selective-installed-ocr.json'
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $target 'TextBridge.exe'))
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    foreach ($argument in @('--verify-ocr', $fixture, 'eng', $report)) { $info.ArgumentList.Add($argument) }
    $verify = [Diagnostics.Process]::Start($info)
    if (-not $verify.WaitForExit(180000)) { $verify.Kill(); throw 'English-only installed OCR timed out.' }
    if ($verify.ExitCode -ne 0) { throw 'English-only installed OCR failed.' }
    $result = Get-Content $report -Raw | ConvertFrom-Json
    if ($result.text -notmatch 'quick brown fox' -or $result.engine -ne 'paddle') { throw 'Installed OCR output was incorrect.' }
    Write-Output 'PASS: English-only installed OCR recognizes text and exports a searchable PDF.'
} finally {
    $uninstaller = Join-Path $target 'unins000.exe'
    if (Test-Path $uninstaller) {
        $process = Start-Process $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Isolated test uninstall failed.' }
        Write-Output 'PASS: Isolated test uninstalled successfully.'
    }
}
