using PodBridge.Core.Bluetooth;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Bluetooth;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Diagnostics;

/// <summary>
/// Device-independent tests for <see cref="BleParseHistoryRecorder"/>: no physical AirPods,
/// driven by <see cref="FakeBleScanner"/> (constitution Tier-1 test gate).
/// </summary>
public class BleParseHistoryRecorderTests
{
    private static BleAdvertisement AppleAdv(byte[] manufacturerData, ulong address = 0x1234_5678_9ABCUL)
        => new(address, RssiDbm: -50, CompanyId: AppleContinuity.AppleCompanyId, manufacturerData);

    [Fact]
    public void ValidFrame_RecordsParsedSuccess_WithMaskedAddress_AndModel()
    {
        var scanner = new FakeBleScanner();
        using var recorder = new BleParseHistoryRecorder(scanner);
        var frame = ContinuityFixtures.Proximity(status: 0x28, battery: 0x57, chargingCase: 0x49, model: 0x200E);

        scanner.Emit(AppleAdv(frame, address: 0x1234_5678_9ABCUL));

        var entry = Assert.Single(recorder.Recent);
        Assert.True(entry.ParsedSuccessfully);
        Assert.Equal(AirPodsModel.AirPodsPro, entry.Model);
        // Only the last two octets survive — the full 48-bit address never appears.
        // 0x1234_5678_9ABC's low two bytes are 0x9A and 0xBC.
        Assert.Equal("**:**:**:**:9A:BC", entry.MaskedAddress);
        Assert.DoesNotContain("123456789ABC", entry.MaskedAddress, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedPayload_RecordsUnparsed_NeverThrows()
    {
        var scanner = new FakeBleScanner();
        using var recorder = new BleParseHistoryRecorder(scanner);

        scanner.Emit(AppleAdv([0x07, 0x02, 0x00])); // truncated/garbage: not a valid proximity block

        var entry = Assert.Single(recorder.Recent);
        Assert.False(entry.ParsedSuccessfully);
        Assert.Null(entry.Model);
    }

    [Fact]
    public void NonAppleAdvertisement_IsIgnored()
    {
        var scanner = new FakeBleScanner();
        using var recorder = new BleParseHistoryRecorder(scanner);

        scanner.Emit(new BleAdvertisement(0x1, -50, CompanyId: 0x0001, ManufacturerData: []));

        Assert.Empty(recorder.Recent);
    }

    [Fact]
    public void HistoryIsCappedAtCapacity_OldestDropsFirst()
    {
        var scanner = new FakeBleScanner();
        using var recorder = new BleParseHistoryRecorder(scanner);
        var frame = ContinuityFixtures.Proximity(status: 0x28, battery: 0x57, chargingCase: 0x49, model: 0x200E);

        for (ulong i = 0; i < BleParseHistoryRecorder.Capacity + 5; i++)
        {
            scanner.Emit(AppleAdv(frame, address: i));
        }

        Assert.Equal(BleParseHistoryRecorder.Capacity, recorder.Recent.Count);
        // The oldest five (addresses 0..4) were dropped; the newest surviving address is 4+... -> the
        // last entry's masked low byte matches the final emitted address (Capacity + 4).
        var expectedLast = $"**:**:**:**:00:{(byte)(BleParseHistoryRecorder.Capacity + 4):X2}";
        Assert.Equal(expectedLast, recorder.Recent[^1].MaskedAddress);
    }

    [Fact]
    public void Dispose_UnsubscribesFromScanner()
    {
        var scanner = new FakeBleScanner();
        var recorder = new BleParseHistoryRecorder(scanner);
        recorder.Dispose();

        scanner.Emit(AppleAdv(ContinuityFixtures.Proximity(0x28, 0x57, 0x49)));

        Assert.Empty(recorder.Recent);
    }
}
