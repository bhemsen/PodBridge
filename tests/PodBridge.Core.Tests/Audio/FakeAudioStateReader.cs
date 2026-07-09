using PodBridge.Core.Audio;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent <see cref="IAudioStateReader"/> whose returned
/// <see cref="AudioState"/> a test sets directly, so the pure guidance/display mapping
/// can be exercised for every enum state with no physical AirPods (constitution Tier-1
/// test gate).
/// </summary>
internal sealed class FakeAudioStateReader : IAudioStateReader
{
    private AudioState _state = AudioState.Unknown;

    /// <summary>Stages the state the next <see cref="Read"/> returns.</summary>
    public void Set(CodecKind codec, MicMode mic) => _state = new AudioState(codec, mic);

    public AudioState Read() => _state;
}
