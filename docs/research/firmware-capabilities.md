# Research: AirPods firmware-read existence + (model, firmware-major) capability matrix

> Permanent record for the `chore:research-firmware-capabilities` issue (#51),
> Phase 8 (spec `docs/specs/spec-model-coverage-hardening.md`). Sole content
> authority for the capability-negotiation implementation issue and its Core
> `ICapabilityProvider` / `AapProtocol` additions (if any).
>
> **Clean-room:** every fact below is re-stated in our own words from the cited
> documented facts (opcode numbers, identifier values, model names are
> constants, not copyrightable prose). No source code and no verbatim
> protocol-doc prose is copied from any GPL project (librepods) or MIT
> reference (WinPods).
>
> Scope: (a) whether a firmware-version read exists on the **cleartext** AAP
> (Apple Accessory Protocol / AACP) control channel over Classic-Bluetooth
> **L2CAP** (PSM `0x1001`, per `docs/prior-art.md`), and (b) which Tier-2
> features (ANC/Transparency/Adaptive, gesture remap, conversation awareness)
> vary by (model, firmware-major). Not the MagicPairing-encrypted path; no
> crypto is defeated.

## TL;DR for the implementer

- **A firmware-version value does exist on the cleartext AAP channel, but there
  is no host-requestable "read" opcode.** Opcode `0x001D` ("Device
  Information") carries name/model/manufacturer/serial/firmware-version
  fields, but the accessory sends it **unsolicited, once, right after the
  handshake** — no opcode exists to request it on demand.
- Because `0x001D` only exists on the Tier-2 L2CAP transport (driver required),
  it was never a candidate for gating a driver-free **Tier-1** feature anyway.
  Tier-1 keeps gating on the BLE-derived model axis only, per the spec.
- Treat the `0x001D` read as **fragile**: a one-shot push that can be missed if
  the app isn't listening at the right moment, or absent if the driver isn't
  connected. On read failure, a known model must fall back to its **Phase-6/7
  model-level Tier-2 capability**, never to a blanket "assume unsupported".
- **No source in this research documents a case of firmware-major alone
  turning a whole Tier-2 feature on or off on otherwise-identical hardware.**
  Every documented capability difference (ANC/Transparency/Adaptive,
  Conversation Awareness, gesture-remap mechanism, HRM) tracks the **model**
  axis (chip/sensor generation), not firmware-major. The capability matrix's
  firmware-major dimension should ship as a no-op (every known firmware-major
  of a model maps to that model's Phase-6/7 capability) until real-hardware QA
  proves a genuine firmware-varying refinement.
- This confirms the spec's existing default design (Tier-1 = model axis only;
  firmware-major refines Tier-2 only, never gates a whole feature) rather than
  overturning it.

## Sources

1. [librepods `docs/opcodes.md`](https://github.com/librepods-org/librepods/blob/main/docs/opcodes.md)
   — the AACP top-level opcode table (GPL-3.0; facts only). Opcode `0x001D` is
   listed as "Device Information", destination "Host" (accessory → host).
2. [librepods `docs/device-info.md`](https://github.com/librepods-org/librepods/blob/main/docs/device-info.md)
   — states plainly that the `0x001D` packet "can not be requested from the
   accessory; it is only sent by the accessory to the host upon connection",
   and enumerates its fields: name, model number, manufacturer, serial number,
   two version fields, hardware revision, updater-app version, per-bud
   serials.
3. [librepods `docs/control_commands.md`](https://github.com/librepods-org/librepods/blob/main/docs/control_commands.md)
   — the full `0x0009` control-command identifier table (`0x01`-`0x41`),
   explicitly noted as "extracted from the iOS 19.1 Beta (23B5044l)'s
   bluetooth stack". Contains no "get/read firmware version" identifier —
   corroborates that no host-initiated request exists. Also documents `0x1A`
   ListeningModeConfigs (bitmask: Off `0x01`/ANC `0x02`/Transparency `0x04`/
   Adaptive `0x08`), `0x16` ClickHoldMode (gesture remap, per-bud), and `0x30`
   HRM enable/disable — used below for the model-capability table.
4. [changcheng967/WinPods `AAPDeviceInfo.cs`](https://github.com/changcheng967/WinPods/blob/main/src/WinPods.Core/AAP/AAPDeviceInfo.cs)
   — an independent MIT C# AAP client that models `GetFirmwareVersionAsync()`
   as an **active** `ControlCommand`/`FirmwareVersion` request-response call.
   Contradicts sources 1-3; `docs/prior-art.md` already flags this project's
   AAP/driver path as unverified against real hardware.
5. [librepods issue #612](https://github.com/kavishdevar/librepods/issues/612)
   — community feature request ("Research: AirPods Firmware Updates via
   Android (UARP)") whose own Phase 1 is "Detect current AirPods firmware
   version" — independent confirmation that even the most mature open AAP
   client does not yet have a working firmware-version read; it is an open
   research problem there too, not a solved one.
6. [librepods issue #288](https://github.com/kavishdevar/librepods/issues/288)
   — an unrelated bug report whose logs show the Linux/Rust client expecting
   an unsolicited `AirPodsInformation` packet immediately after L2CAP connect
   ("Expected AirPodsInformation … got something else") — independent
   behavioural confirmation that device/firmware info arrives as a one-time
   push on connect, not on request.
7. Apple Support — [Active Noise Cancellation and Transparency modes for
   AirPods](https://support.apple.com/en-us/108918) and [Adaptive Audio with
   your AirPods](https://support.apple.com/en-us/104979) — states Conversation
   Awareness and Adaptive Audio are exclusive to specific **models** (AirPods
   Pro 2, Pro 3, AirPods 4 with ANC, AirPods Max 2) and do not work on the
   original AirPods Pro regardless of update state — a hardware/model gate,
   not a firmware gate.
8. [MagicPods changelog](https://help.magicpods.app/changelog/) — the most
   feature-complete closed-source competitor differentiates its supported
   feature set by hardware model / chip generation (e.g. "AirPods Max 2",
   "Airoha AB1571AM"); no changelog entry gates a feature by firmware version.

## Consensus

### Firmware-version read: exists, but push-only

Opcode `0x001D` ("Device Information") rides the same plaintext L2CAP PSM
`0x1001` AAP channel as battery (`0x0004`) / in-ear (`0x0006`) / ANC (`0x0009`
+ `0x0D`) (Source 1; matches `docs/prior-art.md`'s already-documented PSM).
Its null-terminated string fields include a firmware/software version
(Source 2). Critically:

- The accessory sends this packet **unsolicited, once, right after the
  handshake** — there is no opcode a host can send to request it on demand
  (Source 2, explicit). `docs/control_commands.md`'s identifier table
  (`0x01`-`0x41`, sourced from an iOS Bluetooth-stack extraction) has no "read
  firmware" sub-command (Source 3), and an open community issue treats
  firmware-version detection as still-unsolved even in the reference client
  (Source 5). A bug report independently confirms the packet is *expected*
  immediately on connect, i.e. push, not pull (Source 6).
- `docs/prior-art.md`'s existing claim ("no firmware-version read opcode") is
  correct in the *active-request* sense but incomplete: a firmware value **is**
  obtainable, passively, by parsing the one-time `0x001D` packet after
  connecting over the Tier-2 transport.

### Gating model

- **Tier-1 features gate on the BLE-derived model axis only, never on
  firmware.** This is unaffected by the finding above: `0x001D` only exists on
  the Tier-2 L2CAP transport (requires the driver), so it was never usable for
  a driver-free Tier-1 gate in the first place.
- **Tier-2 features gate on driver presence AND (model, firmware-major)
  capability.** Firmware-major, when read succeeds, only ever *refines* Tier-2
  gating — it never gates Tier-1, and (per the Consensus below) it has no
  documented power to toggle a whole Tier-2 feature by itself.

### Firmware-unreadable fallback

A known model keeps its **Phase-6/7 model-level Tier-2 capability**, with no
regression of shipped ANC/gestures, whenever firmware-major can't be read
(driver absent, the one-shot `0x001D` packet missed/dropped before the app
starts listening, or an unrecognised firmware value). This holds regardless of
the existence of `0x001D`, because the read is a fragile one-shot push over a
driver-gated transport and must be treated as unreliable in practice — the
architecture does **not** need to degrade to model-only (an opcode does
exist), but it behaves identically to the model-only case until a firmware
read reliably succeeds. Only genuinely firmware-varying *refinements* on a
known model fall back to "assume unsupported"; no such refinement is confirmed
by any source in this pass (see table below), so today every firmware-major of
a known model should map to that model's Phase-6/7 capability with **no
firmware-varying refinements yet implemented**.

### (model, firmware-major) → Tier-2 capability

Best-effort matrix from documented facts. The **model** column is
well-corroborated (Sources 3, 7, 8); **no source documents a firmware-major-
specific entry** — i.e. no case where the same model gains or loses a whole
Tier-2 feature purely via a firmware bump.

| Model | ANC / Transparency / Adaptive | Conversation Awareness | Gesture-remap mechanism | Firmware-major refinements documented? |
|---|---|---|---|---|
| AirPods 2 | not applicable (no ANC hardware) | not supported, any firmware (Source 7) | double-tap function reassignment only | none found |
| AirPods 3 | not applicable (no ANC hardware) | not supported, any firmware (Source 7) | force-sensor stem press reassignment | none found |
| AirPods Pro (1st gen) | ANC + Transparency; **Adaptive Transparency not supported** (hardware-gated, Source 7) | not supported, any firmware (Source 7) | press-and-hold reassignment (`ClickHoldMode` `0x16`, Source 3) | none found beyond ordinary bug-fix firmware updates |
| AirPods Pro 2 | ANC + Transparency + Adaptive | supported | press-and-hold reassignment | one documented nuance (librepods `AAP Definitions.md`, cited in `docs/research/gesture-aap.md`): CA/Adaptive-Transparency responsiveness depends on a **host-capability-advertisement packet** (`0x004D`, host → accessory), not on the earbud's own firmware-major |
| AirPods Pro 3 | ANC + Transparency + Adaptive (assumed superset; not independently verified this pass) | supported per Source 7 | press-and-hold reassignment; `0x30` "HRM enable/disable" (Source 3) suggests a heart-rate sensor — a genuinely new **model**-gated capability, not firmware | insufficient public documentation — treat conservatively as unsupported until confirmed |
| AirPods Max | ANC + Transparency; Adaptive/Conversation Awareness only on the "Max 2" hardware refresh (Sources 7, 8), not the original Max | Digital Crown (`CrownRotationDirection` `0x1C`) replaces stem clicks entirely; no case (matches existing `docs/prior-art.md`) | none found |

## Disputes (minority → majority decision)

- **Does an active, host-requestable "read firmware version" opcode exist?**
  WinPods (Source 4) says yes (a `ControlCommand`/`FirmwareVersion`
  request-response identifier); librepods' opcode table, its explicit
  device-info.md statement, its iOS-stack-extracted `0x0009` identifier list,
  and an open librepods feature request for firmware detection (Sources 1, 2,
  3, 5), plus behavioural confirmation from an independent bug report
  (Source 6), all agree no such request exists and firmware is only ever a
  passive, one-time push -> **majority: no active read opcode; `0x001D` is
  accessory-initiated and unsolicited only.** WinPods' AAP/driver path is
  already independently flagged unverified against real hardware in
  `docs/prior-art.md`, consistent with treating it as the unconfirmed minority
  claim.
- **Is Tier-2 feature availability primarily a model fact or a firmware-major
  fact?** No source claims firmware-major alone toggles a whole
  ANC/Transparency/Adaptive/Conversation-Awareness/gesture-remap capability on
  otherwise-identical hardware; every source that discusses feature
  availability (Apple Support, MagicPods, librepods' capability-advertisement
  note) frames it by **model** (chip/sensor generation) instead -> **only
  position found in this research: model is the dominant gate; firmware-major,
  where it matters at all, only refines narrow behavioural details (e.g. CA
  responsiveness while audio is playing) — matching the spec's existing
  default that firmware-major refines Tier-2 only and is never itself a
  feature on/off switch.**
- **Is an AI-generated third-party summary (DeepWiki) of the librepods repo a
  reliable independent source for opcode facts?** Checked and found
  under-informative/hedged where the primary repo docs (Sources 1-2) are
  explicit about `0x001D` -> **decision: primary repository docs take
  precedence over AI-generated third-party summaries; DeepWiki is not cited as
  a Source above.**
