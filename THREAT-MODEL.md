# Threat model

> Scope: PodBridge as shipped by Release 1.0 (`docs/specs/spec-release-1.0.md`) —
> a self-contained, single-file `.exe` downloaded from GitHub Releases and run
> directly, no MSIX/Store, no installer, no admin. This document names the
> attack surfaces that follow from that shape and the mitigation already in the
> tree (or tracked in a sibling issue) for each. It is descriptive, not
> aspirational: every mitigation cited here is either implemented today or
> explicitly attributed to the issue that implements it.

## Posture

- **Local-only.** PodBridge makes no network calls except an explicit,
  user-visible update check (`docs/vision.md`, `docs/constitution.md`). It reads
  local BLE advertisements and talks to the local audio/BLE stack; it does not
  phone home, does not upload diagnostics, and has no telemetry.
- **No admin, by default.** The app manifest requests `asInvoker`; Tier 1 (the
  default feature set — battery, play/pause, mic-policy) never elevates and
  never installs a driver.
- **No driver, by default.** The Tier-2 kernel driver (`driver/PodBridgeAAP`,
  optional L2CAP bridge for noise-control/gesture features) is a separate,
  explicit opt-in install (`AdvancedTierInstaller`); it is never installed or
  elevated silently, and every Tier-1 feature keeps working with it absent.
- **Portable, not installed.** Release 1.0 ships a single downloaded `.exe run
  directly — there is no installer, no registry footprint beyond what the app
  itself opts into (mic-policy/gesture settings, the optional auto-start Run
  key), and removal is "delete the file."

Given that shape, two attack surfaces matter: **(A)** untrusted bytes PodBridge
parses from nearby Bluetooth devices, and **(B)** the download/release supply
chain the user trusts to get a genuine, unmodified exe.

## Surface A — untrusted BLE advertisement bytes

**Threat.** PodBridge's Tier-1 telemetry path (`PodBridge.Windows`'s BLE
advertisement watcher → `PodBridge.Core.Protocol.ContinuityParser`) decodes
manufacturer-data bytes broadcast in the clear by *any* nearby Bluetooth LE
device, not only a paired AirPods unit. Those bytes are attacker-controlled:
anyone within BLE range can advertise a crafted length/count/type field. This is
not a hypothetical category — **CVE-2023-24871** was exactly this class of bug
in *Windows' own* BLE advertisement parser: an 8-bit section-counter overflow
produced an undersized heap buffer and an out-of-bounds write from a nearby
device's advertisement alone, no pairing or user action required
(<https://ynwarcs.github.io/z-btadv-cves>). PodBridge's own TLV-walking code
(`ContinuityParser.TryFindProximityBlock`, which advances through
attacker-supplied `type`/`length` bytes) is the directly analogous surface in
this codebase.

**Mitigation.**

- `ContinuityParser` is a **fixed-offset, bounds-checked decoder**, not a
  variable-length structure walk with implicit trust: every field read is
  preceded by an explicit `i + ProximityBlockLength <= data.Count` bounds check
  before any offset into the block is dereferenced
  (`src/PodBridge.Core/Protocol/ContinuityParser.cs`), and the TLV-chain
  advance (`i += 2 + length`) only ever *skips* unrecognised entries — it never
  indexes into them.
- Only the **cleartext** proximity-pairing TLV (type `0x07`) is decoded. Offsets
  11–26 (the encrypted/hashed tail) are never read — decrypting them would
  require the MagicPairing key, which PodBridge never attempts to defeat
  (constitution).
- `ContinuityParserFuzzTests` (`tests/PodBridge.Core.Tests/Protocol/
  ContinuityParserFuzzTests.cs`) property-tests the parser against: fully
  random payloads of every length 0–299 bytes (20,000 iterations); every byte
  value 0x00–0xFF at each decoded offset; every known model's fixture truncated
  at every possible length; every known model's fixture with 1–200 bytes of
  random trailing garbage; and — added by this document's companion change —
  explicit assertions that a malformed `length` byte (truncated after the
  length byte, boundary values `0x00`/`0x19`/`0xFF`, and an over-long value
  that would walk past the buffer) never throws and never reads past the end
  of the supplied span. The invariant under test in every case is: **no
  throw, no out-of-bounds access, and a decoded battery value is always either
  `null` or in `0..100`.**
- The parser never allocates a buffer sized from an attacker-controlled field
  (unlike CVE-2023-24871's undersized-heap-buffer pattern) — it only ever reads
  from the caller-supplied, already-bounded `IReadOnlyList<byte>` passed in by
  the WinRT advertisement-watcher adapter.

**Residual risk.** A crafted advertisement can still cause `TryParse` to return
`false` (rejected) or, if it happens to satisfy the length/type check with
garbage payload bytes, a nonsensical-but-in-range battery/status reading —
never a crash, memory-safety violation, or code execution. This mirrors the
Windows CVE's *class* of input without its consequence, because C#'s managed
array/list indexing throws a catchable `IndexOutOfRangeException` rather than
corrupting memory on an out-of-bounds access, and every decode path here is
additionally bounds-checked before that could even occur.

## Surface B — the download/release supply chain

**Threat.** Release 1.0 ships a bare `.exe` from GitHub Releases with no
installer and (per the signing decision in `docs/specs/spec-release-1.0.md`)
possibly no code-signing certificate. A user has no first-party way to confirm
the file they downloaded is the one PodBridge's CI actually built, and an
unsigned exe is trivially easy to re-host or tamper with off-repo (fake
mirrors, phishing pages offering a "PodBridge installer").

**Mitigation (delivered by #122, the release workflow).**

- **Build-provenance attestation**: the release workflow calls
  `actions/attest-build-provenance` over each published exe, producing a signed
  record tying the binary to the exact repo/workflow/commit that built it;
  users verify with `gh attestation verify <exe> -R bhemsen/PodBridge`, which
  fails closed if the binary is not a genuine CI build. This is strictly
  stronger than a same-page checksum because it is cryptographically bound to
  the build, not just self-reported.
- **Published SHA-256 checksums** (`checksums.sha256` alongside each release
  asset) so a user can verify integrity even without the `gh` CLI.
  GitHub also now exposes an immutable per-asset digest itself.
  SBOM (CycloneDX for .NET) attached to the release so dependencies are
  auditable independent of the source tree.
- Workflow hardening (least-privilege `permissions:` blocks, third-party
  actions pinned to full commit SHAs) so the workflow producing the
  attestation is itself not the weak link.

This document does not claim these controls exist before #122 lands; it names
them here so the threat model is complete for the release shape even though
the implementing work is tracked separately.

## Single-file extraction directory (`%TEMP%\.net`)

`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` self-extracts
native runtime DLLs to a temporary directory before the app starts — by
default under `%TEMP%\.net\<AppName>\<hash>`, overridable via the
`DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable. Microsoft's own security
guidance is that this directory "shouldn't be writable by users or services
with different privileges" than the one running the app
(<https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview>).
On a standard Windows 11 install, `%TEMP%` resolves to the **per-user**
`%LOCALAPPDATA%\Temp` — not a machine-wide, multi-user-writable location — so
the default extraction path is not shared across principals and a
lower-privileged local user cannot plant a DLL for a higher-privileged
PodBridge process to load. The residual case to be aware of is a
misconfigured, machine-wide `%TEMP%` (or an explicit
`DOTNET_BUNDLE_EXTRACT_BASE_DIR` override pointed at a shared, non-per-user
directory) on a shared workstation; PodBridge does not set or rely on any such
override, so the default per-user isolation applies unless the operator's own
environment already deviates from Windows' per-user `%TEMP%` convention.

