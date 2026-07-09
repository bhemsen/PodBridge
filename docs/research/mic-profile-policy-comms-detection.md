# Research: detecting a Communications-role capture session via `IAudioSessionManager2`

> Permanent record for the `chore:research-comms-session-detection` issue (#26).
> Authority for the Phase-4 implementation issue that builds
> `WindowsAudioSessionMonitor`. Clean-room: this file cites Microsoft Learn /
> official Win32 & driver reference and public community/implementation
> corroboration only — no GPL source or verbatim protocol-doc prose is reproduced.
>
> Scope: how a **driver-free, admin-free** monitor on **Windows 11** can reliably
> detect a **Communications-role capture (microphone) session** opening and
> closing, so Core's mic-policy engine can promote/restore the AirPods
> communications role (Auto-switch). Two questions decide the design: *what
> exactly forces the AirPods link from A2DP to HFP*, and *what is the reliable
> `IAudioSessionManager2` signal for it — events or polling*. Sibling read-only
> Phase-3 detection (`mic-mode-detection.md`) shares the underlying facts; this
> unit adds the **event/open-close** model the policy engine needs.

## Sources

1. [Bluetooth Classic Audio — Windows drivers (Microsoft Learn)](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
   — **primary & authoritative for the HFP trigger.** Windows 11 unifies A2DP +
   HFP into **one output and one input endpoint**. States verbatim that Windows
   selects **HFP instead of A2DP** when *either*: (a) "an application opens the
   input (microphone) endpoint", or (b) "an application creates an output
   (playback) stream with the category set to *Communications*"; "in all other
   cases" playback uses A2DP. Profile changes happen **automatically** on
   microphone-usage state (mic opened → HFP; mic closed → back to A2DP).
2. [IAudioSessionManager2 (audiopolicy.h) — Win32 (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionmanager2)
   — the session surface. Activate on an `IMMDevice` (`IMMDevice::Activate` with
   `IID_IAudioSessionManager2`); `GetSessionEnumerator` **enumerates sessions on
   that endpoint**; `RegisterSessionNotification` + `IAudioSessionNotification`
   raise **`OnSessionCreated`**; `RegisterDuckNotification` +
   `IAudioVolumeDuckNotification` raise communication-stream duck/unduck events.
   Windows 7+, desktop, no elevation. **No endpoint-switching method exists here.**
3. [IAudioSessionControl2 (audiopolicy.h) — Win32 (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessioncontrol2)
   — its **complete** method set is `GetSessionIdentifier`,
   `GetSessionInstanceIdentifier`, `GetProcessId`, `IsSystemSoundsSession`,
   `SetDuckingPreference` (+ inherited `IAudioSessionControl`). Decisive negative
   result: **there is no method that returns a session's `AudioCategory`** — an
   observer cannot read "is this a Communications session" off a session object.
4. [AudioSessionState enum (audiosessiontypes.h) — Win32 (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/api/audiosessiontypes/ne-audiosessiontypes-audiosessionstate)
   + [IAudioSessionControl::GetState / IAudioSessionEvents::OnStateChanged](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol-getstate)
   — `AudioSessionStateActive` = "at least one of the streams in the session is
   running"; `Inactive` = has streams, none running; `Expired` = no streams left.
   State is read via `GetState` and **pushed** via `IAudioSessionEvents::OnStateChanged`.
5. [Using a Communication Device (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/coreaudio/using-the-communication-device)
   — defines a **communication stream** as any render *or* capture stream opened
   on the endpoint holding the **`eCommunications`** device role; "the audio system
   generates ducking events when a communication stream is opened or closed for
   rendering or capturing streams." Ties the `eCommunications` role to the duck signal.
6. [Getting Ducking Events from a Communication Device (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/coreaudio/getting-ducking-events-from-a-communication-device)
   + [Implementation Considerations for Ducking Notifications](https://learn.microsoft.com/en-us/windows/win32/coreaudio/handling-audio-ducking-events-from-communication-devices)
   + [IAudioVolumeDuckNotification::OnVolumeDuckNotification](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiovolumeducknotification-onvolumeducknotification)
   — the duck model: `OnVolumeDuckNotification(sessionId, countCommunicationSessions)`
   fires **when a communication stream opens**, `OnVolumeUnduckNotification(sessionId)`
   **when it closes**; `countCommunicationSessions` gives the number of active
   communication sessions. Callbacks are **asynchronous on a background thread**
   and the handler **must not block**.
7. [IAudioSessionManager2::RegisterSessionNotification not notifying on second logoff/logon — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/772268/iaudiosessionmanager2-registersessionnotification)
   — documented **reliability caveat**: `OnSessionCreated` fires on first logon but
   can silently stop on later logon/logoff cycles; the session manager object must
   be kept alive and COM initialised MTA on a non-UI thread.
8. [IAudioSessionNotification (sdk-api, MicrosoftDocs)](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/audiopolicy/nn-audiopolicy-iaudiosessionnotification.md)
   — Microsoft's own remark: the **session enumerator may not be aware of new
   sessions reported through `IAudioSessionNotification`**, so an app relying only
   on the enumerator can get inaccurate results — it should **maintain its own
   session list**. (The enumeration/notification split is not self-consistent.)
9. [frgnca/AudioDeviceCmdlets — `IAudioSessionManager2.cs`](https://github.com/frgnca/AudioDeviceCmdlets/blob/master/SOURCE/IAudioSessionManager2.cs)
   — concrete **C# P/Invoke** declarations for `IAudioSessionManager2` /
   `IAudioSessionControl` (GUIDs, vtable order) — an MIT/permissive implementation
   precedent that the clean-room `WindowsAudioSessionMonitor` interop can be
   modelled against without copying GPL source.
10. [Windows 10/11 Bluetooth Headset Audio: A2DP vs HFP (Windows Forum)](https://windowsforum.com/threads/windows-10-bluetooth-headset-audio-a2dp-vs-hfp-fixes-tradeoffs.401263/)
    + [Why your wireless headphones sound worse on Windows (MakeUseOf)](https://www.makeuseof.com/windows-bluetooth-classic-audio-limitations/)
    — independent behavioural corroboration: opening the mic / a communications
    stream forces HFP and suspends stereo A2DP; stereo playback and the Bluetooth
    mic cannot run simultaneously.

## Consensus

### 1. The exact A2DP → HFP forcing trigger (settled by Source 1)

On **Windows 11**, with the AirPods presented as one unified render + one capture
endpoint, Windows switches the link **to HFP** iff **either**:

1. **an application opens the capture (microphone) input endpoint** — this is the
   dominant, always-present trigger; or
2. **an application opens a render (playback) stream whose category is
   `AudioCategory_Communications`** — HFP is forced even with no capture stream.

In every other case output stays on **A2DP** (high quality). The switch is
**automatic and bidirectional**: mic opens → HFP (stereo A2DP is downmixed /
suspended, resampled to mono 8/16 kHz); last mic stream closes → link returns to
A2DP. (Sources 1, 4, 10.) For PodBridge Auto-switch the operative trigger is #1
(an app wants the mic); #2 is a render-side edge case handled separately (below).

### 2. How a Communications-role capture session is identified

There is **no per-session category flag readable by an observer.** `AudioCategory_Communications`
is set write-only by the *owning* client through `IAudioClient2::SetClientProperties`;
`IAudioSessionControl2` exposes process id, session/instance ids, and the
system-sounds flag, **but no `GetCategory`** (Source 3). So "identify the comms
session" is **not** "read a category" — it is derived from **device role +
session state**, via two complementary signals:

- **Primary — active capture session on the communications capture endpoint.**
  Resolve the **`eCommunications` capture** endpoint
  (`IMMDeviceEnumerator::GetDefaultAudioEndpoint(eCapture, eCommunications)`),
  `Activate` `IAudioSessionManager2` on it, `GetSessionEnumerator`, and treat the
  microphone as engaged when **any** non-system-sounds session reports
  `GetState() == AudioSessionStateActive` (Sources 2, 3, 4). This maps *directly*
  onto trigger #1 ("an application opens the input endpoint"), because a running
  capture stream is exactly what an active capture session represents. The AirPods
  capture endpoint is identified separately by container-id (the `isAirPods` flag;
  see `chore:research-ipolicyconfig`), but the *comms-activity* decision keys off
  the `eCommunications`-role capture endpoint, which is where the policy engine
  routes/removes the AirPods.

- **Secondary — the OS duck signal for a communications stream.** `RegisterDuckNotification`
  / `IAudioVolumeDuckNotification` is Windows' **own** notion of "a communication
  stream opened/closed on the `eCommunications` device", covering **render *and*
  capture** (Sources 5, 6). `OnVolumeDuckNotification` therefore *also* catches
  trigger #2 (a Communications-category **render** stream) that the capture-session
  scan cannot see. `countCommunicationSessions` is the live count of active
  communication sessions — the natural refcount for open/close bookkeeping.

**Recommendation:** the capture-session-state scan is the authoritative signal for
the mic case; the duck notification is a valuable second channel that additionally
covers the Communications-render case and provides a system-maintained session
count. Using both makes the monitor robust to the blind spot each has alone.

### 3. Events vs polling — reliability verdict

**Event-driven is correct, but pure notification is not sufficient; use
event-primary with an enumeration reconciliation.** Evidence:

- Windows exposes the right push events: `IAudioSessionNotification::OnSessionCreated`
  (new session appears), `IAudioSessionEvents::OnStateChanged`
  (Active↔Inactive↔Expired, i.e. the actual start/stop of streaming), and the
  duck/unduck pair. Polling `GetState` in a timer works but is laggy and wasteful
  and still needs the enumerator. (Sources 2, 4.)
- **But notifications are documented as incomplete/unreliable:**
  `OnSessionCreated` can stop firing across logon/logoff cycles (Source 7), and
  the **session enumerator may not even be aware of notification-reported
  sessions** — Microsoft's own guidance is to **maintain your own session list**
  (Source 8). `OnSessionCreated` fires on *creation*, but a session is created in
  the `Inactive` state and only later goes `Active` (Source 4) — so the open of
  interest is an `OnStateChanged`→`Active`, not the create.

Consensus design: **subscribe** to `OnSessionCreated`, wire `IAudioSessionEvents`
on each session (existing + newly created) to catch Active/Inactive/Expired, and
**additionally** register the duck notification; treat all of these as *triggers*
that prompt a **re-enumeration + recompute** of "is any comms-capture session
active", reconciled against a **self-maintained session set**. A low-frequency
safety re-enumeration (e.g. on the events plus a coarse fallback tick) closes the
"notifications silently stopped" gap. Never rely on the enumerator alone, and
never rely on notifications alone. (Sources 2, 4, 7, 8.)

### 4. Open / close event ordering

- **Session lifecycle:** create → `Inactive` (has a stream, not running) →
  `Active` (a stream is running = mic actually engaged) → `Inactive` (stopped) →
  `Expired` (all streams released). The **HFP-forcing "open"** corresponds to the
  transition **into `Active`**; the **"close"** is the transition **out of
  `Active`** (to `Inactive`/`Expired`). `OnSessionCreated` precedes the first
  `OnStateChanged`. (Source 4.)
- **Duck lifecycle:** `OnVolumeDuckNotification` fires **on open**,
  `OnVolumeUnduckNotification` **on close**, with `countCommunicationSessions`
  tracking overlap. Callbacks are **async on a background thread and must not
  block** — marshal work off the callback thread (Source 6). With multiple
  overlapping calls, treat the endpoint as "comms active" while the active-session
  count / duck count is `> 0` and only fire PodBridge's **close** (restore to
  HiFi-lock) when it returns to `0` — this debounces the momentary A2DP↔HFP
  flip Source 1 describes when one call ends as another continues.
- **Practical rule for the monitor:** expose a single **debounced boolean**
  ("a communications capture session is active") derived from the reconciled
  session set + duck count, and raise Core-facing `Opened`/`Closed` events on its
  `false→true` / `true→false` edges — not on every raw notification.

### Confirmation it stays Tier-1 (driver-free, admin-free, switches nothing)

Every call above is enumeration / query / notification: `IMMDeviceEnumerator`,
`IMMDevice::Activate`, `GetSessionEnumerator`, `GetState`,
`RegisterSessionNotification`, `RegisterDuckNotification`. None opens a stream and
none switches an endpoint (endpoint switching is `IPolicyConfig`, Phase 4's
`WindowsAudioPolicy`, a separate research unit). `IAudioSessionManager2` is a
normal desktop-app interface needing no elevation (Source 2). **The monitor must
never open a stream on the capture endpoint itself** (`IAudioClient::Initialize`/
`Start`) — doing so would itself trigger HFP (trigger #1). COM should be
initialised **MTA on a non-UI thread**, and the session-manager and notification
objects **kept alive** for the monitor's lifetime (Sources 6, 7).

## Recommended approach (for the Phase-4 `WindowsAudioSessionMonitor`)

Behind Core's `IAudioSessionMonitor` (raising `Opened` / `Closed` for a
comms-capture session), the `WindowsAudioSessionMonitor` (Windows adapter):

1. Resolve the **`eCommunications` capture** endpoint via `IMMDeviceEnumerator`;
   `Activate(IAudioSessionManager2)` on it. Re-resolve on default-device changes
   (`IMMNotificationClient::OnDefaultDeviceChanged`).
2. Seed a **self-maintained session set** from `GetSessionEnumerator`; attach
   `IAudioSessionEvents` to every session; `RegisterSessionNotification` for new
   ones; `RegisterDuckNotification` for the OS comms-stream signal.
3. On **any** trigger (`OnSessionCreated`, `OnStateChanged`, duck/unduck), reconcile
   the set and recompute a **debounced "comms-capture active"** boolean =
   (any non-system-sounds capture session `Active`) OR (duck count `> 0`).
4. Raise Core-facing **`Opened`** on `false→true` and **`Closed`** on `true→false`
   only. The engine's Auto-switch promotes AirPods to the comms role on `Opened`
   and restores the HiFi-lock assignment on `Closed`.
5. Initialise COM **MTA on a background thread**; marshal callback work off the
   notification thread; keep manager + notification objects alive; unregister on
   dispose.
6. Never open a capture stream; never call any switching API. Unit-test the
   **engine** against a **fake `IAudioSessionMonitor`** driving Opened→Closed
   cycles (the adapter itself is exercised only at the human QA gate — no CI HW).

## Disputes (minority → majority decision)

- **"Read the session's Communications category to identify it."** Minority
  (implied by Grokipedia's *Windows communications activity detection* framing,
  which describes sessions as *marked* `AudioCategory_Communications`): expect to
  read that mark back. **Rejected —** `AudioCategory` is set write-only via
  `IAudioClient2::SetClientProperties`; **no observer API returns it**
  (`IAudioSessionControl2` method list, Source 3). → **Majority: identify comms
  activity from `eCommunications` device role + `AudioSessionStateActive` on the
  capture endpoint, plus the duck notification — never a category read.**
- **Events vs polling.** Minority: pure `OnSessionCreated` events are enough.
  **Rejected —** notifications are documented as incomplete (Source 7) and not
  reconciled with the enumerator (Source 8), and the meaningful open is the
  `Active` state change, not the create. → **Majority: event-primary
  (`OnSessionCreated` + per-session `OnStateChanged` + duck), with re-enumeration
  reconciliation against a self-maintained list and a coarse safety re-scan; not
  pure polling, not pure notifications.**
- **Which endpoint to watch, and is the capture scan sufficient?** Minority: watch
  the default render/console device (the duck sample activates on `eRender`).
  **Refined —** the HFP trigger is defined on the **input (mic) endpoint** and the
  **Communications render** stream (Source 1); the capture-session scan catches the
  first, the duck notification catches the second. → **Majority: key the primary
  decision off the `eCommunications` *capture* endpoint's active sessions; use the
  duck notification as the complementary render-side channel.** The capture scan
  alone is sufficient for the mic case (the Auto-switch use case) but not for a
  Communications-render-only switch, hence both.
- **Restore timing on overlapping sessions.** Minority: restore A2DP the instant
  any one session closes. **Rejected —** Source 1 notes a momentary flip when one
  call ends while another continues, and `countCommunicationSessions` (Source 6)
  exists precisely to track overlap. → **Majority: restore only when the active
  count returns to 0 (debounced edge), giving a deterministic HiFi-lock end state.**
