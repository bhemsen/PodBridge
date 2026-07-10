using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Persistence boundary for the user's press-and-hold <see cref="GestureConfiguration"/>.
/// Core stays OS-free: this is the interface only; the concrete store (a small per-user
/// file under <c>%LOCALAPPDATA%\PodBridge</c>, mirroring the mic-policy store) is a
/// <c>PodBridge.Windows</c> adapter. The configuration is persisted because Apple firmware
/// overwrites it on every reconnect, so the host must keep and re-push the user's choice
/// (docs/research/gesture-aap.md "reconnect-overwrite"; spec docs/specs/spec-gesture-remap.md).
/// </summary>
public interface IGestureConfigStore
{
    /// <summary>
    /// Returns the persisted gesture configuration, or <see langword="null"/> when the
    /// user has never assigned one (or the stored value is missing/unreadable). The
    /// re-push policy sends nothing when this is <see langword="null"/>, so an
    /// unconfigured device is never handed an unsolicited assignment.
    /// </summary>
    GestureConfiguration? Load();

    /// <summary>Persists <paramref name="configuration"/> as the user's current choice.</summary>
    void Save(GestureConfiguration configuration);
}
