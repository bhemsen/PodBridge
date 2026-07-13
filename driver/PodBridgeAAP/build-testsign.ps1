#Requires -Version 5.1
<#
.SYNOPSIS
    Build + TEST-SIGN the PodBridge AAP KMDF L2CAP bridge driver with a
    locally-generated self-signed certificate. No purchase, no account
    (research #40 (d): the dev signing chain).

.DESCRIPTION
    Produces a test-signed driver package (PodBridgeAAP.sys + .inf + .cat) under
    an output folder. This is NOT a Microsoft-signed / attestation-signed driver
    (that path -- EV cert + Partner Center -- is deferred, out of scope for Phase
    6). See docs/specs/spec-advanced-driver-anc.md.

    HONEST LOAD REALITY (x64) -- BOTH are required to load this driver, and this
    script performs NEITHER (both are machine-wide security changes and are done
    only inside the explicit, elevated, user opt-in install; `bcdedit` is on the
    project deny-list and is never run on the user's behalf):

      1. Enable test-signing mode:   bcdedit /set testsigning on   (then reboot)
      2. Trust the self-signed cert  (or x64 rejects the untrusted publisher):
           CertMgr.exe /add PodBridgeTest.cer /s /r localMachine root
           CertMgr.exe /add PodBridgeTest.cer /s /r localMachine trustedpublisher
         (then reboot; verify with certmgr.msc)

    Install (elevated, opt-in):  pnputil /add-driver PodBridgeAAP.inf /install

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER WdkBin
    Path to the WDK tools bin (containing inf2cat.exe / signtool.exe). If omitted
    the script probes the restored Microsoft.Windows.WDK.x64 NuGet package and a
    default WDK install.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$WdkBin
)

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$project = Join-Path $projectDir 'PodBridgeAAP.vcxproj'
$outDir = Join-Path $projectDir "x64\$Configuration"
$certName = 'PodBridge Test (AAP Driver)'
# Export the PUBLIC test cert INTO the build output ($outDir) so the package folder is
# self-contained -- INF + .sys + .cat + .cer co-located, which is exactly what
# install-advanced-tier.ps1's Resolve-PackageDir resolves to and what a downloaded release
# would extract. (Signing uses the cert in CurrentUser\My by thumbprint, not this file.)
$certFile = Join-Path $outDir 'PodBridgeTest.cer'

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found; install Visual Studio with the C++ workload.' }
    $msbuild = & $vswhere -products '*' -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
    if (-not $msbuild) { throw 'MSBuild.exe not found.' }
    return $msbuild
}

# Locate a single WDK/SDK tool by name. inf2cat.exe and signtool.exe do NOT
# always live in the same bin (a NuGet WDK ships inf2cat under x86 while signtool
# comes from an installed SDK bin), so resolve each tool independently rather than
# assuming one shared directory. Prefer an x64 copy, else take any. -WdkBin, when
# given and holding the tool, wins.
function Find-WdkTool([string]$tool) {
    if ($WdkBin -and (Test-Path (Join-Path $WdkBin $tool))) { return (Join-Path $WdkBin $tool) }
    $roots = @()
    $nuget = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.wdk.x64'
    if (Test-Path $nuget) { $roots += $nuget }
    $installed = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (Test-Path $installed) { $roots += $installed }
    foreach ($root in $roots) {
        $found = Get-ChildItem -Path $root -Recurse -Filter $tool -ErrorAction SilentlyContinue |
            Sort-Object { $_.FullName -notmatch '\\x64\\' } | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    throw "Could not locate $tool (WDK/SDK bin). Pass -WdkBin or install the WDK."
}

# 1) Build the driver (headless, via the NuGet WDK).
$msbuild = Find-MSBuild
Write-Host "== restore =="
& $msbuild $project /t:restore /p:Configuration=$Configuration /p:Platform=x64
Write-Host "== build =="
# ResolveNuGetPackages=false: the legacy Microsoft.NuGet.targets asset resolver
# (guarded only by ResolveNuGetPackages + a present lock file, not by project
# style) otherwise runs for this native PackageReference project and fails with
# "no compatible framework" on the native-only assets file. The WDK NuGet needs
# no managed-asset resolution, so turn it off.
& $msbuild $project /p:Configuration=$Configuration /p:Platform=x64 /p:ResolveNuGetPackages=false
if (-not (Test-Path (Join-Path $outDir 'PodBridgeAAP.sys'))) {
    throw "Build did not produce PodBridgeAAP.sys in $outDir"
}

# Stage the INF next to the built .sys so inf2cat sees a complete package.
Copy-Item (Join-Path $projectDir 'PodBridgeAAP.inf') $outDir -Force

# 2) Create (or reuse) the self-signed test certificate.
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=$certName" } | Select-Object -First 1
if (-not $cert) {
    Write-Host "== creating self-signed test cert =="
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=$certName" `
        -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') -NotAfter (Get-Date).AddYears(3)
}
Export-Certificate -Cert $cert -FilePath $certFile -Force | Out-Null
Write-Host "Test cert exported: $certFile (thumbprint $($cert.Thumbprint))"

# 3) Catalog + sign the package (each tool resolved independently -- they need
#    not share a bin).
$inf2cat = Find-WdkTool 'inf2cat.exe'
$signtool = Find-WdkTool 'signtool.exe'

Write-Host "== inf2cat =="
& $inf2cat "/driver:$outDir" /os:10_X64 /verbose

Write-Host "== signtool (sys + cat) =="
& $signtool sign /v /fd SHA256 /sha1 $cert.Thumbprint `
    (Join-Path $outDir 'PodBridgeAAP.sys') (Join-Path $outDir 'PodBridgeAAP.cat')

Write-Host ''
Write-Host 'Test-signed package ready in:' $outDir
Write-Host 'To LOAD it (both are machine-wide, elevated, opt-in -- NOT done here):'
Write-Host '  1) bcdedit /set testsigning on   (then reboot)'
Write-Host "  2) CertMgr.exe /add `"$certFile`" /s /r localMachine root"
Write-Host "     CertMgr.exe /add `"$certFile`" /s /r localMachine trustedpublisher"
Write-Host '  3) pnputil /add-driver PodBridgeAAP.inf /install   (from the output folder)'
