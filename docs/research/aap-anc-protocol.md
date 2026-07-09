# Research: AAP noise-control byte format (Off / ANC / Transparency / Adaptive)

> Permanent record for the `chore:research-aap-anc-protocol` issue (#39),
> Phase 6 (spec `docs/specs/spec-advanced-driver-anc.md`). Authority for the
> clean-room `AapProtocol` module (#41) and the echo-confirm UI (#44).
>
> **Clean-room:** every byte below is re-stated in our own words from the cited
> documented facts. No source code and no verbatim protocol-doc prose is copied
> from any GPL project (librepods, AirPodsDesktop) or from the MIT WinPods
> reference. Byte layouts are inherently factual constants; each is attributed.
>
> Scope: the **cleartext** AAP (Apple Accessory Protocol / AACP) control channel
> over Classic-Bluetooth **L2CAP** — only the **noise-control** command needed
> for Phase 6. Not MagicPairing-encrypted; no crypto is defeated. Gestures,
> conversational awareness, rename, etc. are out of scope (later phases).

## Sources

1. [librepods — `docs/AAP Definitions.md` (kavishdevar/librepods)](https://github.com/kavishdevar/librepods/blob/main/docs/AAP%20Definitions.md)
   — **primary reverse-engineering reference** (GPL-3.0; facts only). Its header
   states the packets were confirmed against **AirPods Pro 2 (USB-C), firmware
   `7A305`**, captured with **PacketLogger on an Intel Mac running macOS Sequoia
   15.0.1**. Documents: the 16-byte plaintext handshake, the "set specific
   features" packet, the "request notifications" packet, the noise-control
   set/notification packet, the mode enum, the echo-after-change behaviour, the
   Adaptive-requires-enable caveat, and the Adaptive-strength packet.
2. [librepods — Feature Reference (DeepWiki, generated from the librepods codebase)](https://deepwiki.com/kavishdevar/librepods/6-feature-reference)
   — corroborates, from the **code** rather than the docs, the noise-control set
   packet `04 00 04 00 09 00 0D [mode] 00 00 00`, the mode values
   (01/02/03/04), that everything rides **L2CAP PSM 0x1001**, and the
   Adaptive-strength sub-command `…09 00 2E [level]…`.
3. [WinPods — `driver/WinPodsAAP/README.md`, `src/WinPods.Core/AAP/AAPNoiseControl.cs`, `AAPModels.cs` (changcheng967/WinPods)](https://github.com/changcheng967/WinPods)
   — **independent MIT reimplementation** for Windows (reference-only, unverified
   on hardware). Independently states: AAP runs over Classic-BT **L2CAP PSM
   0x1001**, **not** BLE GATT, with **unencrypted** hex commands; a concrete
   set-ANC example `04 00 04 00 09 00 0D 02 00 00 00`; the packet shape
   `[header][opcode 0x0009][identifier 0x0D][mode][3 padding bytes]`; the mode
   enum `Off=0x01, NoiseCancellation=0x02, Transparency=0x03, Adaptive=0x04`
   with identifier `ListeningMode = 0x0D`; and confirmation-by-notification
   (the device sends a mode notification the app treats as the acknowledgement).
4. [Apple Support — "Active Noise Cancellation and Transparency modes for AirPods"](https://support.apple.com/en-us/108918)
   — **authoritative for the model/mode-support matrix** (which physical AirPods
   expose Off / Noise Cancellation / Transparency / Adaptive), independent of the
   protocol RE. Used only to pin model support, not byte values.

## Consensus

All multi-byte sequences are little-endian on the two 16-bit fields shown; bytes
are written MSB-left as they appear on the wire. `[x]` = a variable field.

### Transport

- **Channel:** the AAP control channel is Classic-Bluetooth **L2CAP, PSM
  `0x1001` (4097)** — not BLE/GATT. User-mode Windows cannot open this PSM, which
  is why Phase 6 needs the KMDF bridge driver. (Sources 2, 3.)
- **Encryption:** the control channel is **cleartext** — commands are plaintext
  hex frames; this is **not** the MagicPairing-encrypted path. (Source 3;
  consistent with Source 1 sending a plaintext handshake.)
- **Framing:** every control frame after the handshake begins with the 4-byte
  prefix **`04 00 04 00`** (the AAP data-message header). The command then
  follows. (Sources 1, 2, 3.)

### Startup sequence (must run once per connection, in order)

1. **Handshake** — 16 bytes, sent first; without it the AirPods ignore every
   later packet:
   ```
   00 00 04 00 01 00 02 00 00 00 00 00 00 00 00 00
   ```
   (Source 1. Note the distinct `00 00 04 00 01 00 …` prefix — the handshake does
   **not** use the `04 00 04 00` data header.)
2. **Set specific features** (optional but **required to unlock Adaptive**) —
   enables the Apple-silicon-gated features (Adaptive Transparency,
   conversational awareness) so the device will actually honour an Adaptive set:
   ```
   04 00 04 00 4D 00 FF 00 00 00 00 00 00 00
   ```
   (Source 1. If this is skipped, requesting Adaptive is echoed back as a
   different, non-Adaptive mode — see the echo caveat below.)
3. **Request notifications** — subscribes to inbound state notifications
   (battery, ear detection, **noise-control mode**, etc.); without it the device
   sends no notifications:
   ```
   04 00 04 00 0F 00 FF FF FE FF
   ```
   A variant `04 00 04 00 0F 00 FF FF FF FF` is documented to also work.
   (Source 1.)

### Noise-control SET packet (the Phase-6 command)

- **Layout (11 bytes):**
  ```
  04 00 04 00 09 00 0D [mode] 00 00 00
  ```
  - `04 00 04 00` — AAP data header (prefix). (Sources 1, 2, 3.)
  - `09 00` — the control/settings **opcode** (16-bit, little-endian → 0x0009).
    (Sources 2, 3.)
  - `0D` — the **setting identifier** for the listening/noise-control mode
    (WinPods names it `ListeningMode = 0x0D`). (Sources 1, 2, 3.)
  - `[mode]` — one **mode byte** (see enum). (Sources 1, 2, 3.)
  - `00 00 00` — three padding/reserved bytes. (Sources 1, 2, 3.)
- **Mode enum (`[mode]` byte)** — unanimous across all three technical sources:

  | Mode | Byte |
  |------|------|
  | Off | `01` |
  | Active Noise Cancellation (ANC) | `02` |
  | Transparency | `03` |
  | Adaptive (Adaptive Transparency / Adaptive Audio) | `04` |

- **Concrete example (set ANC):** `04 00 04 00 09 00 0D 02 00 00 00`. (Source 3.)

### Echo / confirmation packet (for the #44 echo-confirm UI)

- The AirPods **report the current noise-control mode using the identical
  layout** as the set packet:
  ```
  04 00 04 00 09 00 0D [mode] 00 00 00
  ```
  The same frame is emitted both **unsolicited** (when the mode is changed on the
  device itself, e.g. a stem long-press) and **as the acknowledgement** after a
  host set: "the AirPods respond with the same packet after the mode has been
  changed." (Source 1; Source 3 relies on this notification as the ack.)
- **Confirm logic for #44:** send the set packet, then wait for the inbound
  `09 00 0D` notification and compare its `[mode]` byte to the requested mode.
  Match ⇒ confirmed; **mismatch or timeout ⇒ revert** the optimistic UI.
- **Adaptive caveat (must handle):** if the model supports Adaptive but the
  "set specific features" packet (step 2) was **not** sent, requesting Adaptive
  (`04`) is echoed back as a **different** mode (e.g. `02`). The mismatch-detect
  above catches this and reverts, which is the correct honest behaviour. (Source 1.)

### Adaptive strength (adjacent; not required for basic switching)

- When the mode is Adaptive, the pass-through amount is a separate command:
  ```
  04 00 04 00 09 00 2E [level] 00 00 00
  ```
  `[level]` = `0x00`–`0x64` (0–100); only effective while the mode is Adaptive.
  (Sources 1, 2.) Out of Phase-6 scope (Phase 6 is Off/ANC/Transparency/
  Adaptive switching only) — recorded so it is not re-researched later.

### Model / firmware support

- **Byte format confirmed on:** AirPods Pro 2 (USB-C), firmware **`7A305`** — the
  Phase-6 reference model. (Source 1.) The bytes are firmware-fragile; broad
  model/firmware coverage is Phase 8, not this phase.
- **Mode availability by model** (Source 4, authoritative for the user-facing
  matrix; cross-checked against the protocol notes in Source 1):
  - **Off / ANC / Transparency:** AirPods Pro (1st gen), AirPods Pro 2, AirPods
    Max — all support these three.
  - **Adaptive (mode `04`):** **AirPods Pro 2 only** among the target models
    (also the ANC AirPods 4, out of scope). **Not** on the 1st-gen AirPods Pro,
    and **not** on AirPods Max. Phase 6 must therefore **gate Adaptive on the
    connected model** and only offer it where reported/supported (Pro 2), per the
    spec.

### Implementer summary (actionable for #41 / #44)

- PSM: `0x1001`. Handshake first; then request-notifications; send
  set-specific-features **before** ever offering Adaptive.
- Set: `04 00 04 00 09 00 0D <mode> 00 00 00`, `mode ∈ {01 Off, 02 ANC,
  03 Transparency, 04 Adaptive}`.
- Confirm: parse inbound `04 00 04 00 09 00 0D <mode> 00 00 00`; equal ⇒ commit,
  else/timeout ⇒ revert. Handle the Adaptive-not-enabled fallback echo.
- Gate Adaptive on model = AirPods Pro 2 (reference); firmware pinned to `7A305`.

## Disputes (minority → majority decision)

- **Independence / shared lineage.** Sources 1 and 2 are the same project
  (librepods docs vs. its code as read by DeepWiki); Source 3 (WinPods) is a
  separate MIT codebase but very likely derived its constants from librepods, and
  is itself flagged "unverified on real hardware." So the three technical sources
  are **not fully independent** — they share a common RE origin. **Decision:**
  treat the byte layout as **well-corroborated but single-lineage**; the
  load-bearing facts (PSM `0x1001`, set frame `…09 00 0D <mode>…`, enum
  01/02/03/04, echo-confirm) are stated **identically** by all three including an
  independent reimplementation and a concrete example, so we adopt them. Residual
  firmware-fragility risk is mitigated by pinning to Pro 2 fw `7A305` and
  centralising every constant in one cited `AapProtocol` module (spec + Phase 8).
- **Opcode notation `09 00` vs `0x0009` vs "0x09/0x0D".** Prior-art phrased it as
  "read+set via `0x09/0x0D`"; the byte-level sources show the field is the 16-bit
  little-endian opcode `09 00` (= `0x0009`) **followed by** the 1-byte setting
  identifier `0D`. **Decision:** these describe the **same wire bytes**
  `… 09 00 0D …`; document it as opcode `0x0009` + identifier `0x0D` to match the
  actual frame. No real disagreement.
- **How to confirm a change — read vs notification.** There is no separate
  request/response "read current mode" round-trip in the sources; confirmation is
  the **inbound notification** carrying the same `09 00 0D` frame (after
  request-notifications is subscribed). **Decision:** the "read/echo format" for
  #44 **is** that notification frame; implement confirm as
  notification-compare-with-timeout, not a polled read.
- **Which models get Adaptive.** Source 1 notes Adaptive is limited (Pro 2 / ANC
  AirPods 4, tested on Pro 2); Source 4 (Apple) is authoritative that AirPods Max
  and 1st-gen AirPods Pro expose only Off/ANC/Transparency. **Decision:** gate
  Adaptive to Pro 2 for Phase 6; do not offer it on Max or 1st-gen Pro.
