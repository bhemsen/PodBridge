using PodBridge.Core.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace PodBridge.Windows;

/// <summary>
/// WinRT implementation of <see cref="IBleScanner"/> using
/// <see cref="BluetoothLEAdvertisementWatcher"/>. Active scanning with a
/// company-id <c>0x004C</c> manufacturer-data filter surfaces Apple-Continuity
/// proximity advertisements; the handler forwards the <b>raw</b> wire data
/// (address, RSSI, company id, payload) to Core as a <see cref="BleAdvertisement"/>.
/// It performs no decoding, identification, or connection gating — those live in
/// Core (<see cref="Core.Protocol.ContinuityParser"/> via
/// <see cref="DeviceStateTracker"/>). Tier 1: no driver, no admin (<c>asInvoker</c>).
/// API choices per docs/research/ble-watcher.md (#15).
/// </summary>
/// <remarks>
/// Active scanning also requests the scan response (SCAN_RSP), which Windows
/// surfaces as a <em>separate</em> <see cref="BluetoothLEAdvertisementWatcher.Received"/>
/// event, so the full manufacturer payload is captured across models/firmware
/// (research decision). No appxmanifest capability is required for the unpackaged
/// <c>asInvoker</c> desktop tier; a <c>bluetooth</c> capability would only apply if
/// PodBridge is later packaged as MSIX (Phase 5).
/// </remarks>
public sealed class WinRtBleScanner : IBleScanner, IDisposable
{
    // Apple's Bluetooth SIG company identifier — the AirPods advertisement-path id.
    private const ushort AppleCompanyId = 0x004C;

    private readonly object _gate = new();
    private BluetoothLEAdvertisementWatcher? _watcher;

    /// <inheritdoc />
    public event EventHandler<BleAdvertisement>? AdvertisementReceived;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_watcher is not null)
            {
                return;
            }

            var watcher = new BluetoothLEAdvertisementWatcher
            {
                // Active also requests the scan response so the full Apple
                // manufacturer payload is captured (research: Active > Passive).
                ScanningMode = BluetoothLEScanningMode.Active,
            };

            // Company-id 0x004C manufacturer-data filter, set before Start(), to
            // cut callback volume; empty Data admits all Apple packets. The
            // handler re-checks the company id because hardware-offload filter
            // behaviour varies across radios/Windows builds (research decision).
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(
                new BluetoothLEManufacturerData { CompanyId = AppleCompanyId });

            watcher.Received += OnReceived;
            _watcher = watcher;
            watcher.Start();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        BluetoothLEAdvertisementWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        // Unsubscribe first so no handler fires after teardown, then stop the
        // watcher so no OS scan/callback registration leaks on shutdown.
        watcher.Received -= OnReceived;
        if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            watcher.Stop();
        }
    }

    /// <summary>Stops scanning and releases the watcher subscription.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void OnReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // Belt-and-suspenders: re-check the company id per manufacturer section
        // before forwarding, since the built-in filter's offload behaviour varies.
        foreach (var section in args.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != AppleCompanyId)
            {
                continue;
            }

            var advertisement = new BleAdvertisement(
                args.BluetoothAddress,
                args.RawSignalStrengthInDBm,
                section.CompanyId,
                ReadBytes(section.Data));

            AdvertisementReceived?.Invoke(this, advertisement);
        }
    }

    // Copies the manufacturer-section payload (company id already stripped by
    // WinRT) into a managed array for the Core parser; no decoding here.
    private static byte[] ReadBytes(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        if (buffer.Length > 0)
        {
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
        }

        return bytes;
    }
}
