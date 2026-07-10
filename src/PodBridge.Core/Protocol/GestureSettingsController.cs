using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Device-independent decision + apply logic behind the press-and-hold gesture-remap
/// settings surface. Keeps the WPF settings window thin (mirroring how
/// <see cref="NoiseControlController"/> backs the tray noise-control submenu): it decides
/// whether the feature is offerable for the connected model (the honest action set to expose
/// comes from the static <see cref="GestureSupport"/> gate, queried directly by the UI) and
/// persists + writes the user's assignment.
/// <list type="bullet">
/// <item><see cref="GetAvailability"/> resolves the three honest states — driver absent
/// (Tier-1 default), model unsupported, or available — so the UI degrades gracefully and is
/// never silently broken (constitution).</item>
/// <item><see cref="ApplyAsync"/> persists the assignment via <see cref="IGestureConfigStore"/>
/// and writes it over the transport by delegating to <see cref="GestureRepushController"/>,
/// so the immediate apply and the re-push-on-reconnect share one write+echo-confirm path.
/// The choice is stored before the write, so even a non-fatal "couldn't apply" is re-applied
/// on the next Tier-2 (re)connect (Apple firmware forgets it — docs/research/gesture-aap.md
/// "reconnect-overwrite").</item>
/// <item>When the transport is unavailable (driver absent) nothing is stored or sent
/// (graceful degradation); the UI never reaches this path because it gates on
/// <see cref="GetAvailability"/> first.</item>
/// </list>
/// Core stays OS-free: this holds only Core abstractions; the concrete store and transport
/// are Windows adapters bound at the composition root
/// (spec docs/specs/spec-gesture-remap.md).
/// </summary>
public sealed class GestureSettingsController
{
    private readonly IAapTransport _transport;
    private readonly IGestureConfigStore _store;
    private readonly GestureRepushController _repush;

    /// <summary>
    /// Wires the controller to the Tier-2 transport, the persistence store, and the
    /// re-push policy it delegates the actual write to.
    /// </summary>
    public GestureSettingsController(
        IAapTransport transport,
        IGestureConfigStore store,
        GestureRepushController repush)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(repush);
        _transport = transport;
        _store = store;
        _repush = repush;
    }

    /// <summary>
    /// Resolves whether the gesture surface can be offered for <paramref name="model"/>:
    /// <see cref="GestureAvailability.DriverUnavailable"/> when the Tier-2 driver is absent
    /// (checked first, so the driver-free default always degrades regardless of model),
    /// <see cref="GestureAvailability.ModelUnsupported"/> when the connected model has no
    /// remappable press-and-hold, otherwise <see cref="GestureAvailability.Available"/>.
    /// </summary>
    public GestureAvailability GetAvailability(AirPodsModel model)
    {
        if (!_transport.IsAvailable)
        {
            return GestureAvailability.DriverUnavailable;
        }

        return GestureSupport.SupportsPressAndHold(model)
            ? GestureAvailability.Available
            : GestureAvailability.ModelUnsupported;
    }

    /// <summary>The persisted assignment, or <see langword="null"/> if the user has never set one.</summary>
    public GestureConfiguration? Current => _store.Load();

    /// <summary>
    /// Persists <paramref name="configuration"/> and writes it to the AirPods, returning the
    /// write outcome (<see cref="GestureRepushOutcome.Confirmed"/> on the device echo,
    /// <see cref="GestureRepushOutcome.CouldNotApply"/> on a non-fatal miss — the stored
    /// choice still re-applies on the next reconnect). Stores nothing and returns
    /// <see cref="GestureRepushOutcome.Unavailable"/> when the transport is absent
    /// (graceful degradation).
    /// </summary>
    public async Task<GestureRepushOutcome> ApplyAsync(
        GestureConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!_transport.IsAvailable)
        {
            return GestureRepushOutcome.Unavailable; // graceful degrade: persist nothing, send nothing
        }

        // Persist before the write so a "couldn't apply" is still re-pushed on the next
        // (re)connect; RepushAsync reloads the store, so it writes exactly what we saved.
        _store.Save(configuration);
        return await _repush.RepushAsync(cancellationToken).ConfigureAwait(false);
    }
}
