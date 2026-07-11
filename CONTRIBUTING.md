# Contributing to PodBridge

Thanks for your interest in PodBridge — an open-source, driver-free AirPods
companion for Windows. Contributions of all kinds are welcome: bug reports,
feature ideas, docs, and code.

## Ways to help

- **Report a bug** — use the *Bug report* issue template. Include your AirPods
  model, Windows version, PodBridge version, and (very helpful) the output of
  **tray → `Export diagnostics`** (it is local-only and masks your Bluetooth
  address — no secrets).
- **Request a feature** — use the *Feature request* template; say whether it fits
  the driver-free **Tier 1** or the opt-in advanced **Tier 2**.
- **Ask a question / share setups** — please use
  [Discussions](https://github.com/bhemsen/PodBridge/discussions), not an issue.
- **Report a security issue** — privately, via **Security → Report a
  vulnerability** (see [`SECURITY.md`](SECURITY.md)). Do **not** open a public
  issue for vulnerabilities.

## Building & verifying

Requires the **.NET 10 SDK**.

```
dotnet restore PodBridge.slnx
powershell -NoProfile -File build/verify.ps1   # build (Release) + format check + tests
```

`build/verify.ps1` is the single gate — it must be green before a PR merges. Run
it after every change set.

## How the project is organised

- `src/PodBridge.Core` — platform-neutral domain (protocol, battery, mic-policy,
  audio, capabilities). **No WPF/WinRT-UI, no P/Invoke** — all OS access sits
  behind interfaces.
- `src/PodBridge.Windows` — Windows adapters (WinRT BLE, audio policy, registry
  auto-start, optional driver transport).
- `src/PodBridge.App` — WPF tray UI + composition root.
- `driver/PodBridgeAAP` — the optional **Tier-2** KMDF driver (ships separately).
- `docs/` — vision, constitution, architecture, roadmap, specs, research, QA.

New device logic and its tests go in `Core` (device-independent, testable with
fakes — no physical AirPods needed). New OS capabilities go behind a `Core`
interface with a `PodBridge.Windows` adapter. New UI goes in `PodBridge.App`.

## Ground rules (please read before a code PR)

These come from [`docs/constitution.md`](docs/constitution.md) and are enforced in
review:

- **Clean-room only.** PodBridge is **Apache-2.0**. Do **not** copy source code or
  verbatim documentation prose from GPL-licensed projects (e.g. AirPodsDesktop,
  LibrePods) or any proprietary tool. Reimplement from *documented facts* and cite
  the fact in a comment.
- **Never defeat MagicPairing encryption** — use only the cleartext AAP control
  channel.
- **Tier 1 stays driver-free and admin-free.** The default path never installs a
  driver or requests elevation (`asInvoker`). The Tier-2 driver is always an
  explicit, clearly-warned opt-in.
- **Local-only.** No telemetry or network calls except the explicit update check.
- **No secrets** committed or logged.
- **Honest UI.** No string claims Apple-parity sound; state the AAC-vs-SBC and
  A2DP↔HFP realities truthfully.
- Small units (functions ≤ 50 lines), nullable reference types on, one public type
  per file, warnings-as-errors in `Core`.
- Every Tier-1 feature has a **device-independent test** (fakes).

## Pull requests

- Branch from `main`: `feat/…`, `fix/…`, `chore/…`, `docs/…`, `refactor/…`.
- Use **Conventional Commits** (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`).
- Keep changes minimal and focused; reference the issue (`Closes #N`).
- Make sure `build/verify.ps1` is green. Behavioural/hardware changes should note
  what you tested on real AirPods.

By contributing, you agree that your contributions are licensed under the
project's **Apache-2.0** license.

## Be kind

Assume good faith, keep discussion respectful and on-topic. This is a spare-time,
open-source project — patience appreciated.
