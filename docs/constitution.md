# Constitution

> Normative and binding. Every principle must be verifiable and specific.
> Keep to ~1 page; this file is permanently loaded via CLAUDE.md. No status
> marker — foundation docs carry none.
>
> Project: **PodBridge** — an open-source AirPods companion for Windows.

## Tech stack

| Area | Choice | Rationale |
| ---- | ------ | --------- |
| Language / runtime | C# on .NET (current LTS, .NET 10) | Best WinRT projections (CsWinRT), tray, and packaging story on Windows; proven for this exact use case (WinPods). |
| Target | `net10.0-windows10.0.22621.0` (Windows 11 21H2+) | AAC A2DP + WinRT BLE advertisement APIs require Win11 21H2+. |
| UI shell | WPF, tray-first (Hardcodet/H.NotifyIcon) | Mature tray support; WinUI 3 tray support is weak (prior-art). |
| BLE (Tier 1) | WinRT `BluetoothLEAdvertisementWatcher` via CsWinRT | Driver-free, no admin — battery + in-ear from Apple Continuity (0x004C). |
| Audio policy | NAudio (enumerate) + P/Invoke `IPolicyConfig`/`IPolicyConfig2`, `IAudioSessionManager2` | Only way to set default vs communications endpoints and observe mic sessions. |
| AAP protocol | Clean-room C# module from documented facts | Reimplemented from `docs/prior-art.md` facts; no GPL source copied. |
| Advanced tier (opt-in) | Separate C / KMDF L2CAP-bridge driver (WDK) | Only kernel drivers may open Classic-L2CAP PSM 0x1001 on Windows. |
| Packaging | MSIX + winget (app); separate INF + `pnputil` (driver) | MSIX cannot bundle a kernel driver cleanly; driver is its own opt-in installer. |
| Tests | xUnit + device-independent fakes | Core must be testable with no physical AirPods. |
| CI | GitHub Actions on `windows-latest` | Runs the single Verify command per push/PR. |
| License | Apache-2.0 | Permissive, Store-friendly, explicit patent grant; requires clean-room (no GPL fork). |

## Architecture principles

- **Core is platform-neutral and OS-free.** `PodBridge.Core` (protocol, battery
  model, mic-policy logic) references no WPF/WinUI/WinRT-UI package and performs
  no P/Invoke; all OS access sits behind interfaces (`IBleScanner`,
  `IAudioPolicy`, `IAapTransport`). Verifiable: inspect `PodBridge.Core.csproj`.
- **Tier 1 needs no elevation.** The default-tier code path never installs a
  driver or requests elevation; the app manifest is `requestedExecutionLevel
  asInvoker`. Verifiable: manifest + a smoke run with no driver present.
- **Clean-room protocol.** Every AAP opcode/constant lives in one `AapProtocol`
  module, each with a comment citing the documented fact (PSM, opcode). No source
  or verbatim doc prose copied from GPL projects. Verifiable in review.
- **Graceful degradation.** With the advanced driver absent, every Tier-1 feature
  still works and the Tier-1 test suite passes. Verifiable: run tests with the
  driver uninstalled.
- **Honest audio surface.** No user-facing string claims Apple-parity sound; the
  codec/limitation is stated truthfully. Verifiable in review.
- **Local-only.** No network calls except an explicit, user-visible update check;
  the tool reads local BLE only. Verifiable in review.
- **Small units.** Max function length 50 lines; nullable reference types on;
  warnings-as-errors in `PodBridge.Core`.

## Conventions

- C# latest; `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`;
  `.editorconfig` (dotnet default) enforced by `dotnet format`.
- Analyzers: `Microsoft.CodeAnalysis.NetAnalyzers` at recommended, treated as errors in Core.
- Naming: PascalCase types/methods, `_camelCase` private fields, one public type per file.
- Layout: `src/PodBridge.Core`, `src/PodBridge.App` (WPF tray), `src/PodBridge.Windows`
  (WinRT/audio adapters), `driver/PodBridgeAAP` (C, optional), `tests/PodBridge.Core.Tests`.
- Commits: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`).

## Quality gates

- **Verify (single command) green before merge:** build + analyzers
  (warn-as-error) + `dotnet format --verify-no-changes` + `dotnet test`.
- Every Tier-1 feature has a test that runs without a physical device (fakes).
- App ships with an `asInvoker` manifest — no auto-elevation in the default tier.
- Advanced-driver changes additionally require a manual smoke test (no CI HW).

## Don'ts

- No GPL-licensed source or verbatim doc prose copied into this Apache-2.0 tree.
- No defeating MagicPairing encryption — use only the cleartext AAP control channel.
- No bundling FDK-AAC, the "Alternative A2DP Driver", or any paid/proprietary component.
- No "AirPods" or "Apple" in the product name and no Apple logo; keep the
  not-affiliated disclaimer.
- No kernel driver installed or elevation requested without explicit user opt-in.
- No secrets, tokens, or keys committed or logged.
- No telemetry or background network exfiltration; local-only by default.
