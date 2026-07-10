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

    /// <summary>Number of <see cref="Start"/> calls (a clean restart increments it).</summary>
    public int StartCount { get; private set; }

    /// <summary>Number of <see cref="Stop"/> calls.</summary>
    public int StopCount { get; private set; }

    public void Start()
    {
        IsRunning = true;
        StartCount++;
    }

    public void Stop()
    {
        IsRunning = false;
        StopCount++;
    }

    /// <summary>Simulate one raw advertisement arriving from the OS.</summary>
    public void Emit(BleAdvertisement advertisement)
        => AdvertisementReceived?.Invoke(this, advertisement);
}
