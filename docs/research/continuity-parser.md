# Research: Apple-Continuity 0x004C proximity-pairing message layout

> Permanent record for the `chore:research-continuity-parser` issue (#13).
> Authority for the Phase-2 Core-parser implementation issue (#14). Clean-room:
> this file records reverse-engineered **facts** (byte offsets, bit positions,
> constants, semantics) cross-checked across independent sources and re-described
> in our own words — **no GPL source code or verbatim documentation prose is
> reproduced** (constitution: clean-room protocol).
>
> Scope: decoding the **BLE advertisement** Apple-Continuity *proximity-pairing*
> message (Apple company id `0x004C`, type `0x07`) into per-bud + case **battery**,
> per-bud **charging**, **in-ear / out-of-ear**, **lid** state, and **model id** —
> the driver-free Tier-1 telemetry path. This is **not** the L2CAP AAP battery
> opcode `0x04` / in-ear opcode `0x06` path (those need the Phase-6 driver).

## Sources

1. [furiousMAC/continuity — `messages/proximity_pairing.md`](https://github.com/furiousMAC/continuity/blob/master/messages/proximity_pairing.md)
   — a Wireshark-dissector field map of the `0x07` message: type, length, prefix
   `0x01`, 2-byte device model, status byte, the L/R battery byte (documented as
   high-nibble=right, low-nibble=left), the charging + case-battery byte, lid open
   counter, color, and the 16-byte encrypted tail. Roots its layout in the
   Celosia & Cunche academic reverse-engineering (source 5).
2. [SpriteOvO/AirPodsDesktop — `Source/Core/AppleCP.h` + `AppleCP.cpp`](https://github.com/SpriteOvO/AirPodsDesktop/blob/main/Source/Core/AppleCP.h)
   — a C++ struct + accessors for the same 27-byte message. The clearest statement
   of the **flip model**: fields are *current* (broadcasting pod) and *another*
   (the other pod), not fixed left/right, plus a status "broadcast-from" bit; also
   a full model-id constant table (little-endian `0x20xx` form).
3. [d4rken-org/capod — `.../ble/devices/DualApplePods.kt` + `ApplePods.kt`](https://github.com/d4rken-org/capod/blob/main/app/src/main/java/eu/darken/capod/pods/core/apple/ble/devices/DualApplePods.kt)
   — a Kotlin decoder: byte accessors, the `primary is left = status bit 5` flip,
   the extra `primaryLeft XOR this-pod-in-case` correction for the in-ear bits, the
   `0xF` unknown sentinel, and the lid-reliability caveat.
4. [kavishdevar/librepods — `android/.../bluetooth/BLEManager.kt`](https://github.com/kavishdevar/librepods/blob/main/android/app/src/main/java/me/kavishdevar/librepods/bluetooth/BLEManager.kt)
   — an independent Kotlin decoder that matches source 3 offset-for-offset and
   bit-for-bit: same flip, same in-ear XOR, same `0xF` sentinel; its `modelNames`
   table uses the byte-swapped big-endian (`0xXX20`) form of the same constants.
5. [Celosia & Cunche, "Discontinued Privacy: Personal Data Leaks in Apple
   Bluetooth-Low-Energy Continuity Protocols", PoPETs 2020(1)](https://petsymposium.org/2020/files/papers/issue1/popets-2020-0003.pdf)
   — the peer-reviewed reverse-engineering of the Continuity proximity-pairing
   message that the field maps above derive from (message type, length-prefixed
   framing, status/battery fields).

Sources 1–5 are GPL-family or academic and were consulted for **facts only**.

## Manufacturer-data framing

- Apple advertises under **company id `0x004C`** (little-endian on the wire:
  bytes `4C 00`) inside a Bluetooth *manufacturer-specific data* AD structure.
- The payload after the company id is a sequence of **Continuity TLVs**, each
  `[type][length][value…]`. The proximity-pairing entry is **type `0x07`**,
  **length `0x19` (25)**, followed by the 25-byte value → a 27-byte block
  (`type + length + 25`). AirPodsDesktop validates exactly this: `type == 0x07`
  and `remainingLength == 25`, total 27 bytes (sources 1, 2).
- On Windows/WinRT, `BluetoothLEAdvertisement.ManufacturerData` exposes each
  section as `BluetoothLEManufacturerData { CompanyId, Data }` with the **company
  id already stripped**, so `Data[0]` is the `0x07` type byte. (librepods'
  `getManufacturerSpecificData(76)` and AirPodsDesktop's parsed buffer use the
  same company-id-stripped view — offset 0 = the `0x07` type byte.)
- **Robustness note:** a real advertisement can concatenate several Continuity
  TLVs in one manufacturer-data blob. A robust parser should **scan the TLV chain
  for a `type == 0x07`, `length == 0x19` entry** rather than assume `Data[0] == 0x07`.

## Byte-offset table

Offsets are relative to the start of the company-id-stripped manufacturer-data
blob — i.e. **offset 0 = the `0x07` type byte** (= WinRT `BluetoothLEManufacturerData.Data[0]`).

| Offset | Len | Field | Meaning |
| :----: | :-: | ----- | ------- |
| 0 | 1 | Message type | `0x07` = proximity pairing; reject otherwise |
| 1 | 1 | Remaining length | `0x19` (25); total block = 27 bytes |
| 2 | 1 | Prefix / undefined | constant `0x01` |
| 3–4 | 2 | Device model id | 2 bytes; see *Model id* below for endianness |
| 5 | 1 | Status byte | in-ear / in-case / primary-side bit flags (below) |
| 6 | 1 | Pods battery | low nibble = *primary* pod, high nibble = *secondary* pod |
| 7 | 1 | Charging + case battery | low nibble = case battery; high nibble = charging bits |
| 8 | 1 | Lid byte | bits 0–2 = open/close counter; bit 3 = lid closed |
| 9 | 1 | Device color | e.g. `0x00` = white |
| 10 | 1 | Suffix / undefined | constant `0x00` (librepods reads it as a connection-state byte) |
| 11–26 | 16 | Encrypted / hashed tail | not used on the cleartext path (below) |

### Status byte (offset 5) — bit map (bit 0 = LSB)

| Bit | Meaning | Consensus |
| :-: | ------- | --------- |
| 0 | unused / unknown | — |
| 1 | **primary (current) pod in-ear** | 2, 3, 4 |
| 2 | **both pods in case** | 2, 3, 4 |
| 3 | **secondary (other) pod in-ear** | 2, 3, 4 |
| 4 | one pod in case | 3 (AirPodsDesktop: unknown) |
| 5 | **primary-is-left flag** (1 → left is primary/broadcasting; 0 → right) — *the flip bit* | 2, 3, 4 |
| 6 | **this (broadcasting) pod is in the case** (used for the in-ear XOR) | 3, 4 (AirPodsDesktop: unknown) |
| 7 | unused / unknown | — |

### Pods battery byte (offset 6) and case battery (offset 7 low nibble)

- **Nibble encoding (all three battery nibbles):** `0x0`–`0x9` → value × 10 %
  (0 %…90 %); `0xA` (10) → 100 %; **`0xF` (15) → unknown / absent sentinel.**
- Values `0xB`–`0xE` (11–14) are rarely observed and out of the normal range;
  sources 3 and 4 clamp them to 100 %, source 2 treats anything > 10 as
  unavailable. **Recommendation:** treat `0xF` as the unknown sentinel, map
  `0x0`–`0xA` to 0–100 % (× 10, capped at 100), and treat `0xB`–`0xE` as unknown
  (conservative) — see *Disputes*.
- Offset 6: **low nibble = primary (broadcasting) pod, high nibble = secondary
  pod** — mapped to physical left/right by the flip bit (below).
- Offset 7 **low nibble** = case battery, same encoding.

### Charging bits (offset 7 high nibble)

Within the high nibble (nibble bit 0 = byte bit 4):

| Nibble bit / byte bit | Meaning |
| :-------------------: | ------- |
| bit 0 / byte bit 4 | **primary (current) pod charging** |
| bit 1 / byte bit 5 | **secondary (other) pod charging** |
| bit 2 / byte bit 6 | **case charging** |
| bit 3 / byte bit 7 | unused |

### Lid byte (offset 8)

- bits 0–2: lid open/close **counter** (increments on open/close; glitchy, resets
  over time — sources 2, 3).
- bit 3: **lid closed** (1 = closed, 0 = open) → `lidOpen = (bit 3 == 0)` (sources 2, 3, 4).
- **Reliability caveat (source 3):** the closed bit is only trustworthy when the
  broadcasting pod is itself in the case (status bit 6) or both pods are in the
  case (status bit 2). A frame from the *out-of-case* pod carries a **stale** lid
  byte that can decode to a phantom "open" while the case is physically shut;
  treat such frames' lid state as unknown.

### Encrypted / hashed tail (offsets 11–26)

16 bytes of encrypted/hashed payload. On the cleartext Tier-1 path we **do not
touch it**. With the per-device pairing key (exchanged at pairing) it can be
decrypted to expose **1 %-granular** battery and richer state (source 3's
"private" payload) — that is out of scope for Phase 2 (and would require handling
the pairing key). Constitution: never defeat MagicPairing encryption — we only
read the cleartext nibbles.

## Left/right vs primary/secondary flip — RESOLVED

The proximity message has **no fixed "left" and "right" fields.** Only one earbud
advertises at a time; the two buds alternate (power saving / battery balancing —
source 2's note), and the currently-advertising bud is the **primary** ("current"),
the other is the **secondary** ("another"). The two battery nibbles, the two
charging bits, and the two in-ear bits are always ordered **primary, then
secondary** — so a naive "low nibble = left" reading is wrong whenever the right
bud is the current primary.

**Status bit 5 is the primary-side flag:**

- `bit 5 == 1` → **primary = left** (not flipped): left = primary/low-nibble,
  right = secondary/high-nibble.
- `bit 5 == 0` → **primary = right** (flipped): right = primary/low-nibble,
  left = secondary/high-nibble.

Define `isFlipped = (bit 5 == 0)`. Then (sources 2, 3, 4 agree exactly):

```
leftBatteryNibble  = isFlipped ? highNibble(offset6) : lowNibble(offset6)
rightBatteryNibble = isFlipped ? lowNibble(offset6)  : highNibble(offset6)
leftCharging       = isFlipped ? secondaryChargeBit  : primaryChargeBit
rightCharging      = isFlipped ? primaryChargeBit    : secondaryChargeBit
```

**In-ear needs an extra XOR with status bit 6** ("this broadcasting pod is in the
case"). Define `primaryLeft = (bit5 == 1)`, `thisInCase = (bit6 == 1)`,
`xorFactor = primaryLeft XOR thisInCase`. Then (sources 3, 4):

```
isLeftInEar  = xorFactor ? (status bit 3) : (status bit 1)
isRightInEar = xorFactor ? (status bit 1) : (status bit 3)
```

Source 2 omits the bit-6 XOR (it treats bit 6 as unknown) and instead **masks
in-ear off while that pod is charging** as a robustness filter. Both corrections
are defensive; applying the bit-6 XOR is the majority decision (see *Disputes*),
and gating in-ear on "not charging" is a reasonable additional guard.

## Model id (offsets 3–4)

Two published conventions describe the **same two wire bytes** — they differ only
in read order, not in the bytes:

- **Little-endian value** (source 2 AirPodsDesktop; the AAP-protocol constant
  family): `model = offset3 | (offset4 << 8)` → **`0x20xx`** constants.
- **Big-endian value** (sources 1, 3, 4): `model = (offset3 << 8) | offset4` →
  **`0xXX20`** constants (the byte-swap of the above).

Concretely, AirPods Pro on the wire = bytes `{offset3 = 0x0E, offset4 = 0x20}` →
`0x200E` little-endian, `0x0E20` big-endian.

| Model | LE (`0x20xx`) | BE (`0xXX20`) |
| ----- | :-----------: | :-----------: |
| AirPods (1st gen) | `0x2002` | `0x0220` |
| AirPods (2nd gen) | `0x200F` | `0x0F20` |
| AirPods (3rd gen) | `0x2013` | `0x1320` |
| AirPods 4 | `0x2019` | `0x1920` |
| AirPods 4 (ANC) | `0x201B` | `0x1B20` |
| AirPods Pro | `0x200E` | `0x0E20` |
| AirPods Pro 2 | `0x2014` | `0x1420` |
| AirPods Pro 2 (USB-C) | `0x2024` | `0x2420` |
| AirPods Pro 3 | `0x2027` | `0x2720` |
| AirPods Max (Lightning) | `0x200A` | `0x0A20` |
| AirPods Max (USB-C) | `0x201F` | `0x1F20` |
| Beats Fit Pro | `0x2012` | `0x1220` |

(LE table from source 2; BE table from source 4; entries are consistent
byte-swaps of each other. Pro 3 `0x2027` and Beats Fit Pro `0x2012` are from
source 2; Max USB-C `0x201F`/`0x1F20` from source 4.)

**Recommendation:** read **little-endian** and use the `0x20xx` table — it matches
AirPodsDesktop and the AAP protocol's own model constants, and keeps one
convention across the codebase. Whichever convention is chosen, the constant
table must match the read order.

## Disputes (minority → majority decision)

- **Fixed nibbles vs. flip.** *Minority:* source 1 (furiousMAC) documents fixed
  fields — `leftbattery` = low nibble, `rightbattery` = high nibble, with no flip.
  *Majority:* sources 2, 3, 4 all decode the nibbles as **primary/secondary** and
  map to left/right via **status bit 5**. → **Decision: apply the flip (status
  bit 5).** furiousMAC's fixed labels are correct only when the left bud is the
  primary (the common case), so it is a simplification, not a contradiction of the
  byte positions.
- **In-ear bit-6 XOR.** *Minority:* source 2 omits it (treats bit 6 as unknown)
  and instead masks in-ear while charging. *Majority:* sources 3, 4 apply
  `xorFactor = primaryLeft XOR thisPodInCase` before selecting status bit 1/3. →
  **Decision: apply the bit-6 XOR (majority);** optionally also gate in-ear on
  "not charging" as an extra guard.
- **Battery values `0xB`–`0xE` (11–14).** *Minority:* source 2 treats anything
  > 10 as unavailable. *Majority:* sources 3, 4 clamp 11–14 to 100 %. All agree
  `0xA` (10) = 100 % and **`0xF` (15) = unknown**. → **Decision: `0xF` is the
  unknown sentinel; map `0x0`–`0xA` to 0–100 %; treat `0xB`–`0xE` as unknown
  (conservative).**
- **Model-id endianness.** Two conventions (`0x20xx` LE vs `0xXX20` BE) for the
  **same bytes** — not a genuine disagreement about the wire. → **Decision: read
  little-endian, use the `0x20xx` table** (matches source 2 / AAP constants).
- **Status bits 4 and 6.** *Minority:* source 2 marks bits 4 and 6 as unknown.
  *Majority:* sources 3, 4 use bit 6 = "this pod in case" (needed for the in-ear
  XOR) and source 3 uses bit 4 = "one pod in case". → **Decision: bit 6 = this
  (broadcasting) pod in case; bit 4 = one pod in case.**

## Implementation notes for #14 (Core `ContinuityParser`)

- Parse from the company-id-stripped blob; validate `type == 0x07` **and**
  `length == 0x19` (25) and total 27 bytes before decoding; scan the TLV chain for
  the `0x07` entry rather than assuming it is first.
- Decode primary/secondary, then resolve left/right with the bit-5 flip (battery,
  charging) and the bit-5 ⊕ bit-6 flip (in-ear), per above.
- Map each battery nibble via the `0xF` → unknown rule; surface an explicit
  "unknown / out of range" state (spec) rather than a fake 0 %.
- Treat the lid closed bit as reliable only for in-case broadcasts (bit 6 or
  bit 2); otherwise report lid state unknown.
- Do not read or attempt to decrypt offsets 11–26 (encrypted tail).
- Annotate every offset/constant in the parser with the documented fact it comes
  from (constitution: clean-room protocol), citing this file.
