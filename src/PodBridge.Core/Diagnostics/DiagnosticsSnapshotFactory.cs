using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Capabilities;
using PodBridge.Core.Protocol;

namespace PodBridge.Core.Diagnostics;

/// <summary>
/// Default <see cref="IDiagnosticsSnapshotFactory"/>: pulls the current facts from the
/// existing Core abstractions — no new OS read is added, this is the first real consumer
/// of the Phase-8 <see cref="ICapabilityProvider"/> (issue #53) plus the Phase-2/3/6
/// telemetry/audio/transport seams. Core stays OS-free: it depends only on interfaces,
/// which the composition root binds to their Windows adapters.
/// </summary>
public sealed class DiagnosticsSnapshotFactory : IDiagnosticsSnapshotFactory
{
    private readonly IDeviceStateProvider _stateProvider;
    private readonly IAudioStateReader _audioStateReader;
    private readonly IAapTransport _transport;
    private readonly IModelRegistry _modelRegistry;
    private readonly ICapabilityProvider _capabilityProvider;
    private readonly IBleParseHistory _parseHistory;

    public DiagnosticsSnapshotFactory(
        IDeviceStateProvider stateProvider,
        IAudioStateReader audioStateReader,
        IAapTransport transport,
        IModelRegistry modelRegistry,
        ICapabilityProvider capabilityProvider,
        IBleParseHistory parseHistory)
    {
        ArgumentNullException.ThrowIfNull(stateProvider);
        ArgumentNullException.ThrowIfNull(audioStateReader);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(modelRegistry);
        ArgumentNullException.ThrowIfNull(capabilityProvider);
        ArgumentNullException.ThrowIfNull(parseHistory);
        _stateProvider = stateProvider;
        _audioStateReader = audioStateReader;
        _transport = transport;
        _modelRegistry = modelRegistry;
        _capabilityProvider = capabilityProvider;
        _parseHistory = parseHistory;
    }

    /// <inheritdoc/>
    public DiagnosticsSnapshot Create()
    {
        var state = _stateProvider.Current;
        var modelInfo = _modelRegistry.Resolve(state.Model);
        var audio = _audioStateReader.Read();

        return DiagnosticsSnapshotBuilder.Build(
            modelInfo,
            // No host-requestable firmware-version read exists on the cleartext AAP channel
            // today (docs/research/firmware-capabilities.md) — always unreadable, honestly.
            firmwareMajor: null,
            audio.Codec,
            driverPresent: _transport.IsAvailable,
            _capabilityProvider,
            _parseHistory.Recent);
    }
}
