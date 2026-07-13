using PodBridge.Core.Audio;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent <see cref="ICommsProfileEngager"/> that records every
/// <see cref="Engage"/> / <see cref="Release"/> call so a test can assert the engine
/// forces the AirPods HFP link up on a comms promotion and releases it on demotion —
/// with no physical audio stream (constitution Tier-1 test gate). Mirrors the real
/// adapter's idempotence contract so tests can assert it too.
/// </summary>
internal sealed class FakeCommsProfileEngager : ICommsProfileEngager
{
    private readonly List<string> _engageCalls = [];
    private int _releaseCalls;
    private string? _engagedId;

    /// <summary>Every <see cref="Engage"/> argument, in order.</summary>
    public IReadOnlyList<string> EngageCalls => _engageCalls;

    /// <summary>The number of <see cref="Release"/> calls.</summary>
    public int ReleaseCalls => _releaseCalls;

    /// <summary>The currently-engaged render endpoint id, or null when released.</summary>
    public string? EngagedId => _engagedId;

    public void Engage(string renderEndpointId)
    {
        // Idempotent: re-engaging the already-engaged id records nothing (matches adapter).
        if (_engagedId == renderEndpointId)
        {
            return;
        }

        _engageCalls.Add(renderEndpointId);
        _engagedId = renderEndpointId;
    }

    public void Release()
    {
        // A no-op when nothing is engaged, so a redundant release records nothing.
        if (_engagedId is null)
        {
            return;
        }

        _releaseCalls++;
        _engagedId = null;
    }
}
