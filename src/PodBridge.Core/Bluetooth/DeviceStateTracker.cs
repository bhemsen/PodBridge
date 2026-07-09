using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Turns raw BLE advertisements into the current AirPods <see cref="DeviceState"/>.
/// Subscribes to an <see cref="IBleScanner"/> (raw) and the Phase-1
/// <see cref="IConnectionMonitor"/>, decodes Apple-Continuity frames with
/// <see cref="ContinuityParser"/>, and applies the connection gate, the
/// strongest-RSSI single-device heuristic, and the staleness timeout.
/// <list type="bullet">
/// <item>AirPods are identified on the advertisement path by company id
/// <c>0x004C</c> plus a valid proximity frame — telemetry only; the connection
/// path is never touched.</item>
/// <item>Advertisements are treated as live only while the monitor reports
/// <see cref="ConnectionStatus.Connected"/>; otherwise no live battery is reported.</item>
/// <item>State goes back to <see cref="DeviceState.Unknown"/> after a 30 s timeout
/// with no fresh advertisement (never a stale value shown as live).</item>
/// </list>
/// </summary>
public sealed class DeviceStateTracker : IDeviceStateProvider, IDisposable
{
    /// <summary>Advertisements older than this with no refresh are treated as stale.</summary>
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromSeconds(30);

    private readonly IBleScanner _scanner;
    private readonly IConnectionMonitor _connectionMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _staleTimer;
    private readonly Lock _gate = new();

    private ulong? _trackedAddress;
    private short _trackedRssi;
    private DateTimeOffset? _lastSeen;
    private DeviceState _current = DeviceState.Unknown;

    /// <summary>
    /// Wires the tracker to its sources. It only subscribes to events; starting and
    /// stopping the scanner is the composition root's responsibility.
    /// </summary>
    /// <param name="scanner">Raw BLE advertisement source.</param>
    /// <param name="connectionMonitor">Phase-1 connection state used to gate telemetry.</param>
    /// <param name="timeProvider">Clock/timer source; defaults to <see cref="TimeProvider.System"/>.</param>
    public DeviceStateTracker(
        IBleScanner scanner,
        IConnectionMonitor connectionMonitor,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(connectionMonitor);

        _scanner = scanner;
        _connectionMonitor = connectionMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _staleTimer = _timeProvider.CreateTimer(
            OnStaleTick, state: null, dueTime: Timeout.InfiniteTimeSpan, period: Timeout.InfiniteTimeSpan);

        _scanner.AdvertisementReceived += OnAdvertisementReceived;
        _connectionMonitor.StatusChanged += OnConnectionChanged;
    }

    /// <inheritdoc/>
    public event EventHandler<DeviceState>? StateChanged;

    /// <inheritdoc/>
    public DeviceState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Unsubscribes from the sources and disposes the staleness timer.</summary>
    public void Dispose()
    {
        _scanner.AdvertisementReceived -= OnAdvertisementReceived;
        _connectionMonitor.StatusChanged -= OnConnectionChanged;
        _staleTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnAdvertisementReceived(object? sender, BleAdvertisement advertisement)
    {
        if (advertisement.CompanyId != AppleContinuity.AppleCompanyId)
        {
            return; // non-Apple advertisement: ignored
        }

        if (!ContinuityParser.TryParse(advertisement.ManufacturerData, out var parsed))
        {
            return; // Apple data but not a proximity-pairing frame
        }

        DeviceState? published = null;
        lock (_gate)
        {
            if (_connectionMonitor.CurrentStatus != ConnectionStatus.Connected)
            {
                return; // gate: no AirPods connected → advertisements are not live
            }

            var now = _timeProvider.GetUtcNow();
            if (!ShouldAdopt(advertisement, now))
            {
                return; // a weaker, different device while ours is fresh → ignore
            }

            _trackedAddress = advertisement.Address;
            _trackedRssi = advertisement.RssiDbm;
            _lastSeen = now;
            _staleTimer.Change(StaleTimeout, Timeout.InfiniteTimeSpan);

            var next = ToLiveState(parsed);
            if (next != _current)
            {
                _current = next;
                published = next;
            }
        }

        RaiseIfChanged(published);
    }

    // Single tracked device, disambiguated by strongest RSSI among 0x004C frames
    // received while connected (Apple rotates random addresses, so an advertisement
    // cannot be cryptographically bound to the user's AirPods in this phase).
    private bool ShouldAdopt(BleAdvertisement advertisement, DateTimeOffset now)
    {
        if (_trackedAddress is null || _lastSeen is null)
        {
            return true; // nothing tracked yet
        }

        if (now - _lastSeen.Value >= StaleTimeout)
        {
            return true; // tracked device went stale; adopt any fresh one
        }

        if (advertisement.Address == _trackedAddress)
        {
            return true; // same device: refresh
        }

        return advertisement.RssiDbm >= _trackedRssi; // a nearer device displaces
    }

    private void OnConnectionChanged(object? sender, ConnectionStatus status)
    {
        if (status == ConnectionStatus.Connected)
        {
            return; // wait for advertisements to populate live state
        }

        DeviceState? published;
        lock (_gate)
        {
            published = GoUnknownLocked();
        }

        RaiseIfChanged(published);
    }

    private void OnStaleTick(object? state)
    {
        DeviceState? published;
        lock (_gate)
        {
            if (_lastSeen is null || _timeProvider.GetUtcNow() - _lastSeen.Value < StaleTimeout)
            {
                return; // refreshed since the timer was scheduled
            }

            published = GoUnknownLocked();
        }

        RaiseIfChanged(published);
    }

    // Resets tracking and returns the state to publish, or null if already Unknown.
    private DeviceState? GoUnknownLocked()
    {
        _trackedAddress = null;
        _trackedRssi = 0;
        _lastSeen = null;
        if (_current == DeviceState.Unknown)
        {
            return null;
        }

        _current = DeviceState.Unknown;
        return _current;
    }

    private void RaiseIfChanged(DeviceState? published)
    {
        if (published is not null)
        {
            StateChanged?.Invoke(this, published);
        }
    }

    private static DeviceState ToLiveState(ContinuityProximityData data) => new()
    {
        Model = data.Model,
        LeftBatteryPercent = data.LeftBatteryPercent,
        RightBatteryPercent = data.RightBatteryPercent,
        CaseBatteryPercent = data.CaseBatteryPercent,
        LeftCharging = data.LeftCharging,
        RightCharging = data.RightCharging,
        CaseCharging = data.CaseCharging,
        LeftInEar = data.LeftInEar,
        RightInEar = data.RightInEar,
        IsLive = true,
    };
}
