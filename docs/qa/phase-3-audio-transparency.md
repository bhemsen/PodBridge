# PodBridge — Phase 3 Manual Test Guide (Audio transparency)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine; a few cases need real AirPods, most do not.

## 1. Title & Scope

This guide verifies **Phase 3 — Audio transparency** (milestone #3, issues #20–#24). Phase 3 makes PodBridge honest about AirPods sound on Windows — **display + advise only, it never switches anything** (switching is Phase 4). It delivers:

- A **read-only** `IAudioStateReader` (Core) + `WindowsAudioStateReader` (Windows) returning `(CodecKind, MicMode)`, driver-free and `asInvoker`.
- **Negotiated-codec** model `CodecKind { Aac, Sbc, Unknown }` and **mic/audio-link mode** model `MicMode { HighQualityA2dp, CallModeHfp, Unknown }`.
- A **device-independent Core guidance engine** (`AudioGuidanceEngine`) turning the read state into honest display lines + generic AAC advice.
- A **tray surface**: a codec line, a mic-mode line, a "Refresh audio status" action, and a Windows notification carrying the AAC guidance **only on confirmed SBC**.

> **Honest Tier-1 reality (read this first).** Research (#20) found that the only driver-free way to read the *negotiated codec* (AAC vs SBC) is an **elevated** ETW trace of the `BthA2dp` provider. PodBridge Tier-1 is `asInvoker` (no admin), so the codec read **honestly returns `Unknown`** and the tray shows **`Codec: couldn't determine`**. Consequently the `Codec: AAC` / `Codec: SBC` lines and the **SBC guidance notification are not reachable in the shipped Tier-1 build** — this is by design (constitution "honest audio surface"), **not a bug**. The **mic-mode** line is the live, meaningful audio readout in this phase. Cases below are tagged accordingly.

Out of scope here: the microphone-profile **policy** and any endpoint **switching** (Phase 4); BLE battery / play-pause (Phase 2); packaging/About window (Phase 5); ANC/gestures/driver (Phases 6–7).

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast checks — PodBridge is a Windows tray app, not a web page.

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH`.
- **No administrator rights** — Tier 1 is driver-free; manifest is `asInvoker`.
- Run all commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/GitHub checks (§4.2, §5.7–5.8): the `gh` CLI authenticated against `bhemsen/PodBridge`.
- **For hardware feature tests (§5.3–5.6):** AirPods paired to this PC, a working Bluetooth radio, a media app (browser/Spotify), and a call/mic app (Teams/Zoom/Voice Recorder) to open a `Communications` capture session. *Not needed for build/verify* — the 72 unit tests use device-independent fakes.

> **Localization note:** build/test output here is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`); an English SDK prints `Build succeeded` / `Passed!`.

---

## 3. Build & Run

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

---

## 4. Automated checks (machine-verified baseline — do these first)

### 4.1 Verify gate

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (0/0), `format --verify-no-changes` clean, and `Bestanden!` / `Passed!` with **erfolgreich: 72, gesamt: 72**. This covers the device-independent audio suite: a fake `IAudioStateReader` drives `AudioGuidanceEngine` across **every** enum state (codec Sbc→advice, Aac→best-quality, Unknown→couldn't-determine; mic CallModeHfp→call-mode(mono), HighQualityA2dp→high-quality, Unknown→couldn't-determine), plus a machine-enforced honesty scan (no `apple`/`force aac`/`alternative a2dp driver`/`fdk-aac`/`parity`/`identical` in any string).

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

### 4.2 Static / repo inspections

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **AUDIO-TYPES** | `dir src\PodBridge.Core\Audio\CodecKind.cs src\PodBridge.Core\Audio\MicMode.cs src\PodBridge.Core\Audio\IAudioStateReader.cs src\PodBridge.Core\Audio\AudioGuidanceEngine.cs src\PodBridge.Windows\WindowsAudioStateReader.cs` | All exist. |
| **DI-REGISTRATION** | `findstr /C:"IAudioStateReader" src\PodBridge.Windows\ServiceCollectionExtensions.cs` | Binds `IAudioStateReader`→`WindowsAudioStateReader`. |
| **ARCHITECTURE-DOC** | `findstr /C:"IAudioStateReader" /C:"WindowsAudioStateReader" docs\architecture.md` | Named in the component map; noted read-only, codec Unknown in Tier-1, separate from Phase-4 `IAudioPolicy`. |
| **READ-ONLY-PROOF** | `findstr /I /C:"IAudioClient" /C:"SetDefaultEndpoint" /C:"IPolicyConfig" src\PodBridge.Windows\WindowsAudioStateReader.cs` | Appears **only** in "not-done" doc comments — the reader never opens a mic stream or switches an endpoint (opening the mic would itself force HFP). |
| **ASINVOKER-UNCHANGED** | `type src\PodBridge.App\app.manifest` | `level="asInvoker"`; no `requireAdministrator`. |
| **RESEARCH-COMMENTS** | `gh issue view 20 --repo bhemsen/PodBridge --comments`, then `... 21 ...` | #20 `## Research: Negotiated A2DP codec detection`; #21 `## Research: A2DP-vs-HFP (call) mode detection`. Docs under `docs/research/`. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 1` | Latest `verify` run on `main` = `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

**Reference — exact UI strings:**
- Context menu (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(sep)* · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(sep)* · `Exit`
- Codec line: `Codec: AAC (best available on Windows)` · `Codec: SBC` · `Codec: couldn't determine`
- Mic line: `Mic: High quality (A2DP)` · `Mic: Call mode (mono)` · `Mic: couldn't determine`
- SBC guidance notification body: `Audio is using the lower-quality SBC codec. To reach AAC, make sure you are on Windows 11 21H2 or later, update your Bluetooth adapter driver, and prefer an AAC-capable Bluetooth adapter or dongle.`
- Tooltip: `PodBridge — <status> · <battery>` (audio is **menu-only**, not in the tooltip)

---

### 5.1 Codec + mic lines present, correct menu order
- **Needs:** app only.
- **Action:** Launch the app, right-click the tray icon.
- **Expected:** The menu shows disabled `Status:`, `Battery:`, `Codec:`, `Mic:` lines (initially `—`), then a separator, `Refresh audio status`, `Pair / Reconnect`, `Open Bluetooth settings`, a separator, `Exit`.
- **Maps to:** issue #24.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 "Refresh audio status" re-reads on demand
- **Needs:** app only.
- **Action:** Click `Refresh audio status`, then re-open the menu.
- **Expected:** No crash; the codec + mic lines are (re-)populated from a fresh read. With no AirPods connected they read `Codec: couldn't determine` / `Mic: couldn't determine`.
- **Maps to:** issue #24 (manual refresh, no polling).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 Codec line is honest "couldn't determine" in Tier-1 (real AirPods)
- **Needs:** **real AirPods** connected.
- **Action:** Connect AirPods, open the menu (or click Refresh).
- **Expected:** `Codec: couldn't determine` — because Tier-1 (`asInvoker`) cannot read the elevated ETW codec source. **This is the correct, honest result** — mark PASS if you see "couldn't determine" (not FAIL). No `Codec: AAC`/`Codec: SBC` line and **no SBC notification** appears (both require an elevated codec read that Tier-1 deliberately does not do).
- **Maps to:** issue #23/#20; spec "honest Unknown", Risks row.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 Mic-mode line: high quality (A2DP) while playing media (real AirPods)
- **Needs:** **real AirPods** + a media app.
- **Action:** Connect AirPods, play stereo media through them, click `Refresh audio status`.
- **Expected:** `Mic: High quality (A2DP)` (no mic/call session active). PodBridge **does not switch** anything — display only.
- **Maps to:** issue #23/#21; spec Verification (mic-mode read).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 Mic-mode line flips to call mode when a mic app is active (real AirPods)
- **Needs:** **real AirPods** + a call/mic app (Teams/Zoom/Voice Recorder).
- **Action:** Open a call/mic app so it engages the AirPods microphone (a `Communications` capture session). Click `Refresh audio status`. Then release the mic (close the app / end the call) and Refresh again.
- **Expected:** With the mic engaged → `Mic: Call mode (mono)`; after releasing + Refresh → back to `Mic: High quality (A2DP)`. **PodBridge only displays this — it never switches the profile** (that is Phase 4). If the mode can't be determined it honestly shows `Mic: couldn't determine`.
- **Maps to:** issue #23/#21; spec human-QA gate (mic-mode display).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 No AirPods connected → neutral honest state, no crash
- **Needs:** app only (no AirPods).
- **Action:** With no AirPods connected, open the menu / click Refresh.
- **Expected:** `Codec: couldn't determine` and `Mic: couldn't determine`; no advice notification; no crash, no fabricated value.
- **Maps to:** issue #24; spec graceful degradation.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.7 Research comment on issue #20 (codec detection)
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 20 --repo bhemsen/PodBridge --comments`; cross-check against `src\PodBridge.Windows\WindowsAudioStateReader.cs`.
- **Expected:** `## Research: Negotiated A2DP codec detection` comment (Sources ≥3 / Consensus / Disputes); consensus = ETW `BthA2dp` needs elevation → Tier-1 returns `Unknown`; the reader hard-returns `CodecKind.Unknown` with no elevation/inference crutch. `docs/research/codec-detection.md` present.
- **Maps to:** issue #20; spec Verification (research artefact).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.8 Research comment on issue #21 (mic-mode detection)
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 21 --repo bhemsen/PodBridge --comments`; cross-check against `WindowsAudioStateReader.cs`.
- **Expected:** `## Research: A2DP-vs-HFP (call) mode detection` comment; consensus = read-only via `IAudioSessionManager2` capture-session state (active session ⇒ HFP), never opening a mic stream; the reader matches it. `docs/research/mic-mode-detection.md` present.
- **Maps to:** issue #21; spec Verification (research artefact).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 No elevation, honest audio surface (review + smoke)
- **Needs:** app + repo.
- **Action:** Confirm the app runs unelevated (Task Manager). Scan the audio strings.
- **Expected:** Runs as a normal user process, no driver. No user-facing string claims Apple-parity sound, recommends the paid "Alternative A2DP Driver", or offers a "force AAC" action (machine-enforced in §4.1).
- **Maps to:** constitution "honest audio surface"; spec Verification (no elevation).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

### Known residual limitations (documented, **not** defects)

- **Codec is `couldn't determine` in Tier-1.** Reading AAC vs SBC needs an elevated ETW trace; the driver-free/`asInvoker` tier honestly reports Unknown. A future explicit, opt-in elevated read could populate it (facts recorded in `docs/research/codec-detection.md`).
- **Mic-mode is inferred, read-only.** It's derived from active capture-session state; the Communications-render blind spot and adapter/Windows-build variance map to `couldn't determine`. PodBridge never opens the mic (that would itself force HFP) and never switches the profile.
- **Read cadence.** Audio state is read on device-connect and on manual "Refresh audio status" — not continuously polled; a mid-session change shows after a Refresh.

---

## 6. Recording results & regressions

- Mark each case `PASS` / `FAIL` (including §4) with notes.
- **On any FAIL:** file one `fix:` issue per finding in **milestone #3** (board `Todo`), with case number, observed vs. expected, OS build, repro. Re-run after the fix merges.
- **On full PASS:** Phase 3 QA is **accepted** — the spec (`docs/specs/archive/spec-audio-transparency.md`) is archived, roadmap links repointed, and **milestone #3 is closed**, unblocking **Phase 4 (Microphone-profile policy)**.

---

## 7. Cleanup

- Right-click the tray icon → **Exit**; confirm no `PodBridge.App.exe` remains (`tasklist /FI "IMAGENAME eq PodBridge.App.exe"`).
- If you opened a call/mic app for §5.5, close it.
