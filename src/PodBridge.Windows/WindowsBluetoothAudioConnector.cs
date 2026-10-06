using System.Runtime.InteropServices;
using PodBridge.Core.Bluetooth;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows implementation of <see cref="IBluetoothAudioConnector"/> — connects or
/// disconnects already-paired AirPods the way the Windows Sound control panel's
/// "Connect" / "Disconnect" does. Tier 1: driver-free, admin-free (<c>asInvoker</c>); it
/// never pairs, unpairs or installs anything.
/// </summary>
/// <remarks>
/// <para><b>Finding the AirPods.</b> Audio endpoints of installed devices (active or
/// unplugged, render and capture) are matched by friendly name with
/// <see cref="AirPodsNameHeuristic.IsAirPodsName"/> — AirPods only, so a paired Beats
/// device is never connected or disconnected by an "AirPods" action. An endpoint is
/// <c>DEVICE_STATE_ACTIVE</c> while the Bluetooth link is up and
/// <c>DEVICE_STATE_UNPLUGGED</c> while a paired device is disconnected, which is how
/// <see cref="GetLinkState"/> reports the link. An endpoint that fails mid-enumeration
/// (e.g. it is removed meanwhile) is skipped rather than failing the whole result.</para>
/// <para><b>The request.</b> For every matched endpoint the device topology's connectors
/// are followed (<c>IConnector::GetDeviceIdConnectedTo</c>) to the
/// Bluetooth audio driver's filter (id prefix <c>{2}.\\?\bth</c>: the A2DP stereo and the
/// hands-free filter). Each distinct filter receives the public one-shot
/// <c>KSPROPERTY_ONESHOT_RECONNECT</c> / <c>KSPROPERTY_ONESHOT_DISCONNECT</c> request
/// (<c>KSPROPSETID_BtAudio</c>, ksmedia.h) via <c>IKsControl</c> — both profiles, like
/// Windows does. The driver accepting it only means the request was taken; the caller
/// (<see cref="BluetoothAudioLinkController"/>) confirms via the link state.</para>
/// <para><b>Several paired AirPods.</b> Every matched device receives the request; the
/// one in range connects. Every COM failure degrades to <see cref="BluetoothAudioLinkState.NotFound"/>
/// / <see cref="BluetoothAudioRequestResult.Rejected"/> — this adapter never throws.</para>
/// </remarks>
public sealed class WindowsBluetoothAudioConnector : IBluetoothAudioConnector
{
    /// <inheritdoc />
    public BluetoothAudioLinkState GetLinkState()
        => WithEnumerator(ReadLinkState, BluetoothAudioLinkState.NotFound);

    /// <inheritdoc />
    public BluetoothAudioRequestResult SendRequest(BluetoothAudioRequest request)
        => WithEnumerator(enumerator => Send(enumerator, request), BluetoothAudioRequestResult.Rejected);

