using PodBridge.Core.Bluetooth;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent stand-in for the WinRT Bluetooth-radio state source: lets a test
/// raise synthetic radio power-state transitions so <see cref="BleScannerSupervisor"/>'s
/// toggle-restart logic runs with no physical radio.
/// </summary>
internal sealed class FakeBluetoothRadioSource : IBluetoothRadioSource
{
    public event EventHandler<bool>? RadioStateChanged;

    public bool IsStarted { get; private set; }

    public void Start() => IsStarted = true;

    public void Stop() => IsStarted = false;

    /// <summary>Simulate a radio power-state transition (true = on, false = off).</summary>
    public void Raise(bool isOn) => RadioStateChanged?.Invoke(this, isOn);
}
