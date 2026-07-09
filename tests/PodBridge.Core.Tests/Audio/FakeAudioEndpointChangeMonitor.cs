using PodBridge.Core.Audio;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent <see cref="IAudioEndpointChangeMonitor"/> that lets a test raise a
/// device-topology change on demand, driving the engine's <see cref="MicPolicyEngine.Refresh"/>
/// with no physical device (constitution Tier-1 test gate).
/// </summary>
internal sealed class FakeAudioEndpointChangeMonitor : IAudioEndpointChangeMonitor
{
    public event EventHandler? EndpointsChanged;

    public bool IsStarted { get; private set; }

    public void Start() => IsStarted = true;

    public void Stop() => IsStarted = false;

    /// <summary>Simulates a device-topology change (endpoint added/removed/default changed).</summary>
    public void RaiseEndpointsChanged() => EndpointsChanged?.Invoke(this, EventArgs.Empty);
}
