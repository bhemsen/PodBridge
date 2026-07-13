using System.Collections.Concurrent;
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
/// <para><b>Single MTA apartment.</b> <see cref="Engage"/> and <see cref="Release"/> are
/// called from mixed apartments — the WPF STA UI thread (Call-mode toggle) and the session
/// monitor's MTA background thread (auto-switch). A held <see cref="IAudioClient2"/> created
/// on one apartment and released from another can cross-marshal and silently fail to engage
/// HFP, so — mirroring <see cref="WindowsAudioSessionMonitor"/> — this adapter owns ONE
/// dedicated MTA COM thread and marshals ALL COM work (activation, Start, Stop,
/// ReleaseComObject) for the held stream onto it. The stream is created, held, and released
/// in that single apartment; the stream-touching fields are read/written only there.</para>
/// <para><b>Graceful degradation.</b> Any COM failure at any step degrades to a no-op
/// (the keep-alive is simply not held); nothing throws (constitution). All the COM/P-Invoke
/// is isolated here and in <c>AudioClientInterop.cs</c>; Core sees only the interface.</para>
/// <para>Calls are serialized by <see cref="MicPolicyEngine"/>'s apply gate; the adapter
/// additionally guards its dispatch/disposal lifecycle so a shutdown <see cref="Dispose"/>
/// can never race a late <see cref="Engage"/> (idempotent, never throws).</para>
/// </remarks>
public sealed class WindowsCommsProfileEngager : ICommsProfileEngager, IDisposable
{
    private readonly Lock _gate = new();
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _comThread;
    private bool _disposed;

    // Touched ONLY on the dedicated COM thread (no lock needed): the held keep-alive stream
    // is activated, Started/Stopped and released all on that one MTA apartment (#156), so the
    // IAudioClient2/IAudioRenderClient never cross an apartment boundary.
    private string? _engagedId;
    private IAudioClient2? _client;
    private IAudioRenderClient? _render;
    private bool _started;

    /// <summary>Starts the dedicated MTA COM thread that owns the keep-alive stream.</summary>
    public WindowsCommsProfileEngager()
    {
        _comThread = new Thread(ComThreadMain)
        {
            IsBackground = true,
            Name = "PodBridge.CommsProfileEngager",
        };
        _comThread.SetApartmentState(ApartmentState.MTA);
        _comThread.Start();
    }

    /// <inheritdoc />
    public void Engage(string renderEndpointId)
    {
        if (string.IsNullOrEmpty(renderEndpointId))
        {
            return;
        }

        RunOnComThread(() => EngageOnComThread(renderEndpointId));
    }

    /// <inheritdoc />
    public void Release() => RunOnComThread(ReleaseStream);

    /// <summary>
    /// Releases the held keep-alive stream and stops the COM thread on shutdown. The stream
    /// is released ON the COM thread (in <see cref="ComThreadMain"/>'s finally), so it is
    /// freed in the same apartment that created it. Idempotent; never throws.
    /// </summary>
    public void Dispose()
    {
        Thread thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _work.CompleteAdding(); // no new work; the thread drains, releases, then exits
            thread = _comThread;
        }

        thread.Join(TimeSpan.FromSeconds(5));
        _work.Dispose();
        GC.SuppressFinalize(this);
    }

    // Marshals COM work onto the single MTA thread and blocks until it completes, so the
    // whole stream lifetime stays in one apartment. A late call after Dispose is a no-op
    // (never throws). Callers are the UI or session-monitor thread — never the COM thread
    // itself — so blocking here can never deadlock the COM thread.
    private void RunOnComThread(Action work)
    {
        var completion = new object();
        var completed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _work.Add(() =>
            {
                try
                {
                    work();
                }
                finally
                {
                    lock (completion)
                    {
                        completed = true;
                        Monitor.Pulse(completion);
                    }
                }
            });
        }

        lock (completion)
        {
            while (!completed)
            {
                Monitor.Wait(completion);
            }
        }
    }

    // Owns all COM work on one MTA thread: run each marshaled item, and on shutdown release
    // any still-held keep-alive in the apartment that created it (finally).
    private void ComThreadMain()
    {
        try
        {
            foreach (var work in _work.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception)
                {
                    // A work item never throws (Engage/Release swallow COM faults), but guard
                    // anyway so the COM thread can never die and strand a blocked caller.
                }
            }
        }
        catch (Exception)
        {
            // GetConsumingEnumerable only faults after disposal; degrade to inert.
        }
        finally
        {
            ReleaseStream(); // drop any held keep-alive on THIS apartment
        }
    }

    // Runs on the COM thread. Idempotent: re-engaging the already-held id is a no-op;
    // engaging a different id drops the current stream first (interface contract).
    private void EngageOnComThread(string renderEndpointId)
    {
        if (_engagedId == renderEndpointId)
        {
            return;
        }

        ReleaseStream();
        TryEngage(renderEndpointId);
    }

    // Opens and starts the silent Communications render keep-alive. Any failure leaves the
    // adapter released (no-op degrade); _engagedId is set ONLY on full success. On the COM thread.
    private void TryEngage(string renderEndpointId)
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
                    ReleaseStream();
                }
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception)
        {
            ReleaseStream(); // never crash the tray on an undocumented COM failure
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
            if (clientObj is not null)
            {
                Marshal.ReleaseComObject(clientObj); // release the activated RCW on the cast miss
            }

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
    // step is best-effort so a rollback can never itself throw (constitution). On the COM thread.
    private void ReleaseStream()
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