    private static T WithEnumerator<T>(Func<IMMDeviceEnumeratorForActivation, T> action, T fallback)
    {
        object? comObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            comObject = comType is null ? null : Activator.CreateInstance(comType);
            return comObject is IMMDeviceEnumeratorForActivation enumerator ? action(enumerator) : fallback;
        }
        catch (Exception)
        {
            // Any COM / enumeration failure degrades to the fallback (constitution:
            // graceful degradation) — the connector never throws out.
            return fallback;
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    private static BluetoothAudioLinkState ReadLinkState(IMMDeviceEnumeratorForActivation enumerator)
    {
        var state = BluetoothAudioLinkState.NotFound;
        ForEachAirPodsEndpoint(enumerator, device =>
        {
            ((IMMDeviceWithState)device).GetState(out var endpointState);
            if (endpointState == NativeMethods.DeviceStateActive)
            {
                state = BluetoothAudioLinkState.Connected;
            }
            else if (state == BluetoothAudioLinkState.NotFound)
            {
                state = BluetoothAudioLinkState.Disconnected;
            }
        });
        return state;
    }

    private static BluetoothAudioRequestResult Send(
        IMMDeviceEnumeratorForActivation enumerator, BluetoothAudioRequest request)
    {
        var endpoints = 0;
        var filterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ForEachAirPodsEndpoint(enumerator, device =>
        {
            endpoints++;
            CollectBluetoothFilterIds(device, filterIds);
        });

        if (endpoints == 0)
        {
            return BluetoothAudioRequestResult.NotFound;
        }

        if (filterIds.Count == 0)
        {
            return BluetoothAudioRequestResult.Unsupported;
        }

        var accepted = false;
        foreach (var filterId in filterIds)
        {
            // Non-short-circuiting: every filter (A2DP and hands-free) gets the request.
            accepted |= TrySendToFilter(enumerator, filterId, request);
        }

        return accepted ? BluetoothAudioRequestResult.Accepted : BluetoothAudioRequestResult.Rejected;
    }

    private static void ForEachAirPodsEndpoint(
        IMMDeviceEnumeratorForActivation enumerator, Action<IMMDevice> visit)
    {
        enumerator.EnumAudioEndpoints(EDataFlow.All, BluetoothAudioInterop.PairedEndpointStateMask, out var collection);
        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                TryVisitEndpoint(collection, i, visit);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    // One endpoint failing (e.g. removed during enumeration, or its property store / state
    // unreadable) must not degrade the whole result into NotFound / Rejected — and with it
    // a confirmation poll into a false timeout — so it is skipped and the loop continues.
    private static void TryVisitEndpoint(IMMDeviceCollection collection, uint index, Action<IMMDevice> visit)
    {
        IMMDevice? device = null;
        try
        {
            collection.Item(index, out device);
            if (IsAirPodsEndpoint(device))
            {
                visit(device);
            }
        }
        catch (Exception)
        {
            // Skip this endpoint; the others are still read.
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }
        }
    }

    private static bool IsAirPodsEndpoint(IMMDevice device)
    {
        device.OpenPropertyStore(NativeMethods.StgmRead, out var store);
        try
        {
            return AirPodsNameHeuristic.IsAirPodsName(
                NativeMethods.GetStringProperty(store, PropertyKeys.DeviceFriendlyName));
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    // Walks the endpoint's topology to the driver filters it is connected to and keeps
    // the Bluetooth audio ones. An endpoint whose topology cannot be read is skipped.
    private static void CollectBluetoothFilterIds(IMMDevice device, HashSet<string> filterIds)
    {
        object? raw = null;
        try
        {
            var iid = typeof(IDeviceTopology).GUID;
            device.Activate(ref iid, NativeMethods.ClsCtxAll, IntPtr.Zero, out raw);
            var topology = (IDeviceTopology)raw;
            topology.GetConnectorCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                var filterId = TryGetConnectedFilterId(topology, i);
                if (filterId is not null
                    && filterId.StartsWith(BluetoothAudioInterop.BluetoothFilterIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    filterIds.Add(filterId);
                }
            }
        }
        catch (Exception)
        {
            // Skip this endpoint; the others may still reach the driver.
        }
        finally
        {
            if (raw is not null)
            {
                Marshal.ReleaseComObject(raw);
            }
        }
    }

    private static string? TryGetConnectedFilterId(IDeviceTopology topology, uint index)
    {
        topology.GetConnector(index, out var connector);
        try
        {
            // An unconnected connector fails (E_NOTFOUND): nothing behind it.
            return connector.GetDeviceIdConnectedTo(out var filterId) < 0 ? null : filterId;
        }
        finally
        {
            Marshal.ReleaseComObject(connector);
        }
    }

    // Sends the one-shot request to one filter. True when the driver accepted it (which
    // does not yet mean the link changed — the caller confirms via the link state).
    private static bool TrySendToFilter(
        IMMDeviceEnumeratorForActivation enumerator, string filterId, BluetoothAudioRequest request)
    {
        IMMDevice? filter = null;
        object? raw = null;
        try
        {
            enumerator.GetDevice(filterId, out filter);
            var iid = typeof(IKsControl).GUID;
            filter.Activate(ref iid, NativeMethods.ClsCtxAll, IntPtr.Zero, out raw);
            var property = new KsPropertyHeader
            {
                Set = BluetoothAudioInterop.KsPropSetIdBtAudio,
                Id = request == BluetoothAudioRequest.Connect
                    ? BluetoothAudioInterop.KsPropertyOneShotReconnect
                    : BluetoothAudioInterop.KsPropertyOneShotDisconnect,
                Flags = BluetoothAudioInterop.KsPropertyTypeGet,
            };
            var hr = ((IKsControl)raw).KsProperty(
                ref property, (uint)Marshal.SizeOf<KsPropertyHeader>(), IntPtr.Zero, 0, out _);
            return hr >= 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            ReleaseAll(raw, filter);
        }
    }

    private static void ReleaseAll(params object?[] comObjects)
    {
        foreach (var comObject in comObjects)
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }
}
