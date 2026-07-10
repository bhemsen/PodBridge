using PodBridge.Core.Bluetooth;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for <see cref="BleScannerSupervisor"/>:
/// the driver-free BLE watcher restarts cleanly on a Bluetooth-radio off→on toggle, stops on
/// radio-off, ignores redundant events, and never gates the initial start on radio state so
/// Tier-1 scanning cannot regress if the radio source is silent (spec
/// docs/specs/spec-model-coverage-hardening.md, hardening pass).
/// </summary>
public class BleScannerSupervisorTests
{
    [Fact]
    public void Start_StartsScanningImmediately_RegardlessOfRadioEvents()
    {
        var scanner = new FakeBleScanner();
        var radio = new FakeBluetoothRadioSource();
        using var supervisor = new BleScannerSupervisor(scanner, radio);

        supervisor.Start();

        Assert.True(scanner.IsRunning);
        Assert.Equal(1, scanner.StartCount);
    }

    [Fact]
    public void RadioOffThenOn_RestartsScannerWithFreshWatcher()
    {
        var scanner = new FakeBleScanner();
        var radio = new FakeBluetoothRadioSource();
        using var supervisor = new BleScannerSupervisor(scanner, radio);
        supervisor.Start();

        radio.Raise(isOn: false); // radio turned off
        Assert.False(scanner.IsRunning);
        Assert.Equal(1, scanner.StopCount);

        radio.Raise(isOn: true); // radio back on: clean restart (Stop then Start)
        Assert.True(scanner.IsRunning);
        Assert.Equal(2, scanner.StartCount); // a second, fresh Start()
        Assert.Equal(2, scanner.StopCount);  // dropped the aborted watcher first
    }

    [Fact]
    public void RedundantRadioEvents_DoNotChurnTheScanner()
    {
        var scanner = new FakeBleScanner();
        var radio = new FakeBluetoothRadioSource();
        using var supervisor = new BleScannerSupervisor(scanner, radio);
        supervisor.Start();

        radio.Raise(isOn: true); // already assumed on → no-op
        radio.Raise(isOn: true); // still on → no-op
        Assert.Equal(1, scanner.StartCount);
        Assert.Equal(0, scanner.StopCount);

        radio.Raise(isOn: false);
        radio.Raise(isOn: false); // repeated off → single stop
        Assert.Equal(1, scanner.StopCount);
    }

    [Fact]
    public void RadioEventsBeforeStart_AreIgnored()
    {
        var scanner = new FakeBleScanner();
        var radio = new FakeBluetoothRadioSource();
        using var supervisor = new BleScannerSupervisor(scanner, radio);

        radio.Raise(isOn: false); // not armed yet
        radio.Raise(isOn: true);

        Assert.Equal(0, scanner.StartCount);
        Assert.Equal(0, scanner.StopCount);
    }

    [Fact]
    public void Dispose_UnsubscribesFromRadio_NoFurtherRestarts()
    {
        var scanner = new FakeBleScanner();
        var radio = new FakeBluetoothRadioSource();
        var supervisor = new BleScannerSupervisor(scanner, radio);
        supervisor.Start();
        supervisor.Dispose();

        radio.Raise(isOn: false);
        radio.Raise(isOn: true);

        Assert.Equal(1, scanner.StartCount); // only the initial Start survived
        Assert.Equal(0, scanner.StopCount);
    }
}
