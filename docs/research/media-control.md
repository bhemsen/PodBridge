# Research: Windows media-session control (GSMTC)

> Permanent record for the `chore:research-media-control` issue (#17).
> Authority for the Phase-2 media-engine implementation issue (#18). Clean-room:
> this file cites Microsoft Learn / official WinRT reference and the Microsoft
> DevBlogs sample only — no GPL source or verbatim protocol-doc prose is
> reproduced; all wording is our own.
>
> Scope: driving **pause / play** of whatever media is currently playing on
> Windows 11 from a background, driver-free, non-elevated (`asInvoker`) desktop
> tray app, and **reading current playback state** first so PodBridge never
> resumes media the user paused. This backs the `IMediaController` /
> `WindowsMediaController` adapter and the Core auto play/pause engine. Deciding
> *when* to pause (in-ear/out-of-ear) is the Continuity parser's job (Phase 2,
> separate issue); this file is only about the media-session mechanism.

## Sources

1. [GlobalSystemMediaTransportControlsSessionManager.RequestAsync (Windows.Media.Control)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager.requestasync?view=winrt-22621)
   — the static `IAsyncOperation` factory that yields the session manager; the
   entry point for the whole API.
2. [GlobalSystemMediaTransportControlsSessionManager.GetCurrentSession (Windows.Media.Control)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager.getcurrentsession?view=winrt-26100)
   — returns "the session the system believes the user would most likely want to
   control"; defines what "current session" means for multi-session targeting.
   Lists the `globalMediaControl` app capability.
3. [GlobalSystemMediaTransportControlsSessionManager.GetSessions (Windows.Media.Control)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager.getsessions?view=winrt-26100)
   — returns `IReadOnlyList<GlobalSystemMediaTransportControlsSession>` of **all**
   available sessions; the fallback for enumerating concurrent sessions.
4. [GlobalSystemMediaTransportControlsSession Class (Windows.Media.Control)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession?view=winrt-26100)
   — the session's control surface: `TryPlayAsync`, `TryPauseAsync`,
   `TryTogglePlayPauseAsync`, `GetPlaybackInfo`, `PlaybackInfoChanged`.
5. [GlobalSystemMediaTransportControlsSession.GetPlaybackInfo (Windows.Media.Control)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession.getplaybackinfo?view=winrt-26100)
   — returns `GlobalSystemMediaTransportControlsSessionPlaybackInfo`, "accurate to
   the time of the call"; carries `PlaybackStatus` and the `Controls` capability set.
6. [GlobalSystemMediaTransportControlsSessionPlaybackStatus Enum (Windows.Media.Control)](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionplaybackstatus?view=winrt-26100)
   — the exact enum: `Closed`=0, `Opened`=1, `Changing`=2, `Stopped`=3,
   `Playing`=4, `Paused`=5. Introduced Windows 10 1809 (10.0.17763.0).
7. [The Old New Thing — "How can I get information about media playing on the system, and optionally control their playback?" (Microsoft DevBlogs, 2023-11-08)](https://devblogs.microsoft.com/oldnewthing/20231108-00/?p=108980)
   — Microsoft-authored end-to-end sample: `await RequestAsync()` →
   `GetCurrentSession()` → `GetPlaybackInfo().PlaybackStatus == Playing` →
   `await TryPauseAsync()`. Uses it from an ordinary console/desktop app, no admin.
8. [App capability declarations — UWP applications (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations)
   — capabilities such as `globalMediaControl` are enforced for **packaged**
   apps running in an AppContainer; unpackaged (Medium-IL) desktop apps declare
   their AppContainer-or-Medium-IL choice in the project, not via a manifest cap.
9. [dotnet/runtime issue #84293 — "Can not access GSMTC … while running as service"](https://github.com/dotnet/runtime/issues/84293)
   — confirms `RequestAsync()` **works from an interactive user session without
   admin** and fails only in a **non-interactive** service/SYSTEM session on
   Windows 11 (error `0x80070424`). Bounds the "no-admin" claim precisely.
10. [DubyaDude/WindowsMediaController (GitHub)](https://github.com/DubyaDude/WindowsMediaController)
    — a widely-used wrapper library proving the API works from a normal
    unpackaged .NET desktop app; exposes a focused/current session and enumerates
    all sessions, mirroring `GetCurrentSession` / `GetSessions`.

## Consensus

### Session manager + current session

- **Entry point:** `await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`
  returns the manager (an `IAsyncOperation`). Namespace `Windows.Media.Control`
  (a WinRT/UWP API projected into C# via CsWinRT). (Sources 1, 7.)
- **Current session:** `manager.GetCurrentSession()` returns a single
  `GlobalSystemMediaTransportControlsSession`, documented as **"the session the
  system believes the user would most likely want to control"** — i.e. Windows'
  own heuristic for the foreground/most-recent media app, the same target the
  hardware media keys and the volume-flyout transport controls act on.
  (Source 2.) It returns **`null` when no app currently has a media session**
  (nothing is/has been playing) — callers must null-check. (Sources 2, 7.)
- **Change signal:** the manager raises `CurrentSessionChanged` when the current
  session switches apps; PodBridge re-reads `GetCurrentSession()` on that event
  rather than caching a stale session. (Source 4 family.)

### Pause / play

- The session exposes `TryPauseAsync()` and `TryPlayAsync()` (both
  `IAsyncOperation<bool>`; the `bool` reports whether the request was accepted,
  not whether audio actually changed). It also offers `TryTogglePlayPauseAsync()`,
  which we deliberately **avoid** because a blind toggle defeats the "don't
  resume user-paused media" rule. (Sources 4, 7.)
- A session advertises which commands it honours via
  `GetPlaybackInfo().Controls` (e.g. `IsPauseEnabled`, `IsPlayEnabled`). Robust
  callers check the relevant flag before issuing the request; a `Try…` call on an
  unsupported control simply returns `false`. (Source 5.)

### Reading playback state (playing vs paused)

- `session.GetPlaybackInfo()` returns a
  `GlobalSystemMediaTransportControlsSessionPlaybackInfo` snapshot "accurate to
  the time of the call"; its `.PlaybackStatus` is a
  `GlobalSystemMediaTransportControlsSessionPlaybackStatus`. (Source 5.)
- **Exact enum** (Source 6): `Closed`=0, `Opened`=1, `Changing`=2, `Stopped`=3,
  **`Playing`=4**, **`Paused`=5**. "Media is actively playing" ⇔
  `PlaybackStatus == Playing`; "paused" ⇔ `PlaybackStatus == Paused`.
- **This is the key to the resume rule.** Before auto-pausing (bud removed),
  read `PlaybackStatus`; only pause if it is `Playing`, and record a
  **"paused-by-us"** flag. On bud re-insertion, only call `TryPlayAsync()` if
  that flag is set (PodBridge did the pause) — never resume a session the user
  paused, closed, or stopped themselves. Track state per-session (session
  identity/`SourceAppUserModelId`) so a session change invalidates the flag.
- Live updates: `PlaybackInfoChanged` fires when the state changes, so the
  engine can observe a user manually resuming/pausing and clear its flag; but a
  fresh `GetPlaybackInfo()` immediately before each action is the authoritative
  read.

### No-admin / no-driver confirmation

- The whole `Windows.Media.Control` surface is a **WinRT projection, not a
  driver** — no kernel component, no INF, no `pnputil`. It is available on
  Windows 10 1809+ / all Windows 11 builds (matches the constitution's
  Win11 21H2+ target). (Sources 2–6.)
- It requires **no elevation**: Source 7 (Microsoft's own sample) and Source 9
  both exercise it from an ordinary, non-elevated process, and Source 9
  explicitly notes it "succeeds in an interactive user session without requiring
  administrative privileges." (Sources 7, 9, 10.)
- **Capability caveat:** the reference lists a `globalMediaControl` app
  capability (Source 2). Per Source 8, capabilities are enforced for **packaged**
  apps in an AppContainer; an **unpackaged Medium-IL desktop app** (PodBridge's
  Tier-1 shape) is not gated by a manifest capability. When PodBridge is later
  MSIX-packaged (Phase 5), declare `globalMediaControl` in the package manifest.
  Either way it is a **capability, not an elevation** — no admin, no UAC prompt.
- **Interactive-session caveat (the real limit):** the API needs a normal
  **interactive user session**; it fails in a **non-interactive service / SYSTEM**
  context on Windows 11 (`0x80070424`). (Source 9.) PodBridge is an interactive
  tray app started by the logged-in user, so this does not apply — but it means
  PodBridge must **not** be architected as a Windows service.

### Multi-session target (assumption)

- When several apps have media sessions at once (e.g. Spotify + a browser video),
  `GetCurrentSession()` already resolves to the one Windows deems the user most
  likely wants to control (Source 2); `GetSessions()` exposes the full list for
  fallback/diagnostics (Source 3).
- **Documented assumption for Phase 2:** PodBridge targets **the current session
  only** (`GetCurrentSession()`), matching AirPods' native single-focus behaviour
  and the spec's "single-active-session" assumption. If it is `null`, PodBridge
  does nothing (no session to pause). Multi-session disambiguation beyond the
  current session (e.g. pausing every playing session) is **out of scope** for
  Phase 2 and left as a future enhancement. This mirrors the spec's Risk row
  "Media-session control resumes/pauses the wrong app when several are playing →
  target the current session; confirm at the QA gate."

### GSMTC vs synthesizing media-key input

| Aspect | GSMTC (`Windows.Media.Control`) | Media-key synthesis (`SendInput`/`keybd_event`, `VK_MEDIA_PLAY_PAUSE`=0xB3) |
|---|---|---|
| Read current state | **Yes** — `PlaybackStatus` (playing/paused/stopped). | **No** — cannot query state at all. |
| Explicit pause vs play | **Yes** — separate `TryPauseAsync` / `TryPlayAsync`. | **No** — only a blind play/pause **toggle**. |
| "Don't resume user-paused media" | **Achievable** — read state + paused-by-us flag. | **Impossible reliably** — a toggle would resume whatever the user paused. |
| Target selection | Deterministic (system's current session). | Whichever app currently owns media-key focus — opaque. |
| Success feedback | `bool` result + `Controls` capability flags. | None. |
| Admin / driver | None (asInvoker; interactive session). | None, but sends system-wide synthetic input (SendInput preferred over the legacy `keybd_event`). |

**Decision: GSMTC is the chosen mechanism**, consistent with the spec's Prior
Decision (2026-07-09: "Media control via the Windows media-session manager …
preferred over synthesizing media-key input"). The research **confirms, not
contradicts**, that decision: only GSMTC can read `PlaybackStatus`, which is the
prerequisite for the resume-only-if-we-paused requirement. Media-key synthesis is
recorded as the rejected alternative and is **not** used, even as a fallback,
because its blind-toggle nature would break the resume rule.

## Recommended approach (for issue #18)

Behind Core's `IMediaController` (device-independent, faked in tests), implement
`WindowsMediaController` in `PodBridge.Windows` as:

1. **Init:** `_manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`;
   subscribe to `CurrentSessionChanged`. Do this lazily/once; degrade gracefully
   (log + no-op) if it throws (e.g. the non-interactive-session case).
2. **`bool PauseIfPlaying()` (our pause):** `s = _manager.GetCurrentSession()`;
   if `s == null` → no-op, return `false`. Read
   `s.GetPlaybackInfo().PlaybackStatus`; if `== Playing`, `await s.TryPauseAsync()`
   and return `true` (caller records paused-by-us for that session identity);
   otherwise return `false` (nothing to pause / user already paused).
3. **`void ResumeIfWePaused()`:** only when the Core engine's paused-by-us flag is
   set **and** the current session is still the same one PodBridge paused →
   `await s.TryPlayAsync()`, then clear the flag. Never resume otherwise.
4. **Keep the decision in Core.** The Windows adapter only exposes primitive
   "current playback status" + "pause"/"play"; the paused-by-us state machine and
   the in-ear/out-of-ear→action mapping live in the Core engine so they are
   unit-tested with a fake `IMediaController` (spec test gate). The adapter should
   never resume media the user paused, and the engine enforces it.

Requires **no elevation** (`asInvoker`) and **no driver**; runs in the
interactive user session; targets the **current** session only in Phase 2.

## Disputes (minority → majority decision)

- **Does it need admin?** A minority of forum/Q&A threads conflate the
  service/SYSTEM failure (Source 9) with "needs elevation." The majority —
  Microsoft's own sample (Source 7), the API reference (Sources 2–6), and the
  root-cause analysis in Source 9 — agree it is a **non-interactive-session**
  limitation, **not** an elevation one. **Decision: no admin required; run as an
  interactive tray app, never as a service.**
- **Capability required?** The reference tags `globalMediaControl` (Source 2),
  which reads as "always required." Reconciled with Source 8: it is enforced only
  for **packaged/AppContainer** apps; the unpackaged Medium-IL Tier-1 build is not
  gated by it. **Decision: no manifest capability needed while unpackaged; declare
  `globalMediaControl` when MSIX-packaged in Phase 5.**
- **Toggle vs explicit pause/play?** `TryTogglePlayPauseAsync` exists and is
  simplest, but it cannot honour the resume rule. **Decision: use the explicit
  `TryPauseAsync` / `TryPlayAsync` pair gated on a `PlaybackStatus` read; do not
  use the toggle.**
- **Which session on multi-session?** Enumerate-all (`GetSessions`) vs
  current-only (`GetCurrentSession`). **Decision: current-only for Phase 2**
  (matches AirPods native behaviour and the spec's single-active-session
  assumption); `GetSessions` is available for a later multi-session enhancement.
