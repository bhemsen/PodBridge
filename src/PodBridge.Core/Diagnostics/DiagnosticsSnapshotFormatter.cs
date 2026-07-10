using System.Globalization;
using System.Text;

namespace PodBridge.Core.Diagnostics;

/// <summary>
/// Renders a <see cref="DiagnosticsSnapshot"/> as plain, human-readable text for the
/// exported file + clipboard copy (spec: "the file opens as readable text and shows
/// model/firmware-major/codec/tier/driver-presence + honest signing status + capability
/// matrix"). <paramref name="generatedAt"/> is taken as a parameter rather than read
/// internally so the formatter itself stays a pure, deterministic function of its inputs.
/// Uses <see cref="CultureInfo.InvariantCulture"/> throughout so the exported text is
/// stable regardless of the machine's locale.
/// </summary>
public static class DiagnosticsSnapshotFormatter
{
    /// <summary>Renders <paramref name="snapshot"/> to plain text.</summary>
    public static string Render(DiagnosticsSnapshot snapshot, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var ci = CultureInfo.InvariantCulture;
        var firmware = snapshot.FirmwareMajor?.ToString(ci) ?? "unknown (no host-requestable read exists today)";
        var text = new StringBuilder();
        text.AppendLine("PodBridge diagnostics");
        text.AppendLine(ci, $"Generated: {generatedAt:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine();
        text.AppendLine(ci, $"Model: {snapshot.ModelDisplayName} ({snapshot.Model})");
        text.AppendLine(ci, $"Firmware major: {firmware}");
        text.AppendLine(ci, $"Codec: {snapshot.Codec}");
        text.AppendLine(ci, $"Tier: {snapshot.Tier}");
        text.AppendLine(ci, $"Driver present: {snapshot.DriverPresent}");
        text.AppendLine(ci, $"Driver signing/test-mode status: {snapshot.DriverSigningStatus}");
        text.AppendLine();
        text.AppendLine("Capability matrix:");
        foreach (var entry in snapshot.Capabilities)
        {
            text.AppendLine(ci, $"  {entry.Feature}: {(entry.IsAvailable ? "on" : "off")} ({entry.Reason})");
        }

        text.AppendLine();
        text.AppendLine("Recent BLE parse results (address-masked):");
        if (snapshot.RecentBleParses.Count == 0)
        {
            text.AppendLine("  (none observed yet)");
        }

        foreach (var parse in snapshot.RecentBleParses)
        {
            var modelText = parse.Model?.ToString() ?? "n/a";
            text.AppendLine(
                ci, $"  {parse.MaskedAddress}: {(parse.ParsedSuccessfully ? "parsed" : "unparsed")} (model: {modelText})");
        }

        return text.ToString();
    }
}
