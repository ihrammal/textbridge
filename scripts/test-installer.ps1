param([switch]$ProductionPackage, [switch]$SkipLaunch, [switch]$MachineWide, [switch]$WindowsOnly)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$TestRoot = [IO.Path]::GetFullPath((Join-Path $Root $(if ($WindowsOnly) { '.tools\install-smoke-windows' } else { '.tools\install-smoke' })))
$AllowedRoot = [IO.Path]::GetFullPath((Join-Path $Root '.tools')) + [IO.Path]::DirectorySeparatorChar
if (-not $TestRoot.StartsWith($AllowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer test target is outside the project tools directory.' }
if (Test-Path $TestRoot) { throw 'Installer test directory already exists. Inspect the previous test before retrying.' }
$Suffix = if ($ProductionPackage) { '' } else { '-QA' }
[xml]$Project = Get-Content (Join-Path $Root 'src/AccessibleOCRStudio/AccessibleOCRStudio.csproj')
$Version = [string]$Project.Project.PropertyGroup.Version
$Installer = Join-Path $Root "artifacts\TextBridge-Setup-$Version$Suffix.exe"
if (-not (Test-Path $Installer)) { throw 'Build the isolated QA installer first with build-installer.ps1 -TestInstall.' }
 $AppIdentity = if ($ProductionPackage) { '{9A9C570A-BA44-49B6-95E4-73B724DD0A60}_is1' } else { 'TextBridge-Isolated-QA_is1' }
$UserRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppIdentity"
if ($MachineWide) {
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Machine-wide installer verification is restricted to the disposable GitHub Windows runner.' }
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Machine-wide verification requires an elevated runner.' }
}
$InstallScope = if ($MachineWide) { '/ALLUSERS' } else { '/CURRENTUSER' }
$ScopeName = if ($MachineWide) { 'Machine-wide' } else { 'Per-user' }
$LogPrefix = $(if ($WindowsOnly) { 'windows-' } else { '' }) + $(if ($MachineWide) { 'machine-' } else { '' })
$RegistryPath = if ($MachineWide) { "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppIdentity" } else { $UserRegistryPath }
if (Test-Path $UserRegistryPath) { throw 'An existing per-user installation has this identity.' }
if (Test-Path $RegistryPath) { throw 'A previous isolated QA installation exists. Inspect it before retrying.' }
foreach ($machineRegistry in @("HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppIdentity", "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$AppIdentity")) {
    if (Test-Path $machineRegistry) { throw 'An installation with this identity already exists. Use the separate QA package.' }
}
function Install-App($Log) {
    $Components = if ($WindowsOnly) { '' } else { 'paddle,paddle\arabic,paddle\english,paddle\french' }
    $process = Start-Process -FilePath $Installer -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART $InstallScope /NOICONS /COMPONENTS=`"$Components`" /DIR=`"$TestRoot`" /LOG=`"$Log`"" -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode). See $Log" }
}
try {
    Install-App (Join-Path $Root ("artifacts\${LogPrefix}install-test.log"))
    $Required = @('TextBridge.exe','pdf\pdftoppm.exe','pdf\pdftotext.exe','pdf\pdfunite.exe','image\magick.exe','help\index.html')
    if (-not $WindowsOnly) { $Required += @('paddle\python\python.exe','paddle\worker.py') }
    foreach ($file in $Required) {
        if (-not (Test-Path (Join-Path $TestRoot $file))) { throw "Installed package is missing $file" }
    }
    if ($WindowsOnly -and (Test-Path -LiteralPath (Join-Path $TestRoot 'paddle'))) { throw 'Windows-only installation contains the optional Paddle component.' }
    $ExpectedEngine = if ($WindowsOnly) { 'windows' } else { 'paddle' }
    if (-not $WindowsOnly) {
        foreach ($Model in @('arabic_PP-OCRv5_mobile_rec','en_PP-OCRv5_mobile_rec','latin_PP-OCRv5_mobile_rec')) {
            if (-not (Test-Path (Join-Path $TestRoot "paddle/models/$Model/inference.pdiparams"))) { throw "Selected language model missing: $Model" }
        }
    }
    if ((Get-Content (Join-Path $TestRoot 'ocr-engine.txt') -Raw).Trim() -ne $ExpectedEngine) { throw 'Installed OCR default differs from selected components.' }
    if (-not (Test-Path $RegistryPath)) { throw 'Installer registration is missing for the requested scope.' }
    Write-Output "PASS: $ScopeName installation includes application, OCR, PDF, preprocessing and help files and correct registration."
    Install-App (Join-Path $Root ("artifacts\${LogPrefix}reinstall-test.log"))
    Write-Output 'PASS: Reinstall into the same test directory succeeds.'
    $PublishRoot = [IO.Path]::GetFullPath((Join-Path $Root 'publish'))
    foreach ($file in Get-ChildItem $PublishRoot -Recurse -File) {
        if ($file.Name -like 'ClearRead.*' -or $file.Name -eq 'ClearRead-LICENSE.txt') { continue }
        $relative = $file.FullName.Substring($PublishRoot.Length + 1)
        if ($WindowsOnly -and $relative.StartsWith('paddle\',[StringComparison]::OrdinalIgnoreCase)) { continue }
        $installed = Join-Path $TestRoot $relative
        if (-not (Test-Path $installed) -or (Get-FileHash $file.FullName).Hash -ne (Get-FileHash $installed).Hash) { throw "Installed file does not match the published build: $relative" }
    }
    Write-Output 'PASS: Every installed application file matches the published file hash.'
    Add-Type -AssemblyName System.Drawing
    $Fixture=Join-Path $Root '.tools\installed-ocr-fixture.png'
    $Bitmap=[Drawing.Bitmap]::new(1400,220)
    $Graphics=[Drawing.Graphics]::FromImage($Bitmap)
    $Font=[Drawing.Font]::new('Arial',32)
    try {
        $Graphics.Clear([Drawing.Color]::White)
        $Graphics.DrawString('The quick brown fox has 125 documents.',$Font,[Drawing.Brushes]::Black,40,60)
        $Bitmap.Save($Fixture,[Drawing.Imaging.ImageFormat]::Png)
    } finally { $Font.Dispose();$Graphics.Dispose();$Bitmap.Dispose() }
    $OcrReport=Join-Path $Root ("artifacts\${LogPrefix}installed-ocr.json")
    $VerifyInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $TestRoot 'TextBridge.exe'))
    $VerifyInfo.UseShellExecute=$false
    $VerifyInfo.CreateNoWindow=$true
    $VerifyInfo.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $VerifyInfo.Arguments="--verify-ocr `"$Fixture`" eng `"$OcrReport`""
    $VerifyInfo.EnvironmentVariables['PATH']="$env:SystemRoot\System32;$env:SystemRoot"
    $VerifyInfo.EnvironmentVariables['DOTNET_ROOT']=Join-Path $TestRoot 'no-external-dotnet'
    $Verification=[Diagnostics.Process]::Start($VerifyInfo)
    if (-not $Verification.WaitForExit(300000)) { $Verification.Kill();throw 'Installed OCR verification timed out.' }
    $Recognized=Get-Content -LiteralPath $OcrReport -Raw | ConvertFrom-Json
    if ($Verification.ExitCode -ne 0) {
        if ($WindowsOnly -and $env:GITHUB_ACTIONS -eq 'true' -and $Recognized.error -match 'language pack|class not registered') {
            Write-Output 'SKIP: This runner lacks a Windows OCR language pack; the installed app reports the OS dependency.'
        } else { throw "Installed OCR failed: $($Recognized.error)" }
    } elseif ($Recognized.engine -ne $ExpectedEngine -or $Recognized.text -notmatch 'quick brown fox' -or -not (Test-Path "$OcrReport.pdf")) {
        throw 'Installed OCR or its searchable PDF output differs from the selected engine.'
    } else { Write-Output "PASS: Installed $ExpectedEngine OCR and searchable PDF export work without external runtimes on PATH." }

    if (-not $SkipLaunch) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $TestRoot 'TextBridge.exe'
    $info.WorkingDirectory = $TestRoot
    $info.UseShellExecute = $false
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.EnvironmentVariables['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
    $info.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $TestRoot 'no-external-dotnet'
    $app = [Diagnostics.Process]::Start($info)
    try {
        if (-not $app.WaitForInputIdle(15000)) { throw 'Installed app did not become ready.' }
        Start-Sleep -Seconds 4
        $app.Refresh()
        if ($app.HasExited -or $app.MainWindowHandle -eq 0 -or -not $app.Responding) { throw 'Installed app failed startup.' }
        Write-Output 'PASS: Installed app launches and responds without an external runtime on PATH.'
        $null = $app.CloseMainWindow()
        if (-not $app.WaitForExit(10000) -or $app.ExitCode -ne 0) { throw 'Installed app did not close normally.' }
        Write-Output 'PASS: Installed app closes normally.'
    }
    finally { if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit() } }
    } else { Write-Output 'SKIP: Installed UI launch; desktop behavior is checked separately.' }
}
finally {
    $Uninstaller = Join-Path $TestRoot 'unins000.exe'
    if (Test-Path $Uninstaller) {
        $process = Start-Process -FilePath $Uninstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$Root\artifacts\${LogPrefix}uninstall-test.log`"" -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Test uninstall failed.' }
        for ($attempt = 0; $attempt -lt 20 -and (Test-Path $TestRoot); $attempt++) { Start-Sleep -Seconds 1 }
        if (Test-Path $TestRoot) { throw 'Uninstallation left files in the test directory.' }
        if (Test-Path $RegistryPath) { throw 'Uninstallation left its test registration.' }
        Write-Output "PASS: Uninstall removes the test binaries, directory and $ScopeName registration."
    }
}


