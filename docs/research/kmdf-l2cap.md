# Research: KMDF L2CAP bridge driver (PSM 0x1001) + user-mode interface, INF/`pnputil` install, and the x64 signing/load reality

> Permanent record for the `chore:research-kmdf-l2cap` issue (#40), PodBridge
> milestone #6 (Phase 6 — Advanced tier). Authority for the Phase-6
> implementation issues: the `driver/PodBridgeAAP` KMDF L2CAP bridge (#41), the
> `DriverAapTransport` user-mode transport (#43), the driver CI strategy (#42),
> and the INF/`pnputil`/signing install flow.
>
> Clean-room (Apache-2.0): this file records **facts only** — DDI/struct/IOCTL
> names, BRB flow, INF section shapes, and exact `bcdedit`/`certmgr`/`pnputil`
> command forms — restated in my own words from Microsoft Learn / the WDK
> documentation, plus the `changcheng967/WinPods` repo cited only as a structural
> *reference blueprint*. No source code or verbatim documentation prose is copied
> from any GPL/other project; short, unavoidable API/CLI tokens (struct field
> names, command flags) are the only literal strings reproduced.
>
> Scope: confirm, from ≥ 3 authoritative sources, the exact mechanics for a
> C/KMDF profile driver that (a) opens a Bluetooth-Classic L2CAP channel on AAP
> **PSM 0x1001** to a connected AirPods device, (b) exposes a user-mode-accessible
> WDF device interface so `DriverAapTransport` can send/receive AAP packets, (c)
> the INF + `pnputil` install layout, (d) the full signing + install reality
> (test-signing mode **and** test-cert trust on x64; attestation signing as the
> deferred production path), and (e) whether the driver can be **built in CI on
> `windows-latest`**.

## Sources

1. [Microsoft Learn — How to Use the Bluetooth Driver Stack](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/using-the-bluetooth-driver-stack)
   — **authoritative** for the whole profile-driver I/O model: profile drivers
   talk to `Bthport.sys` via IRPs; kernel-mode operations go through
   **`IOCTL_INTERNAL_BTH_SUBMIT_BRB`** carried on an **`IRP_MJ_INTERNAL_DEVICE_CONTROL`**
   IRP with the BRB pointer in **`Parameters.Others.Argument1`**. Lists the L2CAP
   BRBs we need — **`BRB_REGISTER_PSM`**, **`BRB_L2CA_OPEN_CHANNEL`**,
   **`BRB_L2CA_ACL_TRANSFER`**, **`BRB_L2CA_CLOSE_CHANNEL`** — and the helper
   functions **`BthAllocateBrb`** / **`BthInitializeBrb`** / **`BthReuseBrb`**
   (which set the `BRB_HEADER` `Type`/`Length`). Confirms
   **`IOCTL_INTERNAL_BTHENUM_GET_DEVINFO`** as the way a profile driver learns the
   remote device it was loaded for.
2. [Microsoft Learn — Creating a L2CAP Client Connection to a Remote Device](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/creating-a-l2cap-client-connection-to-a-remote-device)
   — **authoritative** for the client open flow: build+send a `BRB_L2CA_OPEN_CHANNEL`
   giving destination `BtAddress`, `Psm`, and config; the PSM can come from SDP
   **or be a service's fixed PSM** (AAP 0x1001 is fixed); `OutResults`/`InResults`
   describe the negotiated channel; `IncomingQueueDepth` recommended = 10; close
   with `BRB_L2CA_CLOSE_CHANNEL`.
3. [Microsoft Learn — `_BRB_L2CA_OPEN_CHANNEL` (bthddi.h)](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/bthddi/ns-bthddi-_brb_l2ca_open_channel)
   — **authoritative** for the exact struct fields: `Hdr`, `ChannelHandle`,
   `Psm`, `ChannelFlags`, `BtAddress`, `ConfigOut`/`ConfigIn` (with `Mtu` ranges),
   `CallbackFlags`, `Callback`, `CallbackContext`, `OutResults`/`InResults`,
   `IncomingQueueDepth`. Callback flags include **`CALLBACK_RECV_PACKET`** (inbound
   L2CAP packet) and **`CALLBACK_DISCONNECT`**; header is `bthddi.h`.
4. [Microsoft Learn / GitHub — Bluetooth Echo L2CAP Profile Driver sample (`bthecho`)](https://github.com/microsoft/Windows-driver-samples/blob/main/bluetooth/bthecho/README.md)
   — **authoritative reference implementation** (MIT, Microsoft). Splits into a
   server (registers PSM + L2CAP server + SDP record in
   `EvtDeviceSelfManagedIoInit`, accepts via an indication callback) and a client
   (opens the channel when a user-mode app opens a file handle to the device;
   closes on file-close). Data flows via a **"parallel" WDF I/O queue** — user-mode
   writes are sent over L2CAP, reads receive echoed data — and a shared WDF
   "connection object" holds channel state. This is the closest Microsoft blueprint
   for the client role PodBridge needs.
5. [Microsoft Learn — Installing a Bluetooth Device](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/installing-a-bluetooth-device)
   — **authoritative** for the INF/PnP binding: `BthEnum` creates a PDO per remote
   service and generates hardware IDs of the form
   **`BTHENUM\{ServiceGUID}_VID&nnnnnnnn_PID&nnnn`** (and the generic compatible ID
   **`BTHENUM\{ServiceGUID}`**), where `ServiceGUID` is a 16-bit SIG value expanded
   to 128-bit or a custom GUID from `guidgen.exe`. All Bluetooth devices are in the
   **`Bluetooth`** setup class; the class installer is `Bthci.dll`. Server-side
   services are published from user mode via `BluetoothSetLocalServiceInfo` (needs
   `SE_LOAD_DRIVER_NAME`).
6. [Microsoft Learn — Test Signing](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/test-signing)
   — **authoritative** for the x64 kernel-signing requirement and the
   **`bcdedit /set testsigning on`** (+ reboot) step, plus the dev signing chain
   (`makecert` → `inf2cat` → `signtool`) and install via
   **`pnputil /add-driver <inf> /install`**.
7. [Microsoft Learn — Installing Test Certificates](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/installing-test-certificates)
   — **authoritative** for the *exact* trust step: import the test cert into BOTH
   the machine's **Trusted Root Certification Authorities** and **Trusted
   Publishers** stores with
   **`CertMgr.exe /add <cert>.cer /s /r localMachine root`** and
   **`CertMgr.exe /add <cert>.cer /s /r localMachine trustedpublisher`**, then
   reboot; without a trusted root the chain "terminated in a root certificate which
   is not trusted".
8. [Microsoft Learn — Install the WDK using NuGet](https://learn.microsoft.com/en-us/windows-hardware/drivers/install-the-wdk-using-nuget)
   — **authoritative** for CI buildability: the WDK ships as the
   **`Microsoft.Windows.WDK.x64`** NuGet package (SDK NuGet pulled in automatically),
   explicitly "shared and supported by modern CI/CD pipelines" and integrable into
   build systems; points to `Building-Locally.md` for automated builds. Requires a
   Visual Studio with the **Desktop development with C++** workload + the **Windows
   Driver Kit** component + Spectre-mitigated libs. Note: the `dotnet` CLI does not
   drive WDK builds (use MSBuild).
9. [Microsoft Learn — Attestation Sign Windows Drivers](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-attestation)
   — **authoritative** for the production path: needs an **EV code-signing
   certificate** + registration in the **Microsoft Hardware Developer Program**
   (Partner Center); you `MakeCab` the driver package, EV-sign the CAB, submit in
   Partner Center, and Microsoft returns a Microsoft-signed driver that loads
   **without** test-signing mode. (Attestation-signed retail drivers are not pushed
   to Windows Update.)
10. [Microsoft Tech Community — Advancing Windows driver security: Removing trust for the cross-signed driver program](https://techcommunity.microsoft.com/blog/windows-itpro-blog/advancing-windows-driver-security-removing-trust-for-the-cross-signed-driver-pro/4504818)
    — **authoritative** for the April-2026 policy: the **April 2026** Windows update
    (Win11 24H2/25H2/26H1, Server 2025) removes trust for the legacy cross-signed
    driver program (rolls out in evaluation/audit mode first). Cross-signing was
    already deprecated in 2021; going forward, kernel drivers must be signed via the
    Windows Hardware Compatibility Program / attestation. This is why cross-signing
    is a dead end for a free OSS project.
11. [GitHub — `changcheng967/WinPods`](https://github.com/changcheng967/WinPods)
    — **reference blueprint only** (structure, not code). Has a `driver/WinPodsAAP/`
    KMDF L2CAP bridge, built with Visual Studio + WDK, installed with
    `bcdedit /set testsigning on` then `pnputil /add-driver WinPodsAAP.inf /install`;
    a user-mode core (`src/WinPods.Core/AAP/`) reaches the driver through a device
    interface. **Explicitly "not yet verified on real hardware."** Confirms the
    overall shape PodBridge adopts; we depend on none of its code.

## Consensus

An implementer of `driver/PodBridgeAAP` (#41), `DriverAapTransport` (#43), and the
CI/install flow (#42) can act on the following. All names are Microsoft DDI/tool
tokens; the design decisions match Sources 1–4 and the spec's Prior decisions.

### (a) Driver kind & how it targets the connected AirPods

- **Driver kind:** a **KMDF Bluetooth *profile* driver** (a client L2CAP profile
  driver), not a raw user-mode L2CAP client — user mode on Windows gets only
  RFCOMM + BLE/GATT, so a kernel profile driver is the only way to open a custom
  Classic-L2CAP PSM. It is enumerated by **`BthEnum`** as a function driver over a
  PDO that `BthEnum` created for a service GUID on the paired AirPods.
- **Target device / remote address:** the driver does **not** guess the address —
  it is loaded *onto the PDO for that remote device*. It obtains the remote
  `BTH_ADDR` (and device state / class-of-device) by sending
  **`IOCTL_INTERNAL_BTHENUM_GET_DEVINFO`** to the PDO. That `BTH_ADDR` is what goes
  into `BRB_L2CA_OPEN_CHANNEL.BtAddress`.
- **Interface acquisition:** query down the stack (WDM `IRP_MN_QUERY_INTERFACE`
  with the Bluetooth profile-driver interface) to get the exported helpers
  **`BthAllocateBrb`**, **`BthInitializeBrb`**, **`BthReuseBrb`**, `BthFreeBrb`.
  Every BRB is allocated/initialized with these (they set `BRB_HEADER.Type` and
  `Length`).

### (a cont.) L2CAP open flow for PSM 0x1001

- **Submit mechanism (all BRBs):** set `IRP_MJ_INTERNAL_DEVICE_CONTROL`,
  `IoControlCode = IOCTL_INTERNAL_BTH_SUBMIT_BRB`, and
  `Parameters.Others.Argument1 = <pointer to BRB>`; send the IRP down to
  `Bthport.sys` (via the lower `WDFIOTARGET`).
- **PSM:** AAP uses the **fixed PSM `0x1001`** — no SDP lookup is required for a
  client connect (Source 2 allows "a service's fixed PSM"). Put `0x1001` in
  `_BRB_L2CA_OPEN_CHANNEL.Psm`. (A client that only *connects* does not need
  `BRB_REGISTER_PSM`; `BRB_REGISTER_PSM` / `BRB_L2CA_REGISTER_SERVER` are for the
  *server/accept* role, which PodBridge does not use.)
- **Open:** build a `BRB_L2CA_OPEN_CHANNEL` with:
  `BtAddress` = the AirPods addr from `GET_DEVINFO`; `Psm = 0x1001`;
  `ConfigOut.Mtu` / `ConfigIn.Mtu` = as wide a range as possible (do not raise the
  minimum MTU unless required, or negotiation fails); `IncomingQueueDepth = 10`;
  `Callback` = the driver's `PFNBTHPORT_INDICATION_CALLBACK`; `CallbackFlags`
  including **`CALLBACK_RECV_PACKET`** and **`CALLBACK_DISCONNECT`**. `ChannelFlags`
  should **not** force encryption (`CF_LINK_ENCRYPTED`) — the AAP control channel
  is the *cleartext* channel per the constitution; leave the link as the stack
  negotiated it for the existing pairing.
- **On success:** `ChannelHandle` + `OutResults`/`InResults` are filled in; keep
  `ChannelHandle` in the connection object.
- **Send AAP packets (user → AirPods):** build a `BRB_L2CA_ACL_TRANSFER` with the
  outbound direction referencing `ChannelHandle` and the caller's buffer; submit
  via `IOCTL_INTERNAL_BTH_SUBMIT_BRB`.
- **Receive AAP packets (AirPods → user):** inbound frames arrive through the
  indication `Callback` with the `CALLBACK_RECV_PACKET` reason (an
  `IndicationReceivedPacket`-style notification carrying the data); the driver
  copies that into the pending inbound user request (see (b)).
- **Close:** `BRB_L2CA_CLOSE_CHANNEL` on `ChannelHandle` on file-close / device
  removal.

### (b) User-mode-accessible interface (the `DriverAapTransport` contract)

- **Device interface:** the driver calls `WdfDeviceCreateDeviceInterface` with a
  **project-specific interface GUID** (define one new GUID for PodBridge, e.g.
  `GUID_DEVINTERFACE_PODBRIDGE_AAP`). `DriverAapTransport` enumerates it with
  `CM_Get_Device_Interface_List` / `SetupDiGetClassDevs` and opens it with
  `CreateFile`; **driver presence == interface present**, which is exactly the
  "probe → `Unavailable` when absent" check the spec requires.
- **I/O model (DEFAULT, matches spec Prior decision + `bthecho`):** WDF I/O queues
  handling `IRP_MJ_DEVICE_CONTROL` (user-mode) with these IOCTLs (define with
  `CTL_CODE`, `FILE_DEVICE_UNKNOWN`, `METHOD_BUFFERED`, `FILE_READ_DATA|FILE_WRITE_DATA`):
  - **`IOCTL_PODBRIDGE_CONNECT`** — open the L2CAP channel to the AirPods (idempotent;
    returns success once `BRB_L2CA_OPEN_CHANNEL` completes). Optional if connect is
    done implicitly on file-open like `bthecho`.
  - **`IOCTL_PODBRIDGE_SEND`** — input buffer = one AAP packet → `BRB_L2CA_ACL_TRANSFER`.
  - **`IOCTL_PODBRIDGE_RECEIVE`** — the **inverted call / pending-IOCTL** pattern:
    user mode posts this IOCTL ahead of time; the driver parks it in a
    **manual WDF queue** (`WdfIoQueueCreate` with `WdfIoQueueDispatchManual`) and
    completes it with the next inbound frame delivered by the indication callback.
    This delivers asynchronous AirPods notifications (the ANC echo) without polling.
  - A read/write-queue variant (`ReadFile`/`WriteFile`) is an acceptable equivalent
    (that is literally what `bthecho`'s "parallel queue" does); IOCTL + inverted call
    is chosen as the DEFAULT for an explicit, versionable packet contract.
- **Access control:** the interface should be openable by the (non-elevated,
  `asInvoker`) tray app once the driver is installed — the *elevation* is only for
  install, not for runtime I/O. Scope the interface with a suitable SDDL so a normal
  user session can open it.

### (c) INF layout & `pnputil` install

- **INF binds by hardware ID:** the `[Models]` install section matches a `BthEnum`
  hardware ID. For AAP the service GUID is a **custom PodBridge service GUID**
  (generate with `guidgen.exe`), so the match string is
  **`BTHENUM\{PodBridgeServiceGUID}`** (optionally narrowed with
  `_VID&…_PID&…` to Apple's VID/PID to load only on AirPods). `ServiceGUID` is the
  128-bit form.
- **INF essentials:** `[Version]` with `Signature="$WINDOWS NT$"`,
  `Class=Bluetooth`, `ClassGuid={e0cbf06c-cd8b-4647-bb8a-263b43f0f974}` (the
  Bluetooth setup class), `Provider`, a postdated `DriverVer`, `CatalogFile=….cat`,
  and **`PnpLockdown=1`**; a `[Manufacturer]`/`[Models]` mapping the hardware ID to
  a KMDF `AddService`/co-installer-free service install; `[Strings]`. It is a
  **client-side** profile driver install (the remote AirPods advertise the service;
  the PC connects), so no `BluetoothSetLocalServiceInfo` server publishing is needed.
- **Install command (elevated):**
  `pnputil /add-driver PodBridgeAAP.inf /install`
  (stage-only is `pnputil /add-driver PodBridgeAAP.inf`). Uninstall:
  `pnputil /delete-driver <oemNN.inf> /uninstall`. This is a **separate, opt-in,
  elevated** step — never bundled in the Phase-5 MSIX (MSIX cannot cleanly carry a
  kernel driver), and never run silently.

### (d) Signing + load reality on x64 (BOTH requirements)

To load the self-signed **test-signed** KMDF driver on x64, **both** are required —
neither alone is sufficient:

1. **Enable test-signing mode:** `bcdedit /set testsigning on` (elevated) **then
   reboot**. This is a **machine-wide** security relaxation and is a **manual user
   action** — the PodBridge app is on the `bcdedit` deny list and must never run it
   on the user's behalf; the UX documents the step.
2. **Trust the test certificate** — import the self-signed cert into **both**
   machine stores (elevated), or x64 rejects the untrusted publisher even with
   test-signing on:
   - Trusted Root CA: `CertMgr.exe /add PodBridgeTest.cer /s /r localMachine root`
   - Trusted Publishers: `CertMgr.exe /add PodBridgeTest.cer /s /r localMachine trustedpublisher`
   - Reboot; verify with `certmgr.msc`. Skipping the root import yields
     "A certificate chain processed, but terminated in a root certificate which is
     not trusted by the trust provider." This cert import happens **only inside the
     explicit elevated opt-in install**, never silently.
- **Dev signing chain:** `makecert -r -pe -ss PrivateCertStore -n CN=PodBridge(Test) PodBridgeTest.cer`
  → `inf2cat /driver:<pkgdir> /os:10_X64` → `signtool sign /v /s PrivateCertStore
  /n PodBridge(Test) /t <tsa> PodBridgeAAP.cat` (and embed-sign the `.sys` if it is
  a boot-start driver — this one is not). (`New-SelfSignedCertificate` +
  `Import-PfxCertificate` is the modern PowerShell equivalent of `makecert`/import.)
- **Production path (DEFERRED, out of scope for Phase 6):** attestation signing —
  **EV code-signing cert (~$250–560/yr)** + **Microsoft Hardware Developer Program
  (Partner Center)**; `MakeCab` the package, EV-sign the CAB, submit, download the
  Microsoft-signed driver, which then loads on stock x64 with **no** test-signing
  and **no** cert import. This is a paid, human-provisioned prerequisite and is
  recorded, not promised. Cross-signing is **not** an option — the April-2026 update
  removes trust for the legacy cross-signed program (Source 10), so the only routes
  are test-signed (dev/opt-in) or attestation (production).

### (e) CI buildability on `windows-latest` — YES (compile-only)

- The KMDF driver **can be compiled in CI on `windows-latest`** using the
  **`Microsoft.Windows.WDK.x64`** NuGet package (Source 8 states it is explicitly
  meant for CI/CD; the SDK NuGet is pulled in transitively). Build with **MSBuild**
  against the driver `.vcxproj`/solution (the `dotnet` CLI does not drive WDK
  builds). The `windows-latest` image ships Visual Studio Build Tools with the C++
  workload; the WDK/SDK come from the NuGet restore, so no interactive WDK installer
  is needed.
- **Therefore #42's CI job is a real, non-blocking, compile-only build** (catches
  driver compile regressions). What CI **cannot** do: sign for load, enable
  test-signing, or exercise Bluetooth hardware — so **functional verification stays
  a manual smoke test** on a real machine with real AirPods (workflow's Tier-2
  gate). Signing in CI is unnecessary for a compile check and would need secrets, so
  the CI job builds unsigned artifacts only.

## Disputes (minority → majority decision)

- **User-mode I/O: read/write queue vs IOCTL + inverted call.** The `bthecho`
  sample (Source 4) uses a "parallel" WDF **read/write** queue; the spec's Prior
  decision and standard KMDF async-notify practice favor **IOCTL + a manual/pending
  IOCTL (inverted call)**. Both are documented-valid and functionally equivalent.
  → **IOCTL + inverted call** (explicit, versionable packet contract for
  `DriverAapTransport`; matches the spec DEFAULT). `bthecho` remains the structural
  reference for the L2CAP half.
- **Does the driver register a PSM / act as a server?** `bthecho`'s *server* half
  registers a PSM and L2CAP server; some readings assume PodBridge must too.
  → **No.** PodBridge is a **client** that *connects* to the AirPods' fixed PSM
  `0x1001` (Sources 1–2): it needs `BRB_L2CA_OPEN_CHANNEL` + `BRB_L2CA_ACL_TRANSFER`
  + `BRB_L2CA_CLOSE_CHANNEL` only, not `BRB_REGISTER_PSM`/`BRB_L2CA_REGISTER_SERVER`.
- **Is test-signing mode alone enough to load the driver on x64?** A common
  assumption (and some WinPods-style instructions) imply `bcdedit /set testsigning
  on` suffices. Source 7 is explicit that it is **not**: the self-signed cert must
  also be trusted (Trusted Root CA **and** Trusted Publishers) or the load fails on
  an untrusted root. → **Both are mandatory** on x64; the opt-in install performs
  the cert import and the UX/docs state both requirements.
- **Cross-signing as a cheaper production signature.** Historically a cross-signed
  cert could load a kernel driver without Partner Center. → **Dead end**: deprecated
  since 2021 and trust removed by the April-2026 update (Source 10). Production
  signing = **attestation only** (EV cert + Partner Center), deferred.
- **Can CI build the driver, or is it human-only?** Older guidance implied a full
  WDK installer (human-only). → **CI-buildable**: the WDK NuGet package (Source 8)
  is designed for CI/CD, so `windows-latest` can compile the driver headlessly;
  only signing + functional test remain human/hardware steps.
