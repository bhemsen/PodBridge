using PodBridge.Core.Bluetooth;
using PodBridge.Core.Protocol;

namespace PodBridge.Core.Diagnostics;

/// <summary>
/// Default <see cref="IBleParseHistory"/>: subscribes to the raw <see cref="IBleScanner"/>
/// (independently of <see cref="Bluetooth.DeviceStateTracker"/>'s connection-gated pipeline,
/// so diagnostics sees parse attempts even while no AirPods are connected) and records the
/// last <see cref="Capacity"/> Apple-company-id advertisements, masking the address before
/// it is ever stored (constitution: local-only, no durable identifier leak).
/// <para>
/// Malformed / truncated / unknown payloads never throw here: a failed
/// <see cref="ContinuityParser.TryParse"/> is recorded as <c>ParsedSuccessfully = false</c>,
/// not an exception (constitution: graceful degradation).
/// </para>
/// </summary>
public sealed class BleParseHistoryRecorder : IBleParseHistory, IDisposable
{
    /// <summary>Maximum number of recent entries retained.</summary>
    public const int Capacity = 10;

    private readonly IBleScanner _scanner;
    private readonly Lock _gate = new();
    private readonly Queue<BleParseResult> _recent = new();

    /// <summary>Subscribes to <paramref name="scanner"/>'s raw advertisement feed.</summary>
    public BleParseHistoryRecorder(IBleScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        _scanner = scanner;
        _scanner.AdvertisementReceived += OnAdvertisementReceived;
    }

    /// <inheritdoc/>
    public IReadOnlyList<BleParseResult> Recent
    {
        get
        {
            lock (_gate)
            {
                return _recent.ToArray();
            }
        }
    }

    /// <summary>Unsubscribes from the scanner.</summary>
    public void Dispose()
    {
        _scanner.AdvertisementReceived -= OnAdvertisementReceived;
        GC.SuppressFinalize(this);
    }

    private void OnAdvertisementReceived(object? sender, BleAdvertisement advertisement)
    {
        if (!AppleContinuity.IsAppleManufacturerData(advertisement.CompanyId))
        {
            return; // diagnostics history is scoped to Apple-Continuity frames
        }

        var parsed = ContinuityParser.TryParse(advertisement.ManufacturerData, out var data);
        var result = new BleParseResult
        {
            ParsedSuccessfully = parsed,
            MaskedAddress = MaskAddress(advertisement.Address),
            Model = parsed ? data!.Model : null,
        };

        lock (_gate)
        {
            _recent.Enqueue(result);
            while (_recent.Count > Capacity)
            {
                _recent.Dequeue();
            }
        }
    }

    // Keeps only the last two octets (short-lived correlation is all Address is ever used
    // for; the full 48-bit value is never stored in diagnostics).
    private static string MaskAddress(ulong address)
    {
        var b1 = (byte)((address >> 8) & 0xFF);
        var b0 = (byte)(address & 0xFF);
        return $"**:**:**:**:{b1:X2}:{b0:X2}";
    }
}
