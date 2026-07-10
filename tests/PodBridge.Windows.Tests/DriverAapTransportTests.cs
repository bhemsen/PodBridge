using System.ComponentModel;
using PodBridge.Windows;
using PodBridge.Windows.Tests.Interop;
using Xunit;

namespace PodBridge.Windows.Tests;

/// <summary>
/// Device-independent tests for <see cref="DriverAapTransport"/> at the Win32 driver seam
/// (fake interop). They cover the graceful-absence contract (driver not installed → the
/// Tier-1 default), the connect / send round-trip over the IOCTLs, the inverted-call
/// receive loop surfacing inbound frames, and clean teardown. The real driver is verified
/// by a human smoke test (spec docs/specs/spec-advanced-driver-anc.md; CI has no hardware).
/// </summary>
public sealed class DriverAapTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void IsAvailable_true_when_the_driver_interface_is_present()
    {
        using var transport = new DriverAapTransport(new FakeAapDriverInterop());

        Assert.True(transport.IsAvailable);
    }

    [Fact]
    public void IsAvailable_false_when_the_driver_is_absent()
    {
        using var transport = new DriverAapTransport(new FakeAapDriverInterop { InterfacePath = null });

        Assert.False(transport.IsAvailable);
    }

    [Fact]
    public async Task ConnectAsync_when_the_driver_is_absent_is_a_no_op_and_does_not_throw()
    {
        var interop = new FakeAapDriverInterop { InterfacePath = null };
        using var transport = new DriverAapTransport(interop);

        await transport.ConnectAsync();

        Assert.False(transport.IsAvailable);
        Assert.Equal(0, interop.OpenCount); // nothing opened -> Tier-1 unaffected
    }

    [Fact]
    public async Task ConnectAsync_opens_and_connects_the_channel()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);

        await transport.ConnectAsync();

        Assert.True(transport.IsAvailable);
        Assert.Equal(1, interop.OpenCount);
        Assert.True(interop.LastChannel!.Connected);
    }

    [Fact]
    public async Task ConnectAsync_is_idempotent_and_opens_the_channel_once()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);

        await transport.ConnectAsync();
        await transport.ConnectAsync();

        Assert.Equal(1, interop.OpenCount);
    }

    [Fact]
    public async Task ConnectAsync_raises_Connected_once_on_a_fresh_connect()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);
        var count = 0;
        transport.Connected += (_, _) => count++;

        await transport.ConnectAsync();

        Assert.Equal(1, count); // the (re)connect signal the gesture re-push subscribes to
    }

    [Fact]
    public async Task ConnectAsync_does_not_raise_Connected_on_the_idempotent_reconnect()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);
        var count = 0;
        transport.Connected += (_, _) => count++;

        await transport.ConnectAsync();
        await transport.ConnectAsync(); // already open -> no fresh (re)connect

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ConnectAsync_does_not_raise_Connected_when_the_driver_is_absent()
    {
        var interop = new FakeAapDriverInterop { InterfacePath = null };
        using var transport = new DriverAapTransport(interop);
        var count = 0;
        transport.Connected += (_, _) => count++;

        await transport.ConnectAsync();

        Assert.Equal(0, count); // graceful no-op: nothing opened, nothing signalled
    }

    [Fact]
    public async Task SendAsync_writes_the_frame_bytes_to_the_channel()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);
        await transport.ConnectAsync();
        var frame = new byte[] { 0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0D, 0x02 };

        await transport.SendAsync(frame);

        var sent = Assert.Single(interop.LastChannel!.Sent);
        Assert.Equal(frame, sent);
    }

    [Fact]
    public async Task SendAsync_maps_a_driver_Win32Exception_to_IOException()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);
        await transport.ConnectAsync();
        // The real Win32AapDriverChannel throws Win32Exception on a failed send IOCTL (e.g.
        // the L2CAP session dropped mid-write). Core's gesture re-push write path only
        // tolerates IOException, so the adapter must translate at this OS boundary or the
        // Win32Exception escapes onto the WPF dispatcher and crashes the tray.
        interop.LastChannel!.SendFault = new Win32Exception(1167); // ERROR_DEVICE_NOT_CONNECTED

        var ex = await Assert.ThrowsAsync<IOException>(
            () => transport.SendAsync(new byte[] { 0x01 }));

        Assert.IsType<Win32Exception>(ex.InnerException);
    }

    [Fact]
    public async Task SendAsync_before_connecting_throws()
    {
        using var transport = new DriverAapTransport(new FakeAapDriverInterop());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendAsync(new byte[] { 0x01 }));
    }

    [Fact]
    public async Task Inbound_frame_raises_PacketReceived_with_the_same_bytes()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.PacketReceived += (_, frame) => received.TrySetResult(frame.ToArray());
        await transport.ConnectAsync();
        var echo = new byte[] { 0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0D, 0x03 };

        interop.LastChannel!.Emit(echo);

        var actual = await received.Task.WaitAsync(Timeout);
        Assert.Equal(echo, actual);
    }

    [Fact]
    public async Task DisconnectAsync_stops_the_receive_loop_and_closes_the_channel()
    {
        var interop = new FakeAapDriverInterop();
        using var transport = new DriverAapTransport(interop);
        await transport.ConnectAsync();
        var channel = interop.LastChannel!;

        await transport.DisconnectAsync();

        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task DisconnectAsync_without_a_connection_does_not_throw()
    {
        using var transport = new DriverAapTransport(new FakeAapDriverInterop());

        await transport.DisconnectAsync(); // no channel open
    }

    [Fact]
    public async Task ConnectAsync_after_Dispose_throws_ObjectDisposedException()
    {
        var transport = new DriverAapTransport(new FakeAapDriverInterop());
        transport.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ConnectAsync());
    }

    [Fact]
    public async Task Dispose_closes_the_channel_and_is_idempotent()
    {
        var interop = new FakeAapDriverInterop();
        var transport = new DriverAapTransport(interop);
        await transport.ConnectAsync();

        transport.Dispose();
        transport.Dispose(); // second call must be safe

        Assert.True(interop.LastChannel!.Disposed);
    }
}