## Auto-start

The opt-in "start with Windows" toggle is **default OFF** — the user must
explicitly enable it — and is user-revocable at any time via Task Manager's
Startup tab / Settings → Apps → Startup (Windows honours a user disable there
even if the app's own toggle still shows "on," and PodBridge's
`IStartupToggle` contract reports that state back honestly rather than
silently re-enabling). MITRE ATT&CK catalogues both the current MSIX
`StartupTask` mechanism and the portable exe's planned per-user `HKCU\…\Run`
mechanism under the same technique, T1547.001 (Boot or Logon Autostart
Execution: Registry Run Keys / Startup Folder) — neither is more or less
inherently suspicious than the other; what matters for the threat model is that
it is opt-in, per-user (no admin/HKLM key), and the user's own disable is
respected rather than overridden on next launch.

## No `BinaryFormatter`

`BinaryFormatter` is unsound by design ("insecure and can't be made secure";
Microsoft disables it by default starting in .NET 9) and is not used anywhere
in this tree. A repo-wide search for `BinaryFormatter` turns up only its two
mentions in this planning documentation (the spec and research docs that
describe this very hardening requirement) — zero references in `src/` or
`tests/`. Every local persistence path that touches disk was inspected:

| Store | File | Format |
| ----- | ---- | ------ |
| Mic-policy mode | `%LOCALAPPDATA%\PodBridge\mic-policy-mode.txt` (`MicPolicyModeStore`) | Plain enum-name text |
| Gesture configuration | `%LOCALAPPDATA%\PodBridge\gesture-config.txt` (`GestureConfigStore`) | Plain `right;left` delimited text |
| Diagnostics export | `%LOCALAPPDATA%\PodBridge\diagnostics\*.txt` (`DiagnosticsExporter` / `DiagnosticsSnapshotFormatter`) | Plain formatted text |
| Structured logs | rolling log files (`RollingFileLoggerProvider`) | Plain text lines |

None of these use `BinaryFormatter`, `System.Runtime.Serialization`, or any
other unbounded polymorphic deserializer; each is a narrow, hand-written
plain-text reader/writer with a `try`/`catch` that degrades to a safe default
(`HiFiLock`, `null`, or a skipped write) on any malformed content — there is no
deserialization gadget surface to exploit because there is no general-purpose
deserializer in the loop at all. Should a future store move to structured
serialization, the constitution's stack table already commits to
`System.Text.Json` only.

## Out of scope for this document

- Physical access to the Windows machine (standard OS trust boundary).
- Compromise of the user's Apple ID / iPhone (outside PodBridge's control
  surface — PodBridge never requires an Apple account, per `docs/vision.md`).
- The Tier-2 kernel driver's own attack surface beyond "never installed without
  explicit opt-in" — a dedicated driver security review is a separate,
  KMDF-specific exercise the constitution already flags as needing a manual
  smoke test per change.
