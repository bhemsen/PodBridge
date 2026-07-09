using PodBridge.Core.Bluetooth;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent stand-in for the WinRT advertisement watcher: lets a test
/// emit synthetic raw <see cref="BleAdvertisement"/> frames on demand, so the Core
/// parse/gate/staleness pipeline runs the Tier-1 test gate with no physical AirPods.
/// </summary>
internal sealed class FakeBleScanner : IBleScanner
{
    public event EventHandler<BleAdvertisement>? AdvertisementReceived;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Simulate one raw advertisement arriving from the OS.</summary>
    public void Emit(BleAdvertisement advertisement)
        => AdvertisementReceived?.Invoke(this, advertisement);
}
