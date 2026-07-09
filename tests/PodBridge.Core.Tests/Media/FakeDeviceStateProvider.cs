using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;

namespace PodBridge.Core.Tests.Media;

/// <summary>
/// Device-independent <see cref="IDeviceStateProvider"/> that lets a test publish
/// arbitrary in-ear/out-of-ear transitions directly — <b>without</b> the
/// connection gate the real <c>DeviceStateTracker</c> applies. This is deliberate:
/// it lets the engine's own <see cref="IConnectionMonitor"/> gate be proven by
/// feeding transitions while the monitor reports "not connected".
/// </summary>
internal sealed class FakeDeviceStateProvider : IDeviceStateProvider
{
    public DeviceState Current { get; private set; } = DeviceState.Unknown;

    public event EventHandler<DeviceState>? StateChanged;

    /// <summary>Publishes a full state and raises <see cref="StateChanged"/>.</summary>
    public void Publish(DeviceState state)
    {
        Current = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Publishes a live state whose <see cref="DeviceState.AnyInEar"/> is
    /// <paramref name="inEar"/> (models one bud in/out of an ear).
    /// </summary>
    public void SetInEar(bool inEar) => Publish(new DeviceState { LeftInEar = inEar, IsLive = true });
}
