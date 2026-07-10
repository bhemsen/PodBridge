#Requires -Version 5.1
<#
.SYNOPSIS
    Install or uninstall the OPTIONAL PodBridge advanced-tier KMDF L2CAP bridge
    driver -- the explicit, user-triggered, ELEVATED opt-in step.

.DESCRIPTION
    This is the ONE elevated action of the advanced tier (spec
    docs/specs/spec-advanced-driver-anc.md; user guide docs/user/advanced-tier.md).
    The PodBridge app itself always runs `asInvoker` and never elevates; this
    script self-elevates (a single UAC prompt) only when you run it -- either the
    app launches it on your explicit "Enable advanced tier" action, or you run it
    by hand.

    On -Action install it performs, inside the SAME elevated step:
      1. Imports the self-signed TEST certificate that signed the driver into the
         machine's Trusted Root CA and Trusted Publishers stores. On x64, a
         test-signed driver will NOT load unless its cert is trusted in BOTH
         stores (research #40 (d) / Microsoft "Installing Test Certificates").
      2. Installs the driver package: pnputil /add-driver PodBridgeAAP.inf /install.

    On -Action uninstall it reverses both: removes the published driver
    (pnputil /delete-driver <oemNN.inf> /uninstall) and removes the test cert from
    both machine stores.

    HONEST LOAD REALITY (x64) -- BOTH machine-wide changes are required to load
    this driver; this script does exactly ONE of them (the cert trust, inside the
    opt-in). The OTHER -- enabling test-signing mode -- is a manual user step this
    script NEVER performs:

        bcdedit /set testsigning on     (then reboot)

    `bcdedit` is a machine-wide security change on the PodBridge deny-list; the
    app and this script never run it on your behalf. The script only reminds you.

    This driver is TEST-signed with a locally-generated self-signed certificate.
    It is NOT Microsoft-signed / attestation-signed. The production path (an EV
    code-signing certificate + Microsoft Partner Center) is deferred and out of
    scope; see docs/user/advanced-tier.md.

.PARAMETER Action
    install (default) or uninstall.

.PARAMETER PackageDir
    Folder holding the built + test-signed package (PodBridgeAAP.inf/.sys/.cat and
    PodBridgeTest.cer). Defaults to the build output x64\Release next to this
    script (produced by build-testsign.ps1). Point it at the extracted driver
    package if you downloaded a release instead of building locally.

.EXAMPLE
    # Build + test-sign, then install (from an elevated or normal shell):
    .\build-testsign.ps1
    .\install-advanced-tier.ps1 -Action install

.EXAMPLE
    # Remove the driver and un-trust the test cert:
    .\install-advanced-tier.ps1 -Action uninstall
#>
[CmdletBinding()]
param(
    [ValidateSet('install', 'uninstall')]
    [string]$Action = 'install',
    [string]$PackageDir
)

$ErrorActionPreference = 'Stop'

# Subject of the self-signed TEST cert created by build-testsign.ps1. Kept in sync
# with that script so uninstall can find and remove exactly what install trusted.
$certSubject = 'CN=PodBridge Test (AAP Driver)'
$infName = 'PodBridgeAAP.inf'
$certFileName = 'PodBridgeTest.cer'

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Self-elevate: re-launch this script elevated (one UAC prompt) and exit the
# non-elevated instance. This is the ONLY elevation; the calling app stays
# asInvoker. If the user declines the prompt, we report and exit non-zero.
function Invoke-SelfElevation {
    if (Test-IsAdministrator) { return }
    Write-Host 'Requesting administrator rights (one-time, for this install step only)...'
    $psArgs = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-Action', $Action
    )
    if ($PackageDir) { $psArgs += @('-PackageDir', "`"$PackageDir`"") }
    try {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $psArgs -Verb RunAs -PassThru -Wait
        exit $p.ExitCode
    }
    catch {
        Write-Error 'Administrator rights were not granted; the advanced tier was not changed.'
        exit 1
    }
}

function Resolve-PackageDir {
    # Candidates, in order: an explicit -PackageDir, the build output, and the script's own
    # folder (a flat extracted-release layout). Pick the first that actually holds the INF.
    $candidates = @()
    if ($PackageDir) { $candidates += (Resolve-Path -LiteralPath $PackageDir).Path }
    $candidates += (Join-Path $PSScriptRoot 'x64\Release')
    $candidates += $PSScriptRoot
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c $infName)) { return $c }
    }
    throw "No driver package ($infName) found. Build it first (.\build-testsign.ps1) or pass -PackageDir. Looked in: $($candidates -join '; ')"
}

function Resolve-CertPath([string]$packageDir) {
    # Locate the .cer trust file INDEPENDENTLY of the INF: build-testsign.ps1 now co-locates it
    # in the package dir, but an older build (or a hand-run) may have left it in the project root
    # next to this script. Probe both so a fresh package and an already-built tree both resolve.
    # The .cer carries only the PUBLIC key, so its exact location is not security-sensitive.
    $candidates = @($packageDir, $PSScriptRoot) | Select-Object -Unique
    foreach ($c in $candidates) {
        $cer = Join-Path $c $certFileName
        if (Test-Path $cer) { return $cer }
    }
    $looked = ($candidates | ForEach-Object { Join-Path $_ $certFileName }) -join '; '
    throw "Test certificate ($certFileName) not found (run build-testsign.ps1). Looked in: $looked"
}

# Import the test cert into BOTH machine stores (Trusted Root CA + Trusted
# Publishers). Import-Certificate is the built-in PowerShell equivalent of
# `CertMgr.exe /add <cer> /s /r localMachine root` and `... trustedpublisher`
# (research #40 (d)); both are documented-valid. We use the built-in cmdlet so no
# WDK/SDK tool needs to be on PATH.
function Import-TestCertificate([string]$certPath) {
    foreach ($store in @('Root', 'TrustedPublisher')) {
        Write-Host "== trusting test cert -> LocalMachine\$store =="
        Import-Certificate -FilePath $certPath -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
    }
}

function Remove-TestCertificate {
    foreach ($store in @('Root', 'TrustedPublisher')) {
        $found = Get-ChildItem "Cert:\LocalMachine\$store" |
            Where-Object { $_.Subject -eq $certSubject }
        foreach ($c in $found) {
            Write-Host "== removing test cert <- LocalMachine\$store ($($c.Thumbprint)) =="
            Remove-Item -LiteralPath "Cert:\LocalMachine\$store\$($c.Thumbprint)" -Force
        }
    }
}

function Install-AdvancedTier {
    $dir = Resolve-PackageDir
    $inf = Join-Path $dir $infName
    if (-not (Test-Path $inf)) { throw "Driver INF not found: $inf" }
    $cer = Resolve-CertPath $dir

    # 1) Trust the test cert FIRST so the package can load once test-signing is on.
    Import-TestCertificate $cer

    # 2) Stage + install the driver package.
    Write-Host "== pnputil /add-driver $infName /install =="
    & pnputil.exe /add-driver $inf /install
    if ($LASTEXITCODE -ne 0) { throw "pnputil /add-driver failed with exit code $LASTEXITCODE." }

    Write-Host ''
    Write-Host 'Driver installed and test cert trusted.' -ForegroundColor Green
    Write-Host 'ONE machine-wide step remains -- do it yourself; this script never runs bcdedit:' -ForegroundColor Yellow
    Write-Host '    bcdedit /set testsigning on'
    Write-Host '    (from an elevated prompt, then REBOOT)'
    Write-Host ''
    Write-Host 'Test-signing mode + this trusted test cert together weaken machine security;'
    Write-Host 'both are opt-in and reversible. This driver is NOT Microsoft-signed.'
    Write-Host 'See docs/user/advanced-tier.md.'
}

function Uninstall-AdvancedTier {
    # Find the published OEM inf that corresponds to our INF and delete it. Parsed
    # LABEL-INDEPENDENTLY (pnputil field labels are localized): the published-name line
    # carries the "oemNN.inf" token, and the original-name line that follows carries the
    # un-localized "PodBridgeAAP.inf" value. Track the last oemNN.inf seen and delete it
    # when the original-name value appears.
    $published = & pnputil.exe /enum-drivers
    $current = $null
    $removed = $false
    foreach ($line in $published) {
        if ($line -match '(oem\d+\.inf)') { $current = $Matches[1] }
        elseif ($current -and ($line -match [regex]::Escape($infName))) {
            Write-Host "== pnputil /delete-driver $current /uninstall =="
            & pnputil.exe /delete-driver $current /uninstall
            $removed = $true
            $current = $null
        }
    }
    if (-not $removed) { Write-Host "No installed $infName package found (already removed?)." }

    # Un-trust the test cert from both machine stores.
    Remove-TestCertificate

    Write-Host ''
    Write-Host 'Advanced-tier driver removed and test cert un-trusted.' -ForegroundColor Green
    Write-Host 'If you enabled test-signing mode for this, you can turn it off yourself:'
    Write-Host '    bcdedit /set testsigning off   (elevated, then reboot)'
}

Invoke-SelfElevation

switch ($Action) {
    'install' { Install-AdvancedTier }
    'uninstall' { Uninstall-AdvancedTier }
}
