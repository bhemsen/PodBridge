namespace PodBridge.Core.Models;

/// <summary>
/// The user's press-and-hold gesture assignment, held <b>per bud</b>: the action the
/// right bud performs and the action the left bud performs. The press-and-hold is the
/// <b>only</b> remappable AirPods stem gesture (control-command identifier <c>0x16</c>,
/// per-bud) — single, double, and triple presses are fixed by Apple — so this record is
/// the whole gesture model (docs/research/gesture-aap.md "Gesture-remap command"). Only
/// <see cref="GestureAction"/> values are representable, so an unsupported action can
/// never be stored (spec: "no invented actions"). Wire order is right-then-left
/// (<c>data1</c> = right, <c>data2</c> = left); see
/// <c>AapProtocol.BuildSetPressAndHoldGesture</c>.
/// </summary>
/// <param name="RightBud">The action assigned to the right bud (wire <c>data1</c>).</param>
/// <param name="LeftBud">The action assigned to the left bud (wire <c>data2</c>).</param>
public sealed record GestureConfiguration(GestureAction RightBud, GestureAction LeftBud)
{
    /// <summary>
    /// A configuration assigning the same <paramref name="action"/> to both buds — the
    /// shared fallback for models that do not advertise independent per-bud assignment
    /// (docs/research/gesture-aap.md "Shared (non-per-bud) fallback").
    /// </summary>
    public static GestureConfiguration Shared(GestureAction action) => new(action, action);

    /// <summary>
    /// <see langword="true"/> when both buds carry the same action (a shared assignment
    /// rather than an independent per-bud one).
    /// </summary>
    public bool IsShared => RightBud == LeftBud;
}
