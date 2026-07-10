using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent stand-in for the gesture-config store (the real one is the
/// file-backed <c>GestureConfigStore</c> in <c>PodBridge.Windows</c>). Holds the value in
/// memory so the re-push policy can be exercised with no file system: a <see langword="null"/>
/// value models "the user has never assigned a gesture".
/// </summary>
internal sealed class FakeGestureConfigStore : IGestureConfigStore
{
    private GestureConfiguration? _configuration;

    public FakeGestureConfigStore(GestureConfiguration? configuration = null)
        => _configuration = configuration;

    /// <summary>Number of <see cref="Load"/> calls — proves the policy re-reads on each re-push.</summary>
    public int LoadCount { get; private set; }

    public GestureConfiguration? Load()
    {
        LoadCount++;
        return _configuration;
    }

    public void Save(GestureConfiguration configuration) => _configuration = configuration;
}
