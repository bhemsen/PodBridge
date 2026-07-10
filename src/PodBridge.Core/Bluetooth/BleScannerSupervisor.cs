namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Keeps the driver-free Tier-1 BLE watcher alive across Bluetooth-radio power
/// toggles. It owns the <see cref="IBleScanner"/> lifecycle relative to the radio:
/// on a radio <b>off</b> edge it stops the scanner; on the <b>off→on</b> edge it
/// restarts it with a <em>fresh</em> watcher (<see cref="IBleScanner.Stop"/> then
/// <see cref="IBleScanner.Start"/>), because the WinRT advertisement watcher does not
/// resume scanning by itself once the radio was powered off.
/// <para>
/// It is deliberately conservative: <see cref="Start"/> starts the scanner
/// unconditionally (identical to the pre-hardening startup, so Tier-1 never regresses
/// if the radio source is silent or faults), and only the radio-off/on transitions
/// add the restart behaviour on top. Core stays OS-free — it drives only the
/// <see cref="IBleScanner"/> and <see cref="IBluetoothRadioSource"/> abstractions.
/// </para>
/// </summary>
public sealed class BleScannerSupervisor : IDisposable
{
    private readonly IBleScanner _scanner;
    private readonly IBluetoothRadioSource _radioSource;
    private readonly Lock _gate = new();

    private bool _started;
    private bool _radioOff; // last observed radio state; false ⇒ assume on (never gate startup)

    /// <summary>
    /// Wires the supervisor to the scanner and the radio-state source and subscribes to
    /// radio transitions. It only subscribes; starting the scanner is deferred to
    /// <see cref="Start"/> so the composition root controls ordering (as with the
    /// Phase-2 tracker/scanner split).
    /// </summary>
    public BleScannerSupervisor(IBleScanner scanner, IBluetoothRadioSource radioSource)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(radioSource);
        _scanner = scanner;
        _radioSource = radioSource;
        _radioSource.RadioStateChanged += OnRadioStateChanged;
    }

    /// <summary>Starts scanning now (unconditionally) and arms the toggle-restart logic.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _started = true;
            _radioOff = false;
            _scanner.Start();
        }
    }

    /// <summary>Unsubscribes from the radio source; the scanner's lifetime is the container's.</summary>
    public void Dispose()
    {
        _radioSource.RadioStateChanged -= OnRadioStateChanged;
        GC.SuppressFinalize(this);
    }

    private void OnRadioStateChanged(object? sender, bool isOn)
    {
        lock (_gate)
        {
            if (!_started || isOn == !_radioOff)
            {
                return; // not armed yet, or no real transition (redundant event)
            }

            _radioOff = !isOn;
            if (isOn)
            {
                // Off→on: force a fresh watcher — a bare Start() is a no-op while the
                // aborted watcher handle is still held.
                _scanner.Stop();
                _scanner.Start();
            }
            else
            {
                _scanner.Stop();
            }
        }
    }
}
