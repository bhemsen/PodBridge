using System.Runtime.InteropServices;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows implementation of <see cref="IAudioPolicy"/> — enumerates render and
/// capture endpoints and sets the default endpoint <b>per role</b> (default vs
/// default-communications). Tier 1: driver-free, admin-free (<c>asInvoker</c>);
/// it changes endpoint role assignments but never installs a driver or elevates.
/// </summary>
/// <remarks>
/// <para><b>Enumeration + AirPods tagging.</b> Endpoints are enumerated with the
/// reused read-only <see cref="IMMDeviceEnumerator"/> interop (#23) rather than a
/// new NAudio dependency — NAudio can only enumerate, not set defaults, so it would
/// add a package without removing the need for the <see cref="IPolicyConfig"/>
/// P/Invoke below. Each endpoint's <see cref="AudioEndpoint.IsAirPods"/> flag is set
/// by the same container-id technique as <c>WindowsAudioStateReader</c>: locate the
/// AirPods' <c>PKEY_Device_ContainerId</c> via the friendly-name heuristic, then tag
/// every endpoint sharing that container id. When the container id is unavailable it
/// falls back to the endpoint friendly name (the lower-confidence path). This maps an
/// <i>audio endpoint</i> to the AirPods — a distinct mapping from the Phase 1–2
/// Bluetooth-device identification (research #25).</para>
/// <para><b>A2DP-vs-HFP render disambiguation.</b> Real AirPods can expose two
/// <c>IsAirPods</c> render endpoints at once, sharing one container id: the stereo
/// A2DP endpoint (friendly name containing "Headphones") and the mono Hands-Free
/// (HFP) endpoint Windows surfaces for calls (friendly name containing "Hands-Free"
/// or "Headset"). <see cref="AudioEndpoint.IsHandsFreeRender"/> tags the latter by
/// friendly-name match so <c>MicPolicyEngine</c> can deterministically prefer the
/// A2DP endpoint for the media role (#114).</para>
/// <para><b>Setting defaults.</b> Uses the undocumented <see cref="IPolicyConfig"/>
/// <c>SetDefaultEndpoint</c> (slot 11) per <see cref="ERole"/>; NAudio and the public
/// Core Audio APIs cannot set a default endpoint. All COM/P-Invoke is isolated here
/// and in <c>PolicyConfigInterop.cs</c>; Core sees only <see cref="IAudioPolicy"/>.
/// Any COM error (activation, QueryInterface, a non-S_OK <c>HRESULT</c>) degrades to
/// a no-op / empty result rather than crashing (constitution: graceful degradation),
/// and is verified against real hardware at the QA gate.</para>
/// </remarks>
public sealed class WindowsAudioPolicy : IAudioPolicy
{
    /// <inheritdoc />
    public IReadOnlyList<AudioEndpoint> GetEndpoints()
    {
        object? comObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            comObject = comType is null ? null : Activator.CreateInstance(comType);
            if (comObject is not IMMDeviceEnumerator enumerator)
            {
                return Array.Empty<AudioEndpoint>();
            }

            // Discover the AirPods container id once (name heuristic → container id),
            // then tag every endpoint sharing it across both directions.
            var airPodsContainer = FindAirPodsContainerId(enumerator);
            var endpoints = new List<AudioEndpoint>();
            CollectEndpoints(enumerator, EDataFlow.Render, AudioEndpointDirection.Render, airPodsContainer, endpoints);
            CollectEndpoints(enumerator, EDataFlow.Capture, AudioEndpointDirection.Capture, airPodsContainer, endpoints);
            return endpoints;
        }
        catch (Exception)
        {
            // Any COM / enumeration failure degrades to an empty list, never a crash.
            return Array.Empty<AudioEndpoint>();
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    /// <inheritdoc />
    public string? GetDefaultEndpoint(AudioRole role, AudioEndpointDirection direction)
    {
        object? comObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            comObject = comType is null ? null : Activator.CreateInstance(comType);
            if (comObject is not IMMDeviceEnumeratorWithDefault enumerator)
            {
                return null;
            }

            enumerator.GetDefaultAudioEndpoint(ToDataFlow(direction), ToERole(role), out var device);
            try
            {
                return GetEndpointId(device);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception)
        {
            // No default device for the role (E_NOTFOUND) or any COM error → null.
            return null;
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    /// <inheritdoc />
    public void SetDefaultEndpoint(string endpointId, AudioRole role)
    {
        object? comObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(PolicyConfigInterop.PolicyConfigClsid);
            comObject = comType is null ? null : Activator.CreateInstance(comType);
            if (comObject is not IPolicyConfig policyConfig)
            {
                return;
            }

            // [PreserveSig] HRESULT — a non-zero result is a soft failure. IAudioPolicy
            // has no return channel, so a failed set is swallowed here (never thrown):
            // the undocumented interface must degrade honestly, not crash the tray app.
            _ = policyConfig.SetDefaultEndpoint(endpointId, ToERole(role));
        }
        catch (Exception)
        {
            // CoCreateInstance / QueryInterface failure → graceful no-op.
        }
        finally
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }

    // Locate the AirPods' container id: scan render endpoints first (preferred), then
    // capture, returning the container id of the first friendly-name match (#23 technique).
    private static Guid? FindAirPodsContainerId(IMMDeviceEnumerator enumerator)
    {
        return ScanForAirPodsContainerId(enumerator, EDataFlow.Render)
            ?? ScanForAirPodsContainerId(enumerator, EDataFlow.Capture);
    }

    private static Guid? ScanForAirPodsContainerId(IMMDeviceEnumerator enumerator, EDataFlow flow)
    {
        enumerator.EnumAudioEndpoints(flow, NativeMethods.DeviceStateActive, out var collection);
        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                try
                {
                    var container = TryGetAirPodsContainerId(device);
                    if (container is not null)
                    {
                        return container;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return null;
    }

    // Returns the device's container id only when its friendly name names AirPods/Beats.
    private static Guid? TryGetAirPodsContainerId(IMMDevice device)
    {
        device.OpenPropertyStore(NativeMethods.StgmRead, out var store);
        try
        {
            var name = NativeMethods.GetStringProperty(store, PropertyKeys.DeviceFriendlyName);
            return AirPodsNameHeuristic.IsMatch(name)
                ? NativeMethods.GetGuidProperty(store, PropertyKeys.DeviceContainerId)
                : null;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void CollectEndpoints(
        IMMDeviceEnumerator enumerator,
        EDataFlow flow,
        AudioEndpointDirection direction,
        Guid? airPodsContainer,
        List<AudioEndpoint> sink)
    {
        enumerator.EnumAudioEndpoints(flow, NativeMethods.DeviceStateActive, out var collection);
        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                try
                {
                    var endpoint = BuildEndpoint(device, direction, airPodsContainer);
                    if (endpoint is not null)
                    {
                        sink.Add(endpoint);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    private static AudioEndpoint? BuildEndpoint(
        IMMDevice device,
        AudioEndpointDirection direction,
        Guid? airPodsContainer)
    {
        var id = GetEndpointId(device);
        if (id is null)
        {
            return null;
        }

        device.OpenPropertyStore(NativeMethods.StgmRead, out var store);
        try
        {
            var name = NativeMethods.GetStringProperty(store, PropertyKeys.DeviceFriendlyName);
            var container = NativeMethods.GetGuidProperty(store, PropertyKeys.DeviceContainerId);
            var isAirPods = IsAirPodsEndpoint(container, name, airPodsContainer);
            var isHandsFreeRender = direction == AudioEndpointDirection.Render
                && isAirPods
                && IsHandsFreeRenderName(name);
            return new AudioEndpoint(id, direction, isAirPods, name, isHandsFreeRender);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    // Container-id match to the discovered AirPods container is primary; the endpoint
    // friendly name is the fallback when a container id is unavailable (research #25).
    private static bool IsAirPodsEndpoint(Guid? container, string? name, Guid? airPodsContainer)
    {
        if (airPodsContainer is not null && container is not null)
        {
            return container == airPodsContainer;
        }

        return AirPodsNameHeuristic.IsMatch(name);
    }

    // Windows names the Hands-Free (HFP, mono) render endpoint distinctly from the A2DP
    // (stereo) one — e.g. "Hands-Free AG Audio (AirPods Pro)" or "Headset (AirPods
    // Pro)" versus the A2DP endpoint's "Headphones (AirPods Pro)". Only the friendly
    // name distinguishes them; both share PKEY_Device_ContainerId (#114).
    private static readonly string[] HandsFreeNeedles = ["Hands-Free", "Hands Free", "Headset"];

    private static bool IsHandsFreeRenderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var needle in HandsFreeNeedles)
        {
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Reads the endpoint id string (IMMDevice::GetId) via the GetId-carrying interface;
    // the id is what SetDefaultEndpoint consumes. Degrades to null on any COM error.
    private static string? GetEndpointId(IMMDevice device)
    {
        try
        {
            ((IMMDeviceWithId)device).GetId(out var id);
            return id;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ERole ToERole(AudioRole role) => role switch
    {
        AudioRole.Console => ERole.Console,
        AudioRole.Multimedia => ERole.Multimedia,
        AudioRole.Communications => ERole.Communications,
        _ => ERole.Console,
    };

    private static EDataFlow ToDataFlow(AudioEndpointDirection direction) => direction switch
    {
        AudioEndpointDirection.Render => EDataFlow.Render,
        AudioEndpointDirection.Capture => EDataFlow.Capture,
        _ => EDataFlow.Render,
    };
}
