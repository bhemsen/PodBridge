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

    HONEST LOAD REALITY (x64) -- THREE machine-wide conditions must hold before
    this driver can load. This script performs exactly ONE of them (the cert
    trust, inside the opt-in) and only REPORTS the other two:

        1. Secure Boot OFF   -- a precondition of test-signing mode; you change
                                this in UEFI/BIOS. On a BitLocker disk it can
                                trigger a recovery-key prompt at next boot.
        2. bcdedit /set testsigning on     (then reboot)
        3. the test cert trusted           <- the only one this script does

    `bcdedit` and UEFI settings are machine-wide security changes on the PodBridge
    deny-list; the app and this script never make them on your behalf.

    Additionally, Memory Integrity (HVCI, Windows Security > Device security >
    Core isolation) must be OFF: while it enforces, Windows refuses a test-signed
    driver regardless of the three above. Install aborts if it detects HVCI, so
    you do not weaken the machine for a driver that still cannot load; -Force
    overrides that check.

    This driver is TEST-signed with a locally-generated self-signed certificate.
    It is NOT Microsoft-signed / attestation-signed. The production path (an EV
    code-signing certificate + Microsoft Partner Center) is deferred and out of
    scope; see docs/user/advanced-tier.md.

.PARAMETER Action
    install (default) or uninstall.

.PARAMETER Force
    Install even when Memory Integrity (HVCI) is detected as enforcing. Without it
    the install aborts on that check, having changed nothing.

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
    [string]$PackageDir,
    [switch]$Force
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
    if ($Force) { $psArgs += '-Force' }
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

