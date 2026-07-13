using System.Runtime.InteropServices;

namespace PodBridge.Windows.Interop;

// COM interop for the WASAPI render keep-alive that forces the AirPods HFP link up,
// isolated to the WindowsCommsProfileEngager adapter (#156). It opens a RENDER
// (playback) stream ONLY, tagged AudioCategory_Communications via
// IAudioClient2::SetClientProperties BEFORE Initialize — the documented way to make
// Windows engage the Hands-Free profile so the AirPods capture endpoint comes live.
// It NEVER opens a capture stream. Every IID / vtable order here is from the public
// Core Audio (WASAPI) headers (Audioclient.h). Reuses EDataFlow / IMMDevice /
// IMMDeviceCollection / ERole from CoreAudioInterop.cs and PolicyConfigInterop.cs.

/// <summary>Well-known WASAPI GUIDs and constants for the render keep-alive path.</summary>
internal static class AudioClientInterop
{
    // AUDCLNT_SHAREMODE_SHARED — share the endpoint with the system mixer (no exclusive).
    internal const int ShareModeShared = 0;

    // AUDCLNT_BUFFERFLAGS_SILENT — tells the render client to emit silence, so the primed
    // buffer needs no sample data (we only want the stream open, not audible output).
    internal const uint BufferFlagsSilent = 0x2;

    // AUDIO_STREAM_CATEGORY.AudioCategory_Communications (Audiosessiontypes.h): the category
    // that makes Windows route the stream to the communications path and engage HFP/SCO.
    internal const int AudioCategoryCommunications = 3;

    // A 1-second shared-mode buffer (REFERENCE_TIME, 100-ns units). Ample for a silent
    // keep-alive; the actual size is read back via GetBufferSize before priming.
    internal const long BufferDuration = 10_000_000;

    // IID_IAudioClient2 and IID_IAudioRenderClient (Audioclient.h).
    internal static readonly Guid AudioClient2Iid = new("726778CD-F60A-4eda-82DE-E47610CD78AA");
    internal static readonly Guid AudioRenderClientIid = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
}

/// <summary>
/// <c>AudioClientProperties</c> (Audioclient.h) passed to
/// <see cref="IAudioClient2.SetClientProperties"/>. Setting
/// <see cref="ECategory"/> = <see cref="AudioClientInterop.AudioCategoryCommunications"/>
/// before Initialize is what triggers the HFP link.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProperties
{
    public uint CbSize;
    public int BIsOffload;   // BOOL
    public int ECategory;    // AUDIO_STREAM_CATEGORY
    public int Options;      // AUDCLNT_STREAMOPTIONS (0 = none)
}

/// <summary>
/// The <c>IMMDeviceEnumerator</c> surface extended with <c>GetDevice</c> (vtable slot 3),
/// used to obtain the specific render <see cref="IMMDevice"/> by endpoint id. Same IID as
/// the read-only enumerator in CoreAudioInterop.cs; slots 1–2 mirror the real signatures
/// purely to place <c>GetDevice</c> at slot 3.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumeratorForActivation
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
}

/// <summary>
/// <c>IAudioClient2</c> (Audioclient.h). Declares the full vtable — the 12 base
/// <c>IAudioClient</c> methods (slots 0–11) then the three <c>IAudioClient2</c> additions
/// (slots 12–14) — so <see cref="SetClientProperties"/> resolves at slot 13. All methods
/// are <c>[PreserveSig]</c> so a non-S_OK HRESULT degrades gracefully instead of throwing
/// (constitution: never crash the tray). Only the methods the keep-alive path calls carry
/// full signatures; the rest are declared to preserve vtable order.
/// </summary>
[ComImport]
[Guid("726778CD-F60A-4eda-82DE-E47610CD78AA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient2
{
    [PreserveSig]
    int Initialize(
        int shareMode,
        uint streamFlags,
        long hnsBufferDuration,
        long hnsPeriodicity,
        IntPtr pFormat,
        IntPtr audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint numBufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long phnsLatency);

    [PreserveSig]
    int GetCurrentPadding(out uint numPaddingFrames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr ppDeviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

    // Slot 12: never called; declared to keep SetClientProperties at slot 13.
    [PreserveSig]
    int IsOffloadCapable(int category, out int pbOffloadCapable);

    // Slot 13: the category set that triggers HFP. Called BEFORE Initialize.
    [PreserveSig]
    int SetClientProperties(ref AudioClientProperties pProperties);

    // Slot 14: never called; declared to keep the vtable complete.
    [PreserveSig]
    int GetBufferSizeLimits(IntPtr pFormat, bool eventDriven, out long minDuration, out long maxDuration);
}

/// <summary>
/// <c>IAudioRenderClient</c> (Audioclient.h) — obtained via
/// <see cref="IAudioClient2.GetService"/> to prime the shared render buffer with a single
/// SILENT block before Start, so the stream is valid but emits no audible sound.
/// </summary>
[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig]
    int GetBuffer(uint numFramesRequested, out IntPtr ppData);

    [PreserveSig]
    int ReleaseBuffer(uint numFramesWritten, uint dwFlags);
}
