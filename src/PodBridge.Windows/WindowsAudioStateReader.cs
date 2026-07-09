using System.Runtime.InteropServices;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows implementation of <see cref="IAudioStateReader"/> — a <b>read-only</b>
/// audio-state reader for the connected AirPods. Tier 1: driver-free, admin-free
/// (<c>asInvoker</c>). It never sets or switches an endpoint and never opens a
/// stream — that is Phase 4's <c>IAudioPolicy</c>.
/// </summary>
/// <remarks>
/// <para><b>Codec (always <see cref="CodecKind.Unknown"/> in Tier 1).</b> Per
/// docs/research/codec-detection.md (#20) the only driver-free way to read the
/// negotiated A2DP codec is an ETW trace of the in-box
/// <c>Microsoft.Windows.Bluetooth.BthA2dp</c> provider, which <b>requires
/// elevation</b> (the reference tool guards with <c>TraceEventSession.IsElevated()</c>
/// and refuses to run without admin). PodBridge Tier 1 forbids elevation, and the
/// research explicitly rejects priority-list / PCM-format inference as dishonest,
/// so the codec is reported honestly as <see cref="CodecKind.Unknown"/> — no
/// elevation request, no guess.</para>
/// <para><b>Mic mode (read-only).</b> Per docs/research/mic-mode-detection.md (#21)
/// there is no public API returning the current profile, so it is inferred
/// read-only from Core Audio: identify the AirPods capture endpoint (name heuristic
/// + shared <c>PKEY_Device_ContainerId</c>), activate <see cref="IAudioSessionManager2"/>
/// on it and enumerate sessions; an <see cref="AudioSessionState.Active"/> capture
/// session means Windows switched the link to <see cref="MicMode.CallModeHfp"/>,
/// otherwise <see cref="MicMode.HighQualityA2dp"/>. Critically it <b>never</b> calls
/// <c>IAudioClient::Initialize/Start</c> on the mic — that would itself open the
/// microphone and force the very HFP switch being observed. Any missing endpoint or
/// COM error degrades to <see cref="MicMode.Unknown"/>, never a crash.</para>
/// </remarks>
public sealed class WindowsAudioStateReader : IAudioStateReader
{
    /// <inheritdoc />
    public AudioState Read()
    {
        // Codec is honestly Unknown in Tier 1 (see the class remarks / research #20):
        // the ETW BthA2dp read needs elevation, which asInvoker cannot request.
        return new AudioState(CodecKind.Unknown, ReadMicMode());
    }

    private static MicMode ReadMicMode()
    {
        object? comObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            comObject = comType is null ? null : Activator.CreateInstance(comType);
            if (comObject is not IMMDeviceEnumerator enumerator)
            {
                return MicMode.Unknown;
            }

            var capture = FindAirPodsCaptureEndpoint(enumerator);
            if (capture is null)
            {
                // No AirPods audio endpoint present, or the capture endpoint could not
                // be matched (research #21 fallback) — honest neutral/Unknown.
                return MicMode.Unknown;
            }

            try
            {
                return HasActiveCaptureSession(capture)
                    ? MicMode.CallModeHfp
                    : MicMode.HighQualityA2dp;
            }
            finally
            {
                Marshal.ReleaseComObject(capture);
            }
        }
        catch (Exception)
        {
            // Any COM / enumeration failure degrades to Unknown (constitution:
            // graceful degradation) — the reader never throws out.
            return MicMode.Unknown;
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    // Identify AirPods by a friendly-name match (render preferred, capture fallback),
    // then pair the capture endpoint by the shared container id (research #21).
    private static IMMDevice? FindAirPodsCaptureEndpoint(IMMDeviceEnumerator enumerator)
    {
        var container = FindAirPodsContainerId(enumerator, EDataFlow.Render)
            ?? FindAirPodsContainerId(enumerator, EDataFlow.Capture);
        return container is null ? null : FindCaptureByContainerId(enumerator, container.Value);
    }

    private static Guid? FindAirPodsContainerId(IMMDeviceEnumerator enumerator, EDataFlow flow)
    {
        enumerator.EnumAudioEndpoints(flow, NativeMethods.DeviceStateActive, out var collection);
        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                try
                {
                    var container = TryGetAirPodsContainerId(device);
                    if (container is not null)
                    {
                        return container;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return null;
    }

    // Returns the device's container id only when its friendly name names AirPods/Beats.
    private static Guid? TryGetAirPodsContainerId(IMMDevice device)
    {
        device.OpenPropertyStore(NativeMethods.StgmRead, out var store);
        try
        {
            var name = NativeMethods.GetStringProperty(store, PropertyKeys.DeviceFriendlyName);
            return AirPodsNameHeuristic.IsMatch(name)
                ? NativeMethods.GetGuidProperty(store, PropertyKeys.DeviceContainerId)
                : null;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static IMMDevice? FindCaptureByContainerId(IMMDeviceEnumerator enumerator, Guid container)
    {
        enumerator.EnumAudioEndpoints(EDataFlow.Capture, NativeMethods.DeviceStateActive, out var collection);
        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                if (MatchesContainerId(device, container))
                {
                    return device; // caller releases the matched endpoint
                }

                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return null;
    }

    private static bool MatchesContainerId(IMMDevice device, Guid container)
    {
        device.OpenPropertyStore(NativeMethods.StgmRead, out var store);
        try
        {
            return NativeMethods.GetGuidProperty(store, PropertyKeys.DeviceContainerId) == container;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    // Read-only: activates the session manager and enumerates session state. It never
    // opens a stream on the mic (no IAudioClient::Initialize/Start), so observing the
    // capture endpoint cannot itself trigger the HFP switch (research #21, critical nuance).
    private static bool HasActiveCaptureSession(IMMDevice capture)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        capture.Activate(ref iid, NativeMethods.ClsCtxAll, IntPtr.Zero, out var raw);
        var manager = (IAudioSessionManager2)raw;
        try
        {
            manager.GetSessionEnumerator(out var sessions);
            try
            {
                sessions.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    if (IsSessionActive(sessions, i))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sessions);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }

        return false;
    }

    private static bool IsSessionActive(IAudioSessionEnumerator sessions, int index)
    {
        sessions.GetSession(index, out var session);
        try
        {
            session.GetState(out var state);
            return state == AudioSessionState.Active;
        }
        finally
        {
            Marshal.ReleaseComObject(session);
        }
    }
}
