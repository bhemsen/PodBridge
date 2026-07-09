using System.Runtime.InteropServices;
using PodBridge.Core.Audio;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows implementation of <see cref="IAudioEndpointChangeMonitor"/> — raises
/// <see cref="EndpointsChanged"/> when an audio endpoint is added, removed, its state
/// changes, or a default device changes, so <c>MicPolicyEngine.Refresh</c> re-evaluates
/// the single-device degrade warning live (the fallback mic appearing/disappearing).
/// Tier 1: driver-free, admin-free (<c>asInvoker</c>); it only registers a Core Audio
/// <see cref="IMMNotificationClient"/> callback — it never opens a stream or switches an
/// endpoint.
/// </summary>
/// <remarks>
/// The enumerator COM object and the callback object are kept alive for the monitor's
/// lifetime (the native enumerator holds the callback's CCW until unregister). Volume /
/// property changes are ignored — only topology changes matter. Callbacks arrive on an
/// OS background thread; the raise is marshalled onto the thread pool because a
/// notification callback must not block or re-enter the enumerator (which
/// <c>Refresh</c> does via <see cref="IAudioPolicy"/>). Any COM fault degrades to inert,
/// never a crash (constitution: graceful degradation).
/// </remarks>
public sealed class WindowsAudioEndpointChangeMonitor : IAudioEndpointChangeMonitor, IDisposable
{
    private readonly object _gate = new();

    // Held for the monitor's lifetime: the native enumerator keeps the callback's CCW
    // alive until it is unregistered, so both references must outlive registration.
    private object? _enumeratorObject;
    private NotificationClient? _client;
    private bool _started;

    /// <inheritdoc />
    public event EventHandler? EndpointsChanged;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            TryRegister();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            Unregister();
        }
    }

    /// <summary>Stops the monitor and releases its COM references.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void TryRegister()
    {
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            _enumeratorObject = comType is null ? null : Activator.CreateInstance(comType);
            if (_enumeratorObject is not IMMDeviceEnumeratorWithCallback enumerator)
            {
                ReleaseEnumerator();
                return;
            }

            _client = new NotificationClient(RaiseChanged);
            _ = enumerator.RegisterEndpointNotificationCallback(_client);
        }
        catch (Exception)
        {
            // No enumerator, or registration failed — go inert (graceful degradation).
            _client = null;
            ReleaseEnumerator();
        }
    }

    private void Unregister()
    {
        try
        {
            if (_enumeratorObject is IMMDeviceEnumeratorWithCallback enumerator && _client is not null)
            {
                _ = enumerator.UnregisterEndpointNotificationCallback(_client);
            }
        }
        catch (COMException)
        {
            // The enumerator is already gone; nothing left to unregister.
        }
        finally
        {
            _client = null;
            ReleaseEnumerator();
        }
    }

    private void ReleaseEnumerator()
    {
        if (_enumeratorObject is not null)
        {
            Marshal.ReleaseComObject(_enumeratorObject);
            _enumeratorObject = null;
        }
    }

    // Marshal off the OS callback thread: the handler (Refresh) re-enumerates endpoints
    // via IAudioPolicy, which must not run inside a notification callback.
    private void RaiseChanged()
        => ThreadPool.QueueUserWorkItem(_ => EndpointsChanged?.Invoke(this, EventArgs.Empty));

    // Every topology change collapses to one signal; volume/level property changes
    // (OnPropertyValueChanged) fire constantly and are intentionally ignored.
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly Action _onChanged;

        internal NotificationClient(Action onChanged) => _onChanged = onChanged;

        public void OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState) => _onChanged();

        public void OnDeviceAdded(string pwstrDeviceId) => _onChanged();

        public void OnDeviceRemoved(string pwstrDeviceId) => _onChanged();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? pwstrDefaultDeviceId) =>
            _onChanged();

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
            // Ignored: not a topology change.
        }
    }
}
