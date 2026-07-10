using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Resolves an identified <see cref="AirPodsModel"/> to its per-model shape info,
/// degrading any model outside the vision's six supported models to the labelled
/// generic "Unknown AirPods" fallback — never throws (constitution: graceful
/// degradation). An interface (mirroring the other OS-free Core lookups) so the
/// Phase-8 capability provider (issue #53) can take it as a dependency.
/// </summary>
public interface IModelRegistry
{
    /// <summary>
    /// Resolves <paramref name="model"/> to its <see cref="AirPodsModelInfo"/> shape,
    /// or the generic "Unknown AirPods" fallback when it is not one of the vision's
    /// six supported models.
    /// </summary>
    AirPodsModelInfo Resolve(AirPodsModel model);
}
