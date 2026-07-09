# PodBridge

An open-source AirPods companion for **Windows** — battery, automatic play/pause,
audio-quality guidance, and a smart microphone-profile policy, **driver-free by
default**. Advanced features (noise-control switching, gesture remap) are an
optional, clearly-labelled add-on.

> Status: early inception. See [`docs/`](docs/) for the vision, architecture, and roadmap.

## Scope & honesty

PodBridge brings the AirPods experience as close to Apple-native as Windows
technically allows. It does **not** claim Apple-identical sound, and it cannot
provide simultaneous high-quality stereo output *and* AirPods-microphone input —
that is a Bluetooth-Classic platform limit, not a bug (see [`docs/vision.md`](docs/vision.md)).

## Building

Requires the .NET 10 SDK.

    dotnet restore PodBridge.slnx
    powershell -NoProfile -File build/verify.ps1

## License

Apache-2.0. See [`LICENSE`](LICENSE).

## Disclaimer

Not affiliated with, authorized, or endorsed by Apple Inc. "AirPods" and "Apple"
are trademarks of Apple Inc., used here only descriptively.
