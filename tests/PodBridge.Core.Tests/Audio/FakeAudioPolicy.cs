using PodBridge.Core.Audio;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent <see cref="IAudioPolicy"/> for the Tier-1 test gate. Holds a
/// configurable endpoint list (including the AirPods-only degrade case), records every
/// <see cref="SetDefaultEndpoint"/> call, and tracks the resulting per-(direction,role)
/// default so a test can assert the exact endpoint-role assignments with no physical
/// audio device.
/// </summary>
internal sealed class FakeAudioPolicy : IAudioPolicy
{
    private readonly List<AudioEndpoint> _endpoints = [];
    private readonly Dictionary<(AudioEndpointDirection Direction, AudioRole Role), string> _defaults = new();
    private readonly List<AudioPolicySetCall> _setCalls = [];

    /// <summary>Every <see cref="SetDefaultEndpoint"/> call, in order.</summary>
    public IReadOnlyList<AudioPolicySetCall> SetCalls => _setCalls;

    /// <summary>Adds an endpoint (friendly name = id for readability) and returns it.</summary>
    public AudioEndpoint Add(string id, AudioEndpointDirection direction, bool isAirPods)
    {
        var endpoint = new AudioEndpoint(id, direction, isAirPods, id);
        _endpoints.Add(endpoint);
        return endpoint;
    }

    /// <summary>Removes the endpoint with <paramref name="id"/> (simulates unplug).</summary>
    public void Remove(string id) => _endpoints.RemoveAll(e => e.Id == id);

    /// <summary>
    /// Seeds the current default for <paramref name="role"/> <b>without</b> logging a
    /// <see cref="SetDefaultEndpoint"/> call, so a test can stage the pre-existing
    /// default-communications device the fallback picker prefers.
    /// </summary>
    public void SeedDefault(AudioEndpoint endpoint, AudioRole role)
        => _defaults[(endpoint.Direction, role)] = endpoint.Id;

    public IReadOnlyList<AudioEndpoint> GetEndpoints() => _endpoints;

    public string? GetDefaultEndpoint(AudioRole role, AudioEndpointDirection direction)
        => _defaults.TryGetValue((direction, role), out var id) ? id : null;

    public void SetDefaultEndpoint(string endpointId, AudioRole role)
    {
        var direction = _endpoints.Single(e => e.Id == endpointId).Direction;
        _defaults[(direction, role)] = endpointId;
        _setCalls.Add(new AudioPolicySetCall(endpointId, role, direction));
    }

    /// <summary>The endpoint id currently holding (<paramref name="direction"/>, <paramref name="role"/>), or null.</summary>
    public string? DefaultId(AudioEndpointDirection direction, AudioRole role)
        => GetDefaultEndpoint(role, direction);
}

/// <summary>One recorded <see cref="IAudioPolicy.SetDefaultEndpoint"/> invocation.</summary>
internal readonly record struct AudioPolicySetCall(
    string EndpointId, AudioRole Role, AudioEndpointDirection Direction);