# Report the machine-wide load preconditions BEFORE anything is changed, so a user is
# never left having lowered their security for a driver that still cannot load.
#
# Memory Integrity (HVCI) and an enforced WDAC policy each silently defeat everything
# else: while either holds, Windows refuses a test-signed driver however Secure Boot and
# test-signing are set.
#
# Test-signing itself is NOT probed. Reading it means running bcdedit, and the project
# promises -- in the docs, in the app dialog, and via a command deny-list -- that
# PodBridge never runs bcdedit on the user's behalf. An absolute, auditable promise is
# worth more than one row in a table, so the script tells the user how to check instead.
#
# Probes fail CLOSED: an unreadable value reads as "unknown" and says so, rather than
# being reported as a passing precondition.
function Show-LoadPreconditions {
    Write-Host '== load preconditions =='

    $secureBoot = $null
    try {
        $sb = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -ErrorAction Stop
        $secureBoot = [bool]$sb.UEFISecureBootEnabled
    }
    catch { }

    # $null, deliberately NOT $false: with $false as the initial value, a machine whose
    # WMI is broken would be reported "Memory Integrity off -- OK" and sail into an
    # install that cannot possibly work. This probe fails CLOSED; unknown reads unknown.
    $hvci = $null
    $wdac = $null
    try {
        $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard `
            -ClassName Win32_DeviceGuard -ErrorAction Stop
        $hvci = [bool]($null -ne $dg.SecurityServicesRunning -and $dg.SecurityServicesRunning -contains 2)
        # 2 = Enforced. A WDAC code-integrity policy in enforcement blocks a test-signed
        # driver independently of HVCI, and is common on managed work PCs -- exactly the
        # machine this project's primary persona uses.
        $wdac = [bool]($dg.CodeIntegrityPolicyEnforcementStatus -eq 2)
    }
    catch { }

    function Format-State($actual, $wanted) {
        if ($null -eq $actual) { return 'UNKNOWN' }
        if ($actual -eq $wanted) { return 'OK' }
        return 'BLOCKS LOADING'
    }

    $sbText = if ($null -eq $secureBoot) { 'unknown' } elseif ($secureBoot) { 'ON' } else { 'off' }
    $hvText = if ($null -eq $hvci) { 'unknown' } elseif ($hvci) { 'ON' } else { 'off' }
    $wdText = if ($null -eq $wdac) { 'unknown' } elseif ($wdac) { 'ENFORCED' } else { 'off' }

    Write-Host ("  Secure Boot       : {0,-8} {1}" -f $sbText, (Format-State $secureBoot $false))
    Write-Host ("  Memory Integrity  : {0,-8} {1}" -f $hvText, (Format-State $hvci $false))
    Write-Host ("  WDAC enforcement  : {0,-8} {1}" -f $wdText, (Format-State $wdac $false))
    Write-Host '  Test-signing mode : not checked -- see below'
    Write-Host ''
    Write-Host 'Test-signing state is deliberately NOT probed here: PodBridge never runs'
    Write-Host 'bcdedit, not even read-only, so that the promise stays absolute and'
    Write-Host 'auditable. Check it yourself with:  bcdedit /enum {current}'
    Write-Host ''

    if ($null -eq $hvci) {
        Write-Warning @'
Could not determine whether Memory Integrity is on (the DeviceGuard WMI class did not
answer). If it IS on, Windows will refuse this driver however you set everything else.
Check: Windows Security > Device security > Core isolation.
'@
    }

    if ($wdac -eq $true) {
        Write-Warning @'
A WDAC code-integrity policy is ENFORCED on this PC (often set by an employer's device
management). It blocks test-signed drivers independently of Memory Integrity, and you
usually cannot override it yourself -- talk to whoever manages the machine before
changing any security setting for this.
'@
    }

    if ($hvci -eq $true) {
        Write-Warning @'
Memory Integrity (HVCI) is ENFORCING on this PC.
Windows will refuse to load a test-signed driver while it is on -- turning off Secure
Boot and enabling test-signing would weaken this machine for nothing.

Turn it off first if you want the advanced tier:
  Windows Security > Device security > Core isolation > Memory integrity -> Off, reboot.

Nothing has been changed.
'@
        if (-not $Force) {
            throw 'Aborted on the Memory Integrity check. Re-run with -Force to install anyway.'
        }
        Write-Host '-Force given: continuing despite Memory Integrity.' -ForegroundColor Yellow
    }

    if ($secureBoot) {
        Write-Warning @'
Secure Boot is ON. Test-signing mode cannot be enabled until you turn it off in this
PC's UEFI/BIOS setup -- PodBridge cannot and does not change it.

If this disk is BitLocker-encrypted, changing Secure Boot can trigger a recovery-key
prompt on the next boot. Have your recovery key to hand, or suspend protection first:
  manage-bde -protectors -disable C: -rebootcount 2
'@
    }
}

function Install-AdvancedTier {
    $dir = Resolve-PackageDir
    $inf = Join-Path $dir $infName
    if (-not (Test-Path $inf)) { throw "Driver INF not found: $inf" }
    $cer = Resolve-CertPath $dir

    # Check before changing anything -- this can abort, and must abort having done nothing.
    Show-LoadPreconditions

    # 1) Trust the test cert FIRST so the package can load once test-signing is on.
    Import-TestCertificate $cer

    # 2) Stage + install the driver package.
    Write-Host "== pnputil /add-driver $infName /install =="
    & pnputil.exe /add-driver $inf /install
    if ($LASTEXITCODE -ne 0) { throw "pnputil /add-driver failed with exit code $LASTEXITCODE." }

    Write-Host ''
    Write-Host 'Driver installed and test cert trusted.' -ForegroundColor Green
    Write-Host 'Machine-wide steps that remain -- do them yourself; this script never' -ForegroundColor Yellow
    Write-Host 'runs bcdedit and never touches UEFI:' -ForegroundColor Yellow
    Write-Host '  - Secure Boot OFF (UEFI/BIOS setup) if it is still on'
    Write-Host '  - bcdedit /set testsigning on   (elevated prompt, then REBOOT)'
    Write-Host ''
    Write-Host 'See the precondition table printed above for the current state of each.'
    Write-Host 'Secure Boot off + test-signing on + this trusted test cert together weaken'
    Write-Host 'machine security; all are opt-in and reversible, and every default (Tier-1)'
    Write-Host 'feature works without them. This driver is NOT Microsoft-signed.'
    Write-Host 'See docs/user/advanced-tier.md, including how to recover if it misbehaves.'
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
    Write-Host 'Two machine-wide changes stay until YOU reverse them:' -ForegroundColor Yellow
    Write-Host '  1. Test-signing mode:'
    Write-Host '       bcdedit /set testsigning off   (elevated, then reboot)'
    Write-Host '  2. Secure Boot -- re-enable it in your UEFI/BIOS setup if you turned it'
    Write-Host '     off for this. Leaving it off is the larger of the two security costs.'
    Write-Host '     On a BitLocker disk, expect a recovery-key prompt; suspend first with'
    Write-Host '       manage-bde -protectors -disable C: -rebootcount 2'
    Write-Host '  (Also re-enable Memory Integrity if you turned it off:'
    Write-Host '   Windows Security > Device security > Core isolation.)'
}

Invoke-SelfElevation

switch ($Action) {
    'install' { Install-AdvancedTier }
    'uninstall' { Uninstall-AdvancedTier }
}
