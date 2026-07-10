using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Default <see cref="IModelRegistry"/>, backed by the clean-room
/// <see cref="AppleModelIdentifier"/> per-model shape mapper.
/// </summary>
public sealed class ModelRegistry : IModelRegistry
{
    /// <inheritdoc/>
    public AirPodsModelInfo Resolve(AirPodsModel model) => AppleModelIdentifier.Resolve(model);
}
