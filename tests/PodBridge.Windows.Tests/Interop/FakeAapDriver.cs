using System.Collections.Concurrent;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows.Tests.Interop;

/// <summary>
/// Device-independent stand-in for the Win32 driver seam. Lets a test decide whether the
/// driver "interface" is present (via <see cref="InterfacePath"/>) and hands out a
/// <see cref="FakeAapDriverChannel"/> so <c>DriverAapTransport</c>'s connect / send /
/// receive-loop / graceful-absence logic is exercised with no driver or hardware.
/// </summary>
internal sealed class FakeAapDriverInterop : IAapDriverInterop
{
    /// <summary>The interface path to report; <see langword="null"/> == driver absent.</summary>
    public string? InterfacePath { get; set; } = @"\\?\podbridge-aap#test";

    /// <summary>The channel handed out by the most recent <see cref="Open"/>.</summary>
    public FakeAapDriverChannel? LastChannel { get; private set; }

    /// <summary>Number of times <see cref="Open"/> was called (idempotency checks).</summary>
    public int OpenCount { get; private set; }

    public bool TryFindInterfacePath(out string? interfacePath)
    {
        interfacePath = InterfacePath;
        return interfacePath is not null;
    }

    public IAapDriverChannel Open(string interfacePath)
    {
        OpenCount++;
        var channel = new FakeAapDriverChannel();
        LastChannel = channel;
        return channel;
    }
}

/// <summary>
/// Fake open channel: records sent frames and delivers test-pushed inbound frames through a
/// blocking <see cref="Receive"/> that mirrors the driver's inverted-call IOCTL —
/// <see cref="Emit"/> queues a frame, <see cref="CancelPendingReceive"/> unblocks with 0.
/// </summary>
internal sealed class FakeAapDriverChannel : IAapDriverChannel
{
    private readonly BlockingCollection<byte[]> _inbound = new();
    private readonly List<byte[]> _sent = [];

    public bool Connected { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>Frames written via <see cref="Send"/>, in order.</summary>
    public IReadOnlyList<byte[]> Sent
    {
        get { lock (_sent) { return _sent.ToArray(); } }
    }

    public void Connect() => Connected = true;

    public void Send(ReadOnlyMemory<byte> frame)
    {
        lock (_sent)
        {
            _sent.Add(frame.ToArray());
        }
    }

    public int Receive(byte[] buffer)
    {
        byte[] frame;
        try
        {
            frame = _inbound.Take(); // blocks until Emit or CompleteAdding (cancel/close)
        }
        catch (InvalidOperationException)
        {
            return 0; // completed & drained == cancelled / channel gone
        }

        var count = Math.Min(frame.Length, buffer.Length);
        Array.Copy(frame, buffer, count);
        return count;
    }

    /// <summary>Simulate an inbound AAP frame arriving from the AirPods.</summary>
    public void Emit(byte[] frame) => _inbound.Add(frame);

    public void CancelPendingReceive() => _inbound.CompleteAdding();

    public void Dispose()
    {
        Disposed = true;
        _inbound.Dispose();
    }
}
