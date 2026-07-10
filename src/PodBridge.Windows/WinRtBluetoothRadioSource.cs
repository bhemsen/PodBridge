using PodBridge.Core.Bluetooth;
using Windows.Devices.Radios;

namespace PodBridge.Windows;

/// <summary>
/// WinRT implementation of <see cref="IBluetoothRadioSource"/> using
/// <see cref="Radio"/> (<c>Windows.Devices.Radios</c>). It locates the Bluetooth
/// radio and forwards its <see cref="Radio.StateChanged"/> transitions to
/// <see cref="BleScannerSupervisor"/> so the driver-free watcher restarts cleanly
/// after a radio power toggle. Tier 1: no driver, no admin (<c>asInvoker</c>) — the
/// same read-only radio API already used by <c>WinRtConnectionMonitor</c>.
/// </summary>
/// <remarks>
/// Radio enumeration can fault on some hosts (research footgun) and requires the
/// process architecture to match the OS (ship x64/ARM64, never x86); any failure is
/// swallowed and simply leaves the supervisor's conservative "assume on" default in
/// place, so the scanner keeps running (constitution: graceful degradation, never crash).
/// </remarks>
public sealed class WinRtBluetoothRadioSource : IBluetoothRadioSource, IDisposable
{
    private readonly object _gate = new();
    private Radio? _radio;
    private bool _started;

    /// <inheritdoc />
    public event EventHandler<bool>? RadioStateChanged;

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
        }

        // Fire-and-forget: enumeration is async and non-critical — the supervisor already
        // started scanning, so a slow or failed attach only forgoes toggle-restart.
        _ = AttachAsync();
    }

    /// <inheritdoc />
    public void Stop()
    {
        Radio? radio;
        lock (_gate)
        {
            _started = false;
            radio = _radio;
            _radio = null;
        }

        if (radio is not null)
        {
            radio.StateChanged -= OnStateChanged;
        }
    }

    /// <summary>Detaches from the radio's state event.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task AttachAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync().AsTask().ConfigureAwait(false);
            Radio? bluetooth = null;
            foreach (var radio in radios)
            {
                if (radio.Kind == RadioKind.Bluetooth)
                {
                    bluetooth = radio;
                    break;
                }
            }

            if (bluetooth is null)
            {
                return;
            }

            lock (_gate)
            {
                if (!_started)
                {
                    return; // Stop() ran during the await; don't attach after teardown
                }

                _radio = bluetooth;
            }

            bluetooth.StateChanged += OnStateChanged;
        }
        catch (Exception)
        {
            // Radio enumeration faulted: leave "assume on"; the scanner keeps running.
        }
    }

    private void OnStateChanged(Radio sender, object args)
        => RadioStateChanged?.Invoke(this, sender.State == RadioState.On);
}
