using System.Runtime.InteropServices;
using PodBridge.Core.Audio;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows implementation of <see cref="ICommsProfileEngager"/>. Holds a silent
/// WASAPI <b>render</b> keep-alive stream tagged <c>AudioCategory_Communications</c> on
/// the AirPods render endpoint, which forces Windows to bring up the AirPods Hands-Free
/// (HFP/SCO) link so the AirPods microphone (capture) endpoint comes live. Tier 1:
/// driver-free, admin-free (<c>asInvoker</c>).
/// </summary>
/// <remarks>
/// <para><b>Render only.</b> It activates <see cref="IAudioClient2"/> on the render
/// <see cref="IMMDevice"/>, sets the Communications category BEFORE Initialize, primes one
/// SILENT buffer, Starts and HOLDS the stream. It NEVER opens a capture/microphone stream —
/// the mic endpoint is engaged as a side effect of HFP coming up, not by us capturing.</para>
/// <para><b>Graceful degradation.</b> Any COM failure at any step degrades to a no-op
/// (the keep-alive is simply not held); nothing throws (constitution). All the COM/P-Invoke
/// is isolated here and in <c>AudioClientInterop.cs</c>; Core sees only the interface.</para>
/// <para>Calls are serialized by <see cref="MicPolicyEngine"/>'s apply gate, but the adapter
/// also guards its own state so a shutdown Dispose can never race a late Engage.</para>
/// </remarks>
public sealed class WindowsCommsProfileEngager : ICommsProfileEngager, IDisposable
{
    private readonly Lock _gate = new();
    private string? _engagedId;
    private IAudioClient2? _client;
    private IAudioRenderClient? _render;
    private bool _started;

    /// <inheritdoc />
    public void Engage(string renderEndpointId)
    {
        if (string.IsNullOrEmpty(renderEndpointId))
        {
            return;
        }

        lock (_gate)
        {
            if (_engagedId == renderEndpointId)
            {
                return; // idempotent: already holding the keep-alive for this endpoint
            }

            ReleaseLocked();      // engaging a different id drops the old stream first
            TryEngageLocked(renderEndpointId);
        }
    }

    /// <inheritdoc />
    public void Release()
    {
        lock (_gate)
        {
            ReleaseLocked();
        }
    }

    /// <summary>Releases the held keep-alive stream on shutdown.</summary>
    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    // Opens and starts the silent Communications render keep-alive. Any failure leaves the
    // adapter released (no-op degrade); _engagedId is set ONLY on full success.
    private void TryEngageLocked(string renderEndpointId)
    {
        object? enumObj = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            enumObj = comType is null ? null : Activator.CreateInstance(comType);
            if (enumObj is not IMMDeviceEnumeratorForActivation enumerator)
            {
                return;
            }

            enumerator.GetDevice(renderEndpointId, out var device);
            try
            {
                if (StartKeepAlive(device))
                {
                    _engagedId = renderEndpointId;
                }
                else
                {
                    ReleaseLocked();
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception)
        {
            ReleaseLocked(); // never crash the tray on an undocumented COM failure
        }
        finally
        {
            if (enumObj is not null)
            {
                Marshal.ReleaseComObject(enumObj);
            }
        }
    }

    // Activates IAudioClient2 on the render device, tags it Communications, initialises the
    // shared stream, primes silence and starts it. Returns true only when Start succeeds.
    private bool StartKeepAlive(IMMDevice device)
    {
        var iid = AudioClientInterop.AudioClient2Iid;
        device.Activate(ref iid, NativeMethods.ClsCtxAll, IntPtr.Zero, out var clientObj);
        if (clientObj is not IAudioClient2 client)
        {
            return false;
        }

        _client = client;
        return InitializeClient(client, out var frames)
            && PrimeAndStart(client, frames);
    }

    // SetClientProperties(Communications) BEFORE Initialize, then Initialize shared-mode on
    // the endpoint mix format. The mix-format pointer is CoTaskMem and is always freed.
    private static bool InitializeClient(IAudioClient2 client, out uint frames)
    {
        frames = 0;
        var props = new AudioClientProperties
        {
            CbSize = (uint)Marshal.SizeOf<AudioClientProperties>(),
            BIsOffload = 0,
            ECategory = AudioClientInterop.AudioCategoryCommunications,
            Options = 0,
        };
        if (client.SetClientProperties(ref props) < 0)
        {
            return false;
        }

        var mixFormat = IntPtr.Zero;
        try
        {
            if (client.GetMixFormat(out mixFormat) < 0 || mixFormat == IntPtr.Zero)
            {
                return false;
            }

            if (client.Initialize(
                AudioClientInterop.ShareModeShared, 0, AudioClientInterop.BufferDuration,
                0, mixFormat, IntPtr.Zero) < 0)
            {
                return false;
            }

            return client.GetBufferSize(out frames) >= 0;
        }
        finally
        {
            if (mixFormat != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(mixFormat);
            }
        }
    }

    // Gets the render client, primes one SILENT buffer (no sample data), then Starts and
    // HOLDS the stream. Records the render client so Release can stop and free it.
    private bool PrimeAndStart(IAudioClient2 client, uint frames)
    {
        var renderIid = AudioClientInterop.AudioRenderClientIid;
        if (client.GetService(ref renderIid, out var renderObj) < 0
            || renderObj is not IAudioRenderClient render)
        {
            return false;
        }

        _render = render;
        if (frames > 0 && render.GetBuffer(frames, out _) >= 0)
        {
            render.ReleaseBuffer(frames, AudioClientInterop.BufferFlagsSilent);
        }

        if (client.Start() < 0)
        {
            return false;
        }

        _started = true;
        return true;
    }

    // Stops and releases whatever is currently held, resetting to the released state. Every
    // step is best-effort so a rollback can never itself throw (constitution).
    private void ReleaseLocked()
    {
        if (_client is not null && _started)
        {
            try
            {
                _ = _client.Stop();
            }
            catch (Exception)
            {
                // Best-effort stop; a failing Stop must not block the release.
            }
        }

        ReleaseCom(_render);
        ReleaseCom(_client);
        _render = null;
        _client = null;
        _started = false;
        _engagedId = null;
    }

    private static void ReleaseCom(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(comObject);
        }
        catch (Exception)
        {
            // Best-effort release; swallow so shutdown never crashes.
        }
    }
}
