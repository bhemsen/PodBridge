# Research: AAP gesture / stem-press remap byte format + reconnect-overwrite

> Permanent record for the `chore:research-gesture-aap` issue (#46),
> Phase 7 (spec `docs/specs/spec-gesture-remap.md`). Sole content authority for
> the gesture-remap implementation issue (#48) and its clean-room `AapProtocol`
> gesture builders.
>
> **Clean-room:** every byte below is re-stated in our own words from the cited
> documented facts. No source code and no verbatim protocol-doc prose is copied
> from any GPL project (librepods) or MIT reference. Byte layouts are inherently
> factual constants; each is attributed to its source.
>
> Scope: the **cleartext** AAP (Apple Accessory Protocol / AACP) control channel
> over Classic-Bluetooth **L2CAP** — only the **press-and-hold / stem-gesture
> remap** commands needed for Phase 7. Not the MagicPairing-encrypted path; no
> crypto is defeated. Reuses the Phase-6 transport/framing (`AapProtocol`,
> `docs/research/aap-anc-protocol.md`).

## TL;DR for the implementer (#48)

- The remappable gesture on the reference hardware is the **press-and-hold**,
  encoded by control-command **identifier `0x16` (ClickHoldMode)** under the
  settings **opcode `0x0009`** — **not** three separate remappable gestures.
- Frame (11 bytes): `04 00 04 00 09 00 16 [right] [left] 00 00` — **per-bud**,
  `data1 = RIGHT` bud, `data2 = LEFT` bud.
- Documented settable actions: `0x01` = **Noise Control**, `0x05` = **Siri /
  voice assistant**. No other action values are documented → ship only these.
- `SingleClickMode 0x14` / `DoubleClickMode 0x15` exist as identifiers but carry
  **no documented action values** and single/double/triple presses are **fixed**
  (play-pause / next / previous) in Apple's own model → **do not expose them**
  (spec: "no invented actions").
- The set of noise-control modes the press-and-hold cycles through (when the
  action is Noise Control) is a **separate** bitmask command, identifier `0x1A`
  (ListeningModeConfigs): Off=`0x01`, ANC=`0x02`, Transparency=`0x04`,
  Adaptive=`0x08`.
- **Reconnect-overwrite = YES.** The AirPods do **not** persist a third-party
  (non-iCloud-synced) host's control-command config across disconnect. The app
  **must re-push** the stored gesture config on **every L2CAP connect + AAP
  handshake-complete** (the Phase-7 `IAapTransport` (re)connect event), not the
  BLE connection event.

## Sources

1. [librepods — `docs/control_commands.md` (kavishdevar/librepods)](https://github.com/kavishdevar/librepods/blob/main/docs/control_commands.md)
   — **primary byte-level reference** for the settings channel (GPL-3.0; facts
   only). Documents the control-command frame
   `04 00 04 00 09 00 [identifier] [data1..4]`, the per-bud rule (`data2` is used
   when the two buds can differ), and the identifier table: `0x14` SingleClickMode,
   `0x15` DoubleClickMode, `0x16` ClickHoldMode (per-bud; `data1` right, `data2`
   left; `0x01` Noise control / `0x05` Siri), `0x17` DoubleClickInterval, `0x18`
   ClickHoldInterval, `0x1A` ListeningModeConfigs (bitmask Off `0x01`/ANC `0x02`/
   Transparency `0x04`/Adaptive `0x08`), `0x39` Raw Gestures config. Its footnote
   states the identifiers were extracted from the **iOS 19.1 Beta (23B5044l)**
   Bluetooth stack.
2. [librepods — `docs/AAP Definitions.md` (kavishdevar/librepods)](https://github.com/kavishdevar/librepods/blob/main/docs/AAP%20Definitions.md)
   — RE reference whose header pins packets to **AirPods Pro 2 (USB-C), firmware
   `7A305`**, captured via **PacketLogger on an Intel Mac / macOS Sequoia
   15.0.1**. Confirms PSM `0x1001`, and — in its "Configure Stem Long Press"
   section — the `04 00 04 00 09 00 1A [mask] 00 00 00` listening-mode-cycle
   packets **and the reconnect-overwrite behaviour** (see Consensus).
3. [librepods — `docs/opcodes.md` (kavishdevar/librepods)](https://github.com/kavishdevar/librepods/blob/main/docs/opcodes.md)
   — the **top-level opcode** table (a different field from the control-command
   identifiers): `0x0009` Control commands, `0x0014` "Send connected device MAC",
   `0x0019` Stem press (inbound, Host destination). Used to **disambiguate**
   opcodes from control-command identifiers (see Disputes).
4. [librepods — Feature Reference (DeepWiki, generated from the librepods codebase)](https://deepwiki.com/kavishdevar/librepods/6-feature-reference)
   — code-derived corroboration: stem-press configuration is **per-earbud**, with
   an action set including Noise-Control-cycle, voice assistant, media controls,
   and "nothing". (Code-level; its internal action numbers are **not** the wire
   bytes — the wire truth is Source 1.)
5. [d4rken-org/capod — Wiki FAQ](https://github.com/d4rken-org/capod/wiki/FAQ)
   — an **independent** Android AirPods RE project (separate lineage from
   librepods). States that CAPod 5.0+ can control "Stem actions, press speed, and
   press-hold duration", model-dependent — independent corroboration of the
   ClickHoldMode / DoubleClickInterval / ClickHoldInterval command family.
6. [Apple Support — "Use controls and gestures with your AirPods"](https://support.apple.com/guide/airpods/use-controls-and-gestures-with-your-airpods-devb2c431317/web)
   — **authoritative for the user-facing control model**, independent of the
   protocol RE: single/double/triple press are **fixed** (play-pause / next /
   previous); **press-and-hold** is the only user-reassignable control, settable
   **per earbud**, to Noise Control or Siri (AirPods Max: press-and-hold the
   Digital Crown = Siri only).

## Consensus

Frames are written MSB-left as they appear on the wire; `[x]` = a variable field.
The two 16-bit fields (`04 00`, `09 00`) are little-endian. This reuses the
Phase-6 transport (Source 2, Source 3, and `docs/research/aap-anc-protocol.md`):
Classic-BT **L2CAP PSM `0x1001`**, cleartext, 4-byte data header `04 00 04 00`,
16-byte handshake + request-notifications sent first per connection.

### Control-command frame (the settings channel)

- **Opcode `0x0009` = "Control commands"** carries all the settings; the frame is
  fixed-length: header `04 00 04 00` + `09 00` opcode + a **1-byte identifier** +
  four data bytes → 11 bytes total:
  ```
  04 00 04 00 09 00 [identifier] [data1] [data2] [data3] [data4]
  ```
  `data3`/`data4` are always `0x00`; **`data2` is used only when the setting can
  differ per bud** (e.g. ClickHoldMode). (Source 1.)

### Gesture-remap command — ClickHoldMode (identifier `0x16`)

- **The press-and-hold action is control-command identifier `0x16`
  (ClickHoldMode)**, and it is **per-bud** — the one gesture Apple lets the user
  reassign. (Sources 1, 6.)
- **Layout (11 bytes):** `04 00 04 00 09 00 16 [right] [left] 00 00`
  - `04 00 04 00` — AAP data header. (Sources 1, 2.)
  - `09 00` — control-commands opcode (LE `0x0009`). (Sources 1, 3.)
  - `16` — ClickHoldMode identifier. (Source 1.)
  - `[right]` = `data1` = **right** bud action; `[left]` = `data2` = **left** bud
    action. Byte order is explicit: first byte right, second byte left. (Source 1.)
  - `00 00` — `data3`/`data4`, reserved. (Source 1.)
- **Action enum (documented, settable):** `0x01` = **Noise Control**,
  `0x05` = **Siri / voice assistant**. These are the only documented values;
  no other action byte is attested. (Source 1; corroborated as the user options
  by Source 6.)
- **Shared (non-per-bud) fallback:** where a model does not advertise
  independent per-bud assignment, set both `[right]` and `[left]` to the same
  action byte (matches the spec's shared-assignment fallback).

### Non-remappable / adjacent commands (record, do not ship as gestures)

- **`0x14` SingleClickMode, `0x15` DoubleClickMode** — identifiers exist in the
  iOS-stack table but carry **no documented action values**, and single/double/
  triple presses are **fixed** (play-pause / next / previous) in Apple's model.
  Do **not** expose them (no invented actions). (Sources 1, 6.)
- **`0x1A` ListeningModeConfigs** — single-byte **bitmask** selecting which
  noise-control modes the press-and-hold (when set to Noise Control) cycles
  through: Off=`0x01`, ANC=`0x02`, Transparency=`0x04`, Adaptive=`0x08` (OR the
  bits). Frame `04 00 04 00 09 00 1A [mask] 00 00 00`; e.g. `0x0B` = Off+ANC+
  Adaptive. Governs the cycle set, not the gesture action itself; adjacent to the
  ClickHold=Noise-Control case. (Sources 1, 2.)
- **`0x17` DoubleClickInterval, `0x18` ClickHoldInterval** — press-**timing**
  sensitivity (`0x00` default / `0x01` slower / `0x02` slowest), **not** action
  remaps. (Sources 1, 5.)
- **`0x39` Raw Gestures config** — bitmask enabling/disabling which press counts
  are recognised (single `0x01`/double `0x02`/triple `0x04`/long `0x08`); an
  enable mask, not an action assignment. (Source 1.)
- **`0x0019` Stem press** is a top-level **inbound** opcode (device→host press
  event), not a settings write. (Source 3.)

### Reconnect-overwrite behaviour (critical for #48)

- **The device does not retain a third-party host's control-command config
  across disconnect.** Source 2's "Configure Stem Long Press" section states the
  config is overwritten whenever the AirPods connect to a device that is not the
  iCloud-synced owner, so a non-Apple host **must store the config and re-send
  (overwrite) it every time the AirPods connect** — re-pushing the stored config
  is the only way it stays applied on a non-Apple host, and the previous state
  must be known before the new state can be set. (Source 2.)
- **Trigger:** re-push on the **L2CAP connect + AAP handshake-complete** event
  (the Phase-7 `IAapTransport` (re)connect event), **not** the Tier-1 BLE
  connection event — the overwrite happens after the Classic-BT/AAP reconnect.
  (Matches the spec's re-push-trigger decision.)
- **Corroboration:** Source 5 (independent) re-sends configuration on
  service/connection start, consistent with "config not persisted by the device
  for third-party hosts".
- **Confidence:** the source **hedges the exact cause** ("i think" — whether it
  is iCloud-sync vs owner-device state), but states the **re-push requirement**
  plainly; the safe, source-supported conclusion is: always re-push on reconnect.
  Verified on real hardware at the Phase-7 QA gate (spec Verification).

### Model / firmware support

- **Byte format confirmed on:** AirPods Pro 2 (USB-C), firmware **`7A305`**
  (Source 2 header); identifier table from **iOS 19.1 Beta (23B5044l)** (Source
  1). Firmware-fragile — broad model/firmware coverage is **Phase 8**.
- **Per-bud press-and-hold = Noise Control / Siri** applies to AirPods Pro 2 /
  Pro 3 and ANC AirPods 4; **AirPods Max** press-and-hold (Digital Crown) is
  **Siri-only**. (Source 6.) Gate the feature on the connected model (Pro 2
  reference); hide it where the model does not advertise it (spec + Phase 8).

### Implementer summary (actionable for #48)

- Reuse Phase-6 transport: PSM `0x1001`; send handshake + request-notifications
  first; frames are `04 00 04 00 09 00 <id> <d1> <d2> <d3> <d4>`.
- Gesture SET (press-and-hold, per-bud):
  `04 00 04 00 09 00 16 <right> <left> 00 00`, action ∈ {`01` Noise Control,
  `05` Siri}. Shared fallback ⇒ `right == left`.
- Optional cycle-set (only when action = Noise Control):
  `04 00 04 00 09 00 1A <mask> 00 00 00`, mask OR of {Off `01`, ANC `02`,
  Transparency `04`, Adaptive `08`}.
- Do **not** implement `0x14`/`0x15` action remaps (no documented values; Apple
  fixes single/double/triple).
- Persist the per-bud action map and **re-push it on every `IAapTransport`
  (re)connect / handshake-complete** event.
- Gate on model = AirPods Pro 2 (reference); firmware pinned to `7A305`.
- Confirm each write with the Phase-6 write+echo pattern (reuse; a missing echo ⇒
  non-fatal "couldn't apply" + single retry).

## Disputes (minority → majority decision)

- **Opcode vs control-command identifier (the `0x14` collision).** Source 3 lists
  top-level opcode `0x0014` = "Send connected device MAC"; Source 1 lists
  control-command **identifier** `0x14` = "SingleClickMode". These are **different
  fields at different offsets** — the top-level opcode sits right after
  `04 00 04 00`, while the gesture `0x14/0x15/0x16` are **identifiers under opcode
  `0x0009`**. **Decision:** the prior-art/spec "gesture remap `0x14/0x15/0x16`"
  are control-command **identifiers** (SingleClickMode/DoubleClickMode/
  ClickHoldMode), not opcodes; there is no real conflict.
- **"Three remappable gestures" (spec shorthand) vs one (press-and-hold).** The
  spec's Prior decision names SingleClick/DoubleClick/ClickHold as the exposed
  set (minority reading: three action-remappable gestures). The byte-level source
  (S1) documents settable **values** and **per-bud** addressing only for
  ClickHoldMode (`0x16`); SingleClickMode/DoubleClickMode have no documented
  values, and Apple (S6) fixes single/double/triple to media controls (majority).
  **Decision:** implement **ClickHoldMode (`0x16`) per-bud** remap
  (Noise Control / Siri) only; treat `0x14`/`0x15` as documented-but-not-settable
  and hide them. This is **settled within the spec**, whose Prior decision says
  "the exposed action set is exactly what the research comment confirms is
  settable … no invented actions" — so this narrows scope, it is **not** an
  unresolved design fork.
- **Per-bud byte order.** S1 is explicit: `data1` = right bud, `data2` = left bud.
  S4 (code) stores order-agnostic `left_/right_` keys. **Decision:** on the wire,
  **right = `data1`, left = `data2`** (the byte-level doc wins); the device-
  independent unit test asserts this order.
- **Reconnect mechanism certainty.** S2 hedges the *cause* of the overwrite
  ("i think", iCloud-sync vs owner state) but states the *requirement* plainly;
  S5 (independent) re-sends on connect. **Decision:** treat **re-push on every
  reconnect** as required (safe + source-supported); confirm on hardware at the
  QA gate. A "needs real hardware to confirm" is not a blocker — implement and
  defer to QA.
- **Source independence / lineage.** S1–S4 are one project (librepods docs +
  code); S5 (capod) is an independent RE lineage and S6 (Apple) is the official
  user model. The load-bearing facts — press-and-hold is the remappable per-bud
  control, Noise Control vs Siri — are corroborated **across all three
  lineages**. The exact byte values (`0x16`, `0x01`/`0x05`, right-then-left) are
  single-lineage (S1); mitigated by pinning to Pro 2 fw `7A305`, centralising
  every constant in one cited `AapProtocol` builder, and the hardware QA gate.
