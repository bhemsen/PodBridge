# Research: AirPods Continuity model identifiers

> Permanent record for the `chore:research-model-ids` issue (#50). Authority for
> the Phase-8 Core model-registry implementation issue (#52). Clean-room: this
> file records reverse-engineered **facts** (2-byte identifier values, per-model
> shape) cross-checked across independent sources and re-described in our own
> words — **no GPL source code or verbatim documentation prose is reproduced**
> (constitution: clean-room protocol).
>
> Scope: the Apple-Continuity (`0x004C`) proximity-pairing advertisement's 2-byte
> **device-model** field (offsets 3–4 of the company-id-stripped blob, per
> `docs/research/continuity-parser.md`, issue #13/#14) → `AirPodsModel` mapping
> and per-model shape (dual-bud vs single, case present, in-ear support) for the
> vision's six target models (AirPods 2, 3, Pro, Pro 2, Pro 3, Max), plus the
> unknown-identifier generic fallback. This is a **Tier-1** (BLE-derived) fact,
> distinct from any Tier-2 (AAP/L2CAP) model-number string.

## Sources

1. [CAPoD (d4rken-org/capod)](https://github.com/d4rken-org/capod) — GPL-3.0,
   actively-maintained Android AirPods companion (updated Apr 2026). Dedicated
   `AirPodsX.kt` classes under `.../ble/devices/airpods/` each carry a private
   `DEVICE_CODE` `UShort` constant matched against the proximity-pairing
   advertisement's model field, plus a `PodModel` enum whose boolean feature
   flags (`hasDualPods`, `hasCase`, `hasEarDetection`, `hasAncControl`, …) give
   the per-model shape. Covers all six target models plus USB-C/newer variants
   (Pro 2 USB-C, Max USB-C, Max 2).
2. [OpenPods (adolfintel/OpenPods)](https://github.com/adolfintel/OpenPods) —
   GPL-3.0, Android, an independent codebase from CAPoD. `PodsStatus.java`
   contains a single `if/else` chain matching the same advertisement field as
   hex-string substrings; matches CAPoD's values for every model both projects
   cover, and defines a generic `RegularPods` fallback for unmatched identifiers.
3. [AirStatus (delphiki/AirStatus)](https://github.com/delphiki/AirStatus) —
   MIT, Python/Linux, a third independent implementation. Matches a single hex
   nibble at the same byte offset; corroborates AirPods 1/2/3, Pro, and Max
   (predates Pro 2/Pro 3, being an older/smaller tool).
4. [furiousMAC/continuity](https://github.com/furiousMAC/continuity) — academic
   BLE-Continuity reverse-engineering project (Celosia & Cunche, cited at
   Shmoocon 2020), independent of the three battery-tracker apps above. Its
   Wireshark-dissector patch carries a `device` value-table that corroborates
   the three oldest codes from a source that predates every later AirPods model.
5. [librepods device-info doc](https://github.com/librepods-org/librepods/blob/main/docs/device-info.md)
   — GPL-3.0-or-later. Documents that the AAP/L2CAP "Device Information" packet
   (opcode `0x001D`) carries a *separate*, string-form Apple model number (e.g.
   "A2564"); a Tier-2-only fact on a different transport/opcode that must not be
   conflated with the Tier-1 2-byte Continuity-advertisement code below.
6. [MagicPods supported headphones](https://magicpods.app/headphones/) —
   closed-source, behavioural cross-check only (no byte values obtainable).
   Confirms the current mainstream tool already targets all six vision models
   plus AirPods Max 2, corroborating that the model list below is complete for
   "currently shipping AirPods".

Cross-check against this repo's own prior research: `docs/research/continuity-parser.md`
(issue #13/#14, Phase 2) independently tabulated the same 2-byte field for the
same models (from sources 1/2 there = AirPodsDesktop + furiousMAC, sources 3/4 =
CAPoD + librepods' Android decoder) and its big-endian (`0xXX20`) column matches
every value below exactly — no conflict between the Phase-2 parser research and
this Phase-8 model-coverage research.

## Consensus

- **Field:** the AirPods model identifier is a 2-byte value at offsets 3–4 of
  the company-id-stripped Apple Proximity-Pairing (`0x004C`, type `0x07`) BLE
  advertisement. All three tracker apps agree on this position; CAPoD reads it
  as a big-endian `UShort`, OpenPods/AirStatus as an equivalent hex-string or
  nibble — same numeric value either way, no real disagreement. (The wire-byte
  order and the little-endian-vs-big-endian *read* convention are already
  settled in `docs/research/continuity-parser.md`; this file quotes the
  big-endian `0xXX20` form to match the four external sources above.)

- **Model-identifier → `AirPodsModel` (the six target models):**

  | Identifier (hex) | AirPodsModel | Shape |
  |---|---|---|
  | `0x0F20` | AirPods 2 | dual-bud, battery-reporting case, in-ear detection; no ANC |
  | `0x1320` | AirPods 3 | dual-bud, battery-reporting case, in-ear detection; no ANC |
  | `0x0E20` | AirPods Pro | dual-bud, battery-reporting case, in-ear detection, ANC/Transparency |
  | `0x1420` | AirPods Pro 2 (Lightning case) | dual-bud, battery-reporting case, in-ear detection, ANC/Transparency/Adaptive |
  | `0x2420` | AirPods Pro 2 (USB-C case) | identical shape to `0x1420`; same `AirPodsModel`, connector-only difference |
  | `0x2720` | AirPods Pro 3 | dual-bud, battery-reporting case, in-ear detection, ANC/Transparency/Adaptive (see Disputes) |
  | `0x0A20` | AirPods Max (Lightning) | single over-ear unit — **no** battery-reporting case (the "Smart Case" is a sleep cover, not a lid-sensing charging case); head on/off detection instead of in-ear; ANC/Transparency |
  | `0x1F20` | AirPods Max (USB-C) | identical shape to `0x0A20`; same `AirPodsModel`, connector-only difference |

- **Reference/context, outside the six-model scope:** `0x0220` = AirPods (1st
  gen, predates the vision's list). `0x2D20` = AirPods Max 2 (H2 chip, announced
  March 2026) — a newer model not in the vision's original six; see Disputes for
  how issue #52 should treat it.

- **Unknown-identifier fallback:** every source degrades gracefully instead of
  failing closed. CAPoD carries a `PodModel.UNKNOWN` value with a dedicated
  "unknown pod" UI card and a generic earbuds icon; OpenPods falls back to a
  generic dual-pod `RegularPods` type. Consensus for the registry: an
  unrecognized identifier should attempt best-effort dual-bud battery/in-ear
  parsing (assuming the standard proximity-pairing message length/shape) and
  surface as **"Unknown AirPods"** — never throw, never silently show nothing.

- **Tier-1 vs Tier-2 model facts are distinct:** the 2-byte Continuity
  identifier above is BLE-advertisement-derived (Tier-1, no driver needed). The
  AAP Device-Information packet's string-form model number (e.g. "A2698",
  opcode `0x001D`) is a different fact on a different transport, readable only
  via the Tier-2 L2CAP driver. The registry must key off the Tier-1 identifier
  only, per the spec's Tier-1-independence rule.

- **Shape used for capability gating:** all five dual-bud models (AirPods
  2/3/Pro/Pro 2/Pro 3) have a battery-reporting case and in-ear detection;
  AirPods Max has neither (unanimous — CAPoD's `AirPodsMax`/`AirPodsMax2`
  implement the single-unit interface with no case-related feature flag,
  OpenPods' `AirPodsMax` constructor takes no case pod). ANC/Transparency is
  present from AirPods Pro onward (Pro, Pro 2, Pro 3, Max) and absent on AirPods
  2 and 3, consistent across CAPoD's feature flags and MagicPods' published
  per-model feature grid.

## Disputes (minority → majority decision)

- **Point: is `0x2720` for AirPods Pro 3 an independently-confirmed identifier,
  or an extrapolation?** CAPoD's own in-repo comment describes it as following
  the established "…ends in `0x20`" pattern rather than citing an explicit
  capture → but OpenPods independently shipped the identical `0x2720` in its
  "initial support for AirPods Pro 3" commit within days of the Sept 19, 2025
  retail launch, and MagicPods lists AirPods Pro 3 as supported from around the
  same window — both consistent with real-device detection rather than a shared
  guess → **majority decision: adopt `0x2720`**, corroborated by two
  independently-maintained, actively-used trackers, but flag it for a
  real-hardware re-check at the Phase 8 human QA gate since neither source's
  write-up shows an explicit packet capture.
- **Point: does "AirPods Max" collapse to one `AirPodsModel`, or split by
  connector/generation?** OpenPods and AirStatus only ever matched a single Max
  code (they predate the USB-C refresh and Max 2) → CAPoD, the most current
  source (updated April 2026), distinguishes three: `0x0A20` (Lightning),
  `0x1F20` (USB-C), `0x2D20` (Max 2, H2 chip, announced March 2026) →
  **majority/most-current decision:** Lightning and USB-C Max codes are the
  same `AirPodsModel.Max` shape (connector-only difference, fold both into one
  enum value); AirPods Max 2 is a newer, higher-capability model outside the
  vision's original six-model list and is surfaced separately rather than
  silently folded in — left for issue #52 to decide whether it gets its own
  enum value or waits for a future phase.
- **Point: byte-numeric vs hex-string/nibble matching convention.** CAPoD
  compares a numeric `UShort`; OpenPods/AirStatus compare hex-string substrings
  or a single nibble → both conventions decode to the identical value for every
  model the sources share (e.g. string `"0F20"` == numeric `0x0F20`) → **no real
  disagreement**; restated as one field above rather than treated as a dispute.

## Implementation notes for #52 (Core `IModelRegistry`)

- Key the registry on the big-endian `0xXX20` identifier form to match this
  file and the existing `docs/research/continuity-parser.md` table (whichever
  byte-read convention the parser itself uses internally, the constant table it
  compares against must use the same order — already resolved in #13/#14).
- Fold each connector/case variant of a model (Pro 2 Lightning/USB-C, Max
  Lightning/USB-C) into a single `AirPodsModel` enum value; the six vision
  models are `AirPods2`, `AirPods3`, `AirPodsPro`, `AirPodsPro2`, `AirPodsPro3`,
  `AirPodsMax`.
- Any identifier not in the table (including `0x0220` AirPods 1st gen and
  `0x2D20` AirPods Max 2 until/unless a future phase adds them) resolves to the
  generic **"Unknown AirPods"** fallback — best-effort dual-bud parsing, no
  throw, model-specific (Tier-2) features disabled.
- Cite this file's table entry (and this issue) in the `AapProtocol`/registry
  source comment for every model-identifier constant, per the constitution's
  clean-room citation rule.
