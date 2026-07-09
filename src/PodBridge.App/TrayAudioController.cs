using System.Windows.Threading;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;

namespace PodBridge.App;

/// <summary>
/// Wires the read-only <see cref="IAudioStateReader"/> to the <see cref="TrayIcon"/>:
/// on a connect transition (and on the manual "Refresh audio status" action) it reads
/// the audio state, maps it through the device-independent
/// <see cref="AudioGuidanceEngine"/>, renders the codec and mic-mode lines, and raises
/// a Windows notification with the generic AAC guidance <b>only when SBC fallback is
/// positively confirmed</b> (<see cref="AudioGuidance.ShowAacAdvice"/>) — never on AAC
/// or the honest <c>Unknown</c> state. There is <b>no continuous polling</b>: reads
/// happen on connect and on demand only. UI updates are marshalled to the WPF
/// dispatcher (the monitor raises its event from a WinRT/thread-pool thread). Owns only
/// the event subscription; the monitor's and reader's lifetimes belong to the DI
/// container. Must be started on the UI thread.
/// </summary>
public sealed class TrayAudioController : IDisposable
{
    private const string AdviceTitle = "Audio quality: SBC";

    private readonly TrayIcon _tray;
    private readonly IAudioStateReader _reader;
    private readonly IConnectionMonitor _monitor;
    private readonly Dispatcher _dispatcher;

    private ConnectionStatus _lastStatus = ConnectionStatus.Unknown;

    private TrayAudioController(
        TrayIcon tray,
        IAudioStateReader reader,
        IConnectionMonitor monitor,
        Dispatcher dispatcher)
    {
        _tray = tray;
        _reader = reader;
        _monitor = monitor;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="reader"/> and
    /// <paramref name="monitor"/> to <paramref name="tray"/>. Call <see cref="Start"/>
    /// to wire the refresh action and render the current surface.
    /// </summary>
    public static TrayAudioController Create(
        TrayIcon tray,
        IAudioStateReader reader,
        IConnectionMonitor monitor,
        Dispatcher dispatcher)
        => new(tray, reader, monitor, dispatcher);

    /// <summary>
    /// Wires the "Refresh audio status" action, subscribes to connection changes, and
    /// renders the audio surface for the current status. Must be called on the UI
    /// thread, and before the monitor is started, so no initial connect is missed.
    /// </summary>
    public void Start()
    {
        _tray.SetRefreshAudioHandler(Refresh);
        _monitor.StatusChanged += OnStatusChanged;
        Apply(_monitor.CurrentStatus);
    }

    /// <summary>
    /// Re-reads the audio state on demand (the "Refresh audio status" menu action) and
    /// re-renders, raising the AAC guidance notification only on confirmed SBC.
    /// </summary>
    public void Refresh() => ReadAndRender();

    /// <summary>Unsubscribes so no late status change touches a disposed tray.</summary>
    public void Dispose() => _monitor.StatusChanged -= OnStatusChanged;

    private void OnStatusChanged(object? sender, ConnectionStatus status)
        => _dispatcher.InvokeAsync(() => Apply(status));

    private void Apply(ConnectionStatus status)
    {
        var justConnected = status == ConnectionStatus.Connected
            && _lastStatus != ConnectionStatus.Connected;
        _lastStatus = status;

        if (justConnected)
        {
            // The codec is negotiated at connect: read once on the transition. A
            // repeated Connected event leaves the current lines intact (no polling).
            ReadAndRender();
        }
        else if (status != ConnectionStatus.Connected)
        {
            // No connected device: show the neutral, honest undetermined surface and
            // never raise advice (nothing was positively determined).
            Render(AudioGuidanceEngine.ForState(AudioState.Unknown), notify: false);
        }
    }

    private void ReadAndRender()
        => Render(AudioGuidanceEngine.ForState(ReadSafely()), notify: true);

    private AudioState ReadSafely()
    {
        try
        {
            return _reader.Read();
        }
        catch (Exception)
        {
            // The reader contracts never to throw and to report Unknown for an
            // undeterminable value; this defensive fallback keeps the tray honest and
            // alive if an adapter ever misbehaves.
            return AudioState.Unknown;
        }
    }

    private void Render(AudioGuidance guidance, bool notify)
    {
        _tray.SetCodec(guidance.CodecLine);
        _tray.SetMicMode(guidance.MicModeLine);

        // Fire the guidance notification only on positively-confirmed SBC, so the
        // advice is always trustworthy (never on AAC or the honest Unknown state).
        if (notify && guidance.ShowAacAdvice && guidance.Advice is not null)
        {
            _tray.ShowNotification(AdviceTitle, guidance.Advice);
        }
    }
}
