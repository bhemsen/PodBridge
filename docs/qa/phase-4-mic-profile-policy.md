# PodBridge — Phase 4 Manual Test Guide (Microphone-profile policy)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine; the routing/mic cases need real AirPods **plus a second audio device**, the build/verify, repo and persistence cases do not. Each case is tagged **[machine]** (no AirPods needed) or **[real-AirPods]** (needs hardware) so the no-hardware cases can be batched first.

## 1. Title & Scope

This guide verifies **Phase 4 — Microphone-profile policy** (milestone #4, issues #25–#30). Phase 4 gives the user explicit, driver-free control over the unavoidable **A2DP↔HFP trade-off** on AirPods, with three modes selectable from the tray:

- A **Core mic-policy engine** (`MicPolicyEngine`) behind `IAudioPolicy` (enumerate endpoints; set the default vs default-communications endpoint per `AudioRole` = `Console`/`Multimedia`/`Communications`) and `IAudioSessionMonitor` (comms-capture session open/close). No P/Invoke and no WinRT-UI package in Core — every role decision hinges purely on each endpoint's adapter-supplied `IsAirPods` flag.
- **Three modes:** **HiFi-lock** (AirPods stay the media render default; the communications render **and** capture point at a non-AirPods fallback, so opening a mic session never forces HFP on the AirPods), **Auto-switch** (promotes AirPods to the communications role only while a comms capture session is live, then restores the HiFi-lock assignment), and **Call-mode** (a manual tray toggle that swaps the render+capture communications role to/from AirPods on demand).
- **AirPods audio-endpoint identification** in `WindowsAudioPolicy` by matching the MMDevice **container-id** (`PKEY_Device_ContainerId`) to the connected AirPods, with an endpoint friendly-name fallback — a **distinct mapping** from the Phase 1–2 Bluetooth-device identification (name heuristic / BLE company-id): it maps an *audio endpoint*, not a *Bluetooth device*.
- **Graceful degradation:** when AirPods are the only audio device (no non-AirPods fallback for the comms role), HiFi-lock/Auto-switch collapse to Call-mode behaviour and the tray surfaces an honest warning — never a silent quality collapse or crash.
- **Persistence + tray UI:** the selected mode persists across restarts (default **HiFi-lock**); the mode + Call-mode toggle live in a **"Microphone mode"** tray submenu.

Phase 4 stays **Tier 1: driver-free and requires no administrator rights** — the app runs `asInvoker` and must never trigger a UAC prompt. The Windows levers are `IPolicyConfig`/`IPolicyConfig2` (set default vs default-communications endpoint per role) and `IAudioSessionManager2` (observe comms capture sessions), both via P/Invoke isolated in `PodBridge.Windows`.

> **Honest Tier-1 reality (read this first).** The **A2DP↔HFP trade-off is a Bluetooth-Classic platform limit, not a PodBridge bug.** On AirPods, Windows unifies A2DP (stereo, hi-fi) and HFP (mono, narrowband call quality) into one render + one capture endpoint; the moment any app opens the AirPods microphone **or** a Communications-category render stream, the radio drops to HFP and stereo media collapses to mono call quality. A user-mode tool **cannot** command the profile and **cannot** force a wideband (mSBC) mic — that is radio/driver-negotiated. Phase 4 **manages** the trade-off (it routes which device holds which role) — it does **not** solve it, and no case below should be failed for "the AirPods mic is mono during a call" (that is correct HFP behaviour). The Phase-3 mic-mode line still shows the live A2DP/HFP state.

Out of scope here: negotiated-codec (AAC/SBC) + mic-mode **display** (Phase 3 — Phase 4 *acts on* the trade-off, Phase 3 *detects and shows* it); battery / play-pause (Phase 2); packaging/About window (Phase 5); ANC/gestures + the L2CAP driver (Phases 6–7).

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH`.
- **No administrator rights** — Tier 1 is driver-free; the manifest is `requestedExecutionLevel asInvoker`.
- Run all commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/GitHub checks (§4.2, §5.11–5.12): the [`gh`](https://cli.github.com) CLI authenticated against `bhemsen/PodBridge`.
- **For the routing/mic hardware tests (§5.5–5.10):** AirPods paired to this PC, a working Bluetooth radio, **a second audio device** (built-in speakers + mic, a USB headset, or a webcam mic — anything non-AirPods that can hold the communications role), a media app (browser/Spotify) and a call/mic app (Teams/Zoom/Voice Recorder). The **degrade** case (§5.10) additionally needs to make the AirPods the *only* audio device. *Not needed for build/verify/repo/persistence cases* — the unit tests use device-independent fakes.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run

Run each command from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

> **Gotchas:** (a) `asInvoker`: launch must raise **no UAC prompt**. (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) On first ever run the mode defaults to **HiFi-lock** and the Call-mode toggle is **off**.

---

## 4. Automated checks (machine-verified baseline — do these first)

All commands run from repo root.

### 4.1 Verify gate (build + analyzers + format + tests) — [machine]

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (**0 warnings / 0 errors**, warnings-as-errors in Core), `dotnet format --verify-no-changes` clean, and `Bestanden!` / `Passed!` with **erfolgreich: 83, gesamt: 83** (83 passed, 0 failed, 0 skipped). This covers the **device-independent mic-policy suite**: a fake `IAudioPolicy` + fake `IAudioSessionMonitor` drive **all three modes** and a comms-session **open→close** cycle, asserting the exact endpoint-role assignments (AirPods on Console+Multimedia render; fallback vs AirPods on the Communications render+capture role) and the **Auto-switch restore** to the HiFi-lock assignment on close; **plus the single-device degrade decision** — a fake `IAudioPolicy` exposing **only an AirPods endpoint** collapses HiFi-lock/Auto-switch to Call-mode behaviour and raises the honest "no alternate mic" warning; **plus** the endpoint-change → `Refresh` wiring (issue #30 fix).

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build` on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail.

### 4.2 Static / repo inspections — [machine]

Building does not by itself confirm the wiring, the boundary, the manifest, or the research artefacts — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **CORE-TYPES-EXIST** | `dir src\PodBridge.Core\Audio\IAudioPolicy.cs src\PodBridge.Core\Audio\IAudioSessionMonitor.cs src\PodBridge.Core\Audio\IAudioEndpointChangeMonitor.cs src\PodBridge.Core\Audio\MicPolicyEngine.cs src\PodBridge.Core\Audio\MicPolicyMode.cs src\PodBridge.Core\Audio\AudioEndpoint.cs src\PodBridge.Core\Audio\AudioRole.cs` | All exist. |
| **WINDOWS-ADAPTERS-EXIST** | `dir src\PodBridge.Windows\WindowsAudioPolicy.cs src\PodBridge.Windows\WindowsAudioSessionMonitor.cs src\PodBridge.Windows\WindowsAudioEndpointChangeMonitor.cs src\PodBridge.Windows\Interop\PolicyConfigInterop.cs src\PodBridge.Windows\Interop\AudioSessionNotificationInterop.cs src\PodBridge.Windows\Interop\CoreAudioInterop.cs` | All exist. |
| **CORE-IS-OS-FREE** | `findstr /I /C:"DllImport" /C:"P/Invoke" /C:"IPolicyConfig" /C:"IAudioSessionManager" src\PodBridge.Core\Audio\MicPolicyEngine.cs` | **No match** — Core carries no P/Invoke; all COM lives in `PodBridge.Windows`. |
| **DI-WIRING** | `findstr /C:"IAudioPolicy" /C:"IAudioSessionMonitor" /C:"IAudioEndpointChangeMonitor" src\PodBridge.Windows\ServiceCollectionExtensions.cs` then `findstr /C:"MicPolicyEngine" src\PodBridge.App\CompositionRoot.cs` | Windows binds `IAudioPolicy`→`WindowsAudioPolicy`, `IAudioSessionMonitor`→`WindowsAudioSessionMonitor`, `IAudioEndpointChangeMonitor`→`WindowsAudioEndpointChangeMonitor`; the App composition root registers the singleton `MicPolicyEngine`. |
| **APP-WIRING** | `dir src\PodBridge.App\TrayMicController.cs src\PodBridge.App\MicPolicyModeStore.cs` then `findstr /C:"StartMicPolicyPipeline" src\PodBridge.App\App.xaml.cs` | The tray controller + persistence store exist; `App.OnStartup` starts the mic-policy pipeline (resolves the engine, then starts the session + endpoint-change monitors). |
| **ARCHITECTURE-DOC** | `findstr /C:"WindowsAudioPolicy" /C:"WindowsAudioSessionMonitor" /C:"IAudioPolicy" docs\architecture.md` | Named in the component map / key flow #2 (mic-profile policy). |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker"`; **no** `requireAdministrator` / `highestAvailable` (unchanged by Phase 4). |
| **RESEARCH-COMMENTS** | `gh issue view 25 --repo bhemsen/PodBridge --comments`, then `... 26 ...` | #25 `## Research: IPolicyConfig/IPolicyConfig2 contract + endpoint->device match`; #26 `## Research: IAudioSessionManager2 comms-capture detection`. Docs: `dir docs\research\mic-profile-policy-ipolicyconfig.md docs\research\mic-profile-policy-comms-detection.md`. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 1` | Latest `verify` run on `main` = `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown.

**Reference — exact UI strings:**
- Top-level context menu (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(separator)* · **`Microphone mode`** (submenu) · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · `Exit`
- **`Microphone mode`** submenu (top→bottom): `HiFi-lock` · `Auto-switch` · `Call-mode` (three checkable **radio** items) · *(separator)* · `AirPods mic (Call-mode)` (checkable toggle) · *(separator)* · *(degrade warning line — hidden unless degraded)*
- Degrade warning line **and** the one-shot degrade toast body: `No alternate mic — AirPods mic requires HFP/mono.` (toast title: `Microphone`)
- Default on first run: **`HiFi-lock`** checked; `AirPods mic (Call-mode)` **unchecked**; the warning line **hidden**.

---

### 5.1 "Microphone mode" submenu present, correct order — [machine]
- **Needs:** app only (no AirPods).
- **Action:** Launch the app, right-click the tray icon, open the **`Microphone mode`** submenu.
- **Expected:** The top-level menu shows the disabled `Status:`/`Battery:`/`Codec:`/`Mic:` lines, a separator, then **`Microphone mode`**, `Refresh audio status`, `Pair / Reconnect`, `Open Bluetooth settings`, a separator, `Exit`. Inside `Microphone mode`: three radio items `HiFi-lock` / `Auto-switch` / `Call-mode`, a separator, the `AirPods mic (Call-mode)` toggle, a separator. On a fresh install `HiFi-lock` is checked and the toggle is off.
- **Maps to:** issue #30; spec "mode selectable from the tray (submenu + Call-mode toggle)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 Mode radio behaviour — picking a mode moves the check — [machine]
- **Needs:** app only.
- **Action:** In `Microphone mode`, click `Auto-switch`, re-open the submenu; then click `Call-mode`, re-open; then `HiFi-lock`, re-open.
- **Expected:** Exactly one mode is checked at a time and it follows your selection (radio group — clicking the already-checked item never leaves the group empty). No crash, no UAC prompt.
- **Maps to:** issue #30; spec "the active mode is selectable from the tray".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 Selected mode persists across restart (default HiFi-lock first run) — [machine]
- **Needs:** app only.
- **Action:** Select `Auto-switch`. Exit the app (tray → **Exit**). Optionally inspect `%LOCALAPPDATA%\PodBridge\mic-policy-mode.txt` (`type "%LOCALAPPDATA%\PodBridge\mic-policy-mode.txt"` → `AutoSwitch`). Re-launch and open `Microphone mode`.
- **Expected:** `Auto-switch` is still checked after the restart. On a **brand-new** profile (no settings file) the mode defaults to `HiFi-lock`. Note the **Call-mode toggle deliberately defaults off every launch** even if a mode is remembered — so AirPods are never silently forced into HFP/mono at startup.
- **Maps to:** issue #30; spec "the selected mode is persisted across restarts (default HiFi-lock)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 No AirPods connected → no crash, no warning, no elevation — [machine]
- **Needs:** app only (no AirPods).
- **Action:** With no AirPods connected, switch between all three modes and toggle `AirPods mic (Call-mode)` on/off a few times.
- **Expected:** No crash and the degrade warning line stays **hidden** (a normal PC still has a non-AirPods speaker + mic to hold the comms role). Switching modes with no AirPods present is a no-op on the AirPods side. Process stays a normal (non-elevated) user process.
- **Maps to:** spec graceful degradation + "runs without elevation".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 Endpoint identification — correct device tagged as AirPods — [real-AirPods]
- **Needs:** **real AirPods** + a second audio device, both connected.
- **Action:** Connect AirPods and keep a second audio device present. Open **Settings → System → Sound** to see the endpoint names. Exercise the modes below (§5.6–§5.8) and watch which device Windows shows as the default / default-communications device.
- **Expected:** Role changes land on the **right** devices — media roles on the AirPods, the comms fallback on the second device (HiFi-lock), never the reverse. The adapter tags the AirPods endpoint by MMDevice container-id (friendly-name fallback). If the wrong device is tagged, roles route to the wrong endpoint — that is a FAIL (file it).
- **Maps to:** issue #28; spec Verification "endpoint identification (real HW)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 HiFi-lock — media stays A2DP during a call — [real-AirPods]
- **Needs:** **real AirPods** + second audio device + media app + call/mic app.
- **Action:** Select `HiFi-lock`. Play stereo media through the AirPods. Open a call/mic app (Teams/Zoom/Voice Recorder) so it engages the microphone.
- **Expected:** The AirPods **media audio stays A2DP** (stays stereo/hi-fi, does **not** collapse to mono) and the microphone in use is the **fallback** (non-AirPods) device — because HiFi-lock keeps both communications roles (render + capture) off the AirPods. Cross-check the Phase-3 `Mic:` line: it should stay `Mic: High quality (A2DP)`.
- **Maps to:** issue #27/#28; spec Verification "HiFi-lock (real HW)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6a Smooth playback — no chopped audio, no endpoint-reassignment loop (regression) — [real-AirPods]
- **Needs:** **real AirPods** + a second audio device; **no** call/mic app.
- **Background:** the mic-policy engine writes default endpoints via `IPolicyConfig`; each write can raise the OS default-changed notification, which the engine re-processes. Before the idempotence fix (`MicPolicyEngine.SetDefaultIfChanged`) the engine re-applied an already-satisfied assignment on every such notification, forming a **self-feeding loop** that continuously re-initialised the A2DP render stream — heard as **playback chopped "after every letter."** The fix skips no-op sets so apply converges to a fixed point.
- **Action:** Select `HiFi-lock`. Play continuous stereo media (music/speech) through the AirPods for **≥ 60 s** without touching the tray. Then switch to `Auto-switch` (still no call app) and keep listening for another **≥ 60 s**.
- **Expected:** Playback is **continuous and clean** — no repeated micro-dropouts, stutter, or gaps, and no audible re-connect/re-init clicks. Audio stays **stereo A2DP** (`Mic: High quality (A2DP)`). If you hear chopped/stuttering output that clears only when the mic policy is disabled, that is a FAIL — capture the selected mode, whether media is stereo or mono, and whether it correlates with mic use.
- **Maps to:** post-Phase-4 regression fix (idempotent apply); constitution graceful-degradation + honest-audio.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.7 Auto-switch — AirPods mic during a call, A2DP restored after — [real-AirPods]
- **Needs:** **real AirPods** + second audio device + media app + call/mic app.
- **Action:** Select `Auto-switch`. Play stereo media through the AirPods. Open a call/mic app so a Communications capture session goes live; speak/observe; then end the call / close the app.
- **Expected:** While the comms session is live the **AirPods microphone works** (mono/HFP — this is correct, not a bug); when the session closes the policy **restores the HiFi-lock assignment** and **A2DP stereo media resumes** on the AirPods. The Phase-3 `Mic:` line tracks it: `Mic: Call mode (mono)` during the call → `Mic: High quality (A2DP)` after.
- **Maps to:** issue #27/#28/#29; spec Verification "Auto-switch (real HW)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.8 Call-mode — manual toggle swaps AirPods to/from comms, no live session — [real-AirPods]
- **Needs:** **real AirPods** + second audio device.
- **Action:** Select `Call-mode`. With **no** call app running, click `AirPods mic (Call-mode)` on; re-open the submenu; then click it off; re-open.
- **Expected:** Toggling **on** gives the AirPods the communications render+capture role (AirPods become the call mic on demand, no live session needed); toggling **off** returns the AirPods to A2DP-preferred roles (media back to hi-fi). The toggle's check reflects the engine's actual state each time you re-open the menu.
- **Maps to:** issue #27/#30; spec Verification "Call-mode (real HW)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 Auto-switch robustness spot-check (just-fixed items #82) — [real-AirPods]
- **Needs:** **real AirPods** + second audio device + a call app.
- **Action:** In `Auto-switch`, run **several** call open/close cycles in a row, including a Communications app that only opens a *render* stream (e.g. a soft-phone ringtone) as well as ones that open the mic. Between cycles, add and remove the second (fallback) audio device (unplug/replug a USB headset).
- **Expected:** After **every** cycle the AirPods reliably return to A2DP media — the comms detection never gets **stuck "on"** (the fixed **duck-reconciliation**: the render-side signal tracks the OS-authoritative count and self-heals a missed unduck on the next duck / endpoint change / 2 s safety re-scan). Adding/removing the fallback device updates the degrade warning **live** (the fixed **device-topology → `Refresh`** wiring). No stuck HFP, no crash.
- **Maps to:** PR #82 nits 1 & 3 (spot-check).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.10 Degrade path — AirPods as the only audio device — [real-AirPods]
- **Needs:** **real AirPods** as the **only** audio device (disable/unplug every other output **and** input so no non-AirPods render or capture endpoint remains).
- **Action:** With AirPods already the only device, **launch** PodBridge (to exercise the launch-toast fix), then also open the `Microphone mode` submenu. In `HiFi-lock` or `Auto-switch`, open a call app.
- **Expected:** The degrade warning line reads exactly `No alternate mic — AirPods mic requires HFP/mono.` **and** a one-shot toast (title `Microphone`, same body) fires **at launch** (the just-fixed **launch degrade toast** — it previously only updated the menu line). HiFi-lock/Auto-switch behave as Call-mode (comms follows the manual toggle, default off) — **no silent HFP** and **no crash**. Restoring a second device clears the warning line (live).
- **Maps to:** issue #27/#30 + PR #82 nit 2; spec Verification "degrade path (real HW)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.11 Research comment on issue #25 (IPolicyConfig contract) — [machine]
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 25 --repo bhemsen/PodBridge --comments`; cross-check against `src\PodBridge.Windows\WindowsAudioPolicy.cs` / `Interop\PolicyConfigInterop.cs`.
- **Expected:** `## Research: IPolicyConfig/IPolicyConfig2 contract + endpoint->device match` comment (Sources / Consensus / Disputes); its consensus (interface GUID, vtable order, `SetDefaultEndpoint`/`ERole` signature, and the MMDevice→device **container-id** match) is reflected in the adapter. `docs/research/mic-profile-policy-ipolicyconfig.md` present.
- **Maps to:** issue #25; spec Verification (research comment as QA artefact).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.12 Research comment on issue #26 (comms-session detection) — [machine]
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 26 --repo bhemsen/PodBridge --comments`; cross-check against `src\PodBridge.Windows\WindowsAudioSessionMonitor.cs`.
- **Expected:** `## Research: IAudioSessionManager2 comms-capture detection` comment; its consensus (comms-role session identification, the exact A2DP→HFP forcing trigger, and the event-primary/reconciled approach with a duck-notification complement) is reflected in the monitor. `docs/research/mic-profile-policy-comms-detection.md` present.
- **Maps to:** issue #26; spec Verification (research comment as QA artefact).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.13 No elevation, honest surface (review + smoke) — [machine]
- **Needs:** app + repo.
- **Action:** With PodBridge running, open Task Manager → Details, find `PodBridge.App.exe`; confirm it is not elevated. Read every Phase-4 user-facing string (submenu labels, toggle, warning, toast).
- **Expected:** Runs as a normal (Medium-IL) user process — no admin was requested, no driver/INF/`pnputil` step occurred. No string claims Apple-parity sound or promises to "solve"/"fix" the mic trade-off; the degrade warning is honest about the HFP/mono reality.
- **Maps to:** spec Verification "runs without elevation (`asInvoker`)"; constitution honesty principle.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

### Known residual limitations (documented, **not** defects — do not reject Phase 4 for these)

- **The A2DP↔HFP trade-off is unsolvable, by design.** Whenever the AirPods hold the communications role their mic is **mono/HFP** and media drops to call quality; there is no user-mode way to get a wideband (mSBC) mic **and** hi-fi stereo at once on Classic AirPods. Phase 4 manages *which device* holds the role — it does not remove the trade-off (vision non-goal).
- **`IPolicyConfig`/`IPolicyConfig2` are undocumented, reverse-engineered COM.** The GUID/vtable/`SetDefaultEndpoint` signature can vary across Windows 11 builds; it is confirmed by the `chore:research-ipolicyconfig` comment and isolated in `WindowsAudioPolicy`. Verify on the actual build under test.
- **Fallback device is auto-picked, not a picker.** The comms fallback is the current non-AirPods default-communications device, else the first available non-AirPods endpoint. A per-device chooser is intentionally deferred to a later QoL adhoc (spec prior decision) — not a Phase-4 defect.
- **Comms/COM-bound paths are QA-gate-verified, not unit-tested.** The **duck reconciliation** (`WindowsAudioSessionMonitor`) and the **`IMMNotificationClient` endpoint-change adapter** (`WindowsAudioEndpointChangeMonitor`) are COM/hardware-bound and there is **no Windows test project**, so they are exercised only here at the QA gate (the fix agent added a device-independent test for the Core change-monitor → `Refresh` wiring, which Verify covers). Spot-check them via §5.9 and §5.10.
- **Two AirPods render endpoints share one container-id (deterministic-selection hardening).** Real AirPods surface **both** a stereo A2DP render endpoint *and* a Hands-Free (HFP) render endpoint under the same `PKEY_Device_ContainerId`, so `WindowsAudioPolicy` tags **both** `IsAirPods=true` and Core's `MicPolicyEngine.FindAirPods` picks the first enumerated one. Within an unchanged topology MMDevice enumeration order is stable, so the media role lands on one consistent endpoint and the idempotent apply converges. **Not yet guaranteed by code:** an adapter signal distinguishing the A2DP (media) render from the HFP render, so Core could *prefer* stereo deterministically. Deferred pending on-device validation of the A2DP-vs-HFP endpoint semantics (needs real hardware). **Watch for at §5.6/§5.6a:** if media ever plays **mono** in `HiFi-lock` with a second device present, the media role bound to the HFP endpoint — file it; that is the trigger to land the deterministic-selection fix.

> **Follow-ups deferred (post-Phase-4 fix passes):** (1) the per-device fallback **picker** noted above (spec already deferred to a later adhoc); (2) **deterministic A2DP-render selection** among the two AirPods render endpoints — the idempotent-apply regression fix (`SetDefaultIfChanged`, §5.6a) relies on stable enumeration order for loop termination; making it order-independent needs the adapter A2DP/HFP signal above and on-device validation, so it is deferred to this QA gate. PR #82 earlier fixed all three of its review nits (duck reconciliation, launch degrade toast, live device-topology `Refresh`) with no code deferral.

---

## 6. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #4** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, and repro steps. Re-run this guide after the fix merges.
- **On full PASS:** Phase 4 QA is **accepted** — the spec (`docs/specs/archive/spec-mic-profile-policy.md`) is archived, roadmap links are repointed, and **milestone #4 is closed**, which unblocks **Phase 5 (Packaging & distribution)**.

---

## 7. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process: `tasklist /FI "IMAGENAME eq PodBridge.App.exe"` → `Keine Aufgaben` / `No tasks are running`. If any lingers: `taskkill /IM PodBridge.App.exe`.
- If you disabled other audio devices for §5.10, re-enable them. If you opened a call/mic app for §5.6–§5.10, close it.
