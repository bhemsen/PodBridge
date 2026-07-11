using PodBridge.Core.Audio;
using Xunit;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for the Phase-8 hardening of
/// <see cref="MicPolicyEngine"/>: the engine snapshots the user's prior endpoint-role
/// routing before its first apply and restores it both on an explicit
/// <see cref="MicPolicyEngine.Restore"/> (graceful shutdown) and automatically when an
/// endpoint-set fails mid-apply — so a failure/crash never leaves audio rerouted or
/// half-applied (spec docs/specs/spec-model-coverage-hardening.md, hardening pass).
/// </summary>
public class MicPolicyEngineRestoreTests
{
    private const string ApRender = "ap-render";
    private const string ApCapture = "ap-capture";
    private const string SpkRender = "spk-render";
    private const string MicCapture = "mic-capture";

    [Fact]
    public void Restore_ReturnsAllRolesToThePriorRouting()
    {
        var policy = SeededTwoDevicePolicy();
        using var engine = NewEngine(policy);

        // The initial HiFi-lock apply moved the media render role onto the AirPods.
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Console));
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));

        engine.Restore();

        // Prior routing (the seeded speaker/mic) is restored across every mutated role.
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Console));
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    [Fact]
    public void ApplyFailure_RollsBackToThePriorRouting()
    {
        var policy = SeededTwoDevicePolicy();
        using var engine = NewEngine(policy);
        engine.SetMode(MicPolicyMode.CallMode);

        // Inject a failure on the comms-render set that the Call-mode promotion is about to
        // perform. Apply is idempotent — the already-satisfied media render role is skipped —
        // so the failure must ride a role that genuinely changes (comms → AirPods).
        policy.FailOnceOnEndpointId = ApRender;

        engine.SetCallModeActive(true); // apply throws part-way → engine rolls back to prior routing

        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Console));
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    [Fact]
    public void PersistentApplyFailure_RollbackConverges_IssuesNoFurtherSetsOnRetry()
    {
        var policy = SeededTwoDevicePolicy();
        using var engine = NewEngine(policy);
        engine.SetMode(MicPolicyMode.CallMode);

        // A PERSISTENT (non-transient) failure — unlike FailOnceOnEndpointId, this never
        // clears, so every apply attempt that tries to move a role onto the AirPods keeps
        // failing. This is the scenario the idempotent rollback path guards: an
        // unconditional rollback would keep re-issuing every baseline set on each retry
        // (including roles a failed attempt never actually touched), ping-ponging
        // apply<->rollback and re-initialising the A2DP render stream indefinitely.
        policy.FailAlwaysOnEndpointId = ApRender;

        engine.SetCallModeActive(true); // apply throws part-way -> rolls back to prior routing

        // The media role genuinely changed (HiFi-lock had moved it to the AirPods), so
        // the rollback legitimately restores it.
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Console));
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));
        // The comms roles were never actually moved off the fallback — the failing set
        // never committed — so they were already at the baseline.
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));

        var callsAfterFirstRollback = policy.SetCalls.Count;

        // Simulate the OS re-notifying (OnDefaultDeviceChanged) repeatedly while the
        // failure persists — every Refresh re-enters ApplyLocked, retries, fails, and
        // rolls back again.
        engine.Refresh();
        engine.Refresh();

        // Once converged, the idempotent rollback issues ZERO further sets on retry: the
        // rolled-back state already matches the baseline, so there is nothing left to
        // change. An unconditional rollback would keep reissuing the same already-
        // satisfied sets on every retry (this assertion fails without the #114 fix).
        Assert.Equal(callsAfterFirstRollback, policy.SetCalls.Count);
    }

    [Fact]
    public void Restore_SkipsRemovedEndpoints_NeverThrows()
    {
        var policy = SeededTwoDevicePolicy();
        using var engine = NewEngine(policy);

        // The prior devices are gone at shutdown (AirPods disconnected, dock unplugged).
        policy.Remove(SpkRender);
        policy.Remove(MicCapture);

        engine.Restore(); // must be a no-op for the vanished endpoints, never a throw

        Assert.DoesNotContain(policy.SetCalls, c => c.EndpointId == SpkRender && c.Role == AudioRole.Console);
    }

    [Fact]
    public void NoPriorDefaults_Restore_IsANoOp()
    {
        // No seeded defaults ⇒ empty baseline ⇒ Restore changes nothing (and never throws).
        var policy = new FakeAudioPolicy();
        policy.Add(ApRender, AudioEndpointDirection.Render, isAirPods: true);
        policy.Add(ApCapture, AudioEndpointDirection.Capture, isAirPods: true);
        policy.Add(SpkRender, AudioEndpointDirection.Render, isAirPods: false);
        policy.Add(MicCapture, AudioEndpointDirection.Capture, isAirPods: false);
        using var engine = NewEngine(policy);

        var callsBefore = policy.SetCalls.Count;
        engine.Restore();

        Assert.Equal(callsBefore, policy.SetCalls.Count);
    }

    private static MicPolicyEngine NewEngine(FakeAudioPolicy policy)
        => new(policy, new FakeAudioSessionMonitor(), new FakeAudioEndpointChangeMonitor());

    // AirPods + a non-AirPods fallback, with the speaker/mic seeded as the user's PRIOR
    // defaults for every role the engine mutates (captured as the restore baseline).
    private static FakeAudioPolicy SeededTwoDevicePolicy()
    {
        var policy = new FakeAudioPolicy();
        policy.Add(ApRender, AudioEndpointDirection.Render, isAirPods: true);
        policy.Add(ApCapture, AudioEndpointDirection.Capture, isAirPods: true);
        var spk = policy.Add(SpkRender, AudioEndpointDirection.Render, isAirPods: false);
        var mic = policy.Add(MicCapture, AudioEndpointDirection.Capture, isAirPods: false);
        policy.SeedDefault(spk, AudioRole.Console);
        policy.SeedDefault(spk, AudioRole.Multimedia);
        policy.SeedDefault(spk, AudioRole.Communications);
        policy.SeedDefault(mic, AudioRole.Communications);
        return policy;
    }
}
