using System;

namespace RDETerminal.Adapters;

/// <summary>
/// Reads the editor's live playback clock in nominal elapsed seconds.
/// Differences between samples are independent of tempo changes because the
/// underlying clock is the conductor's visual/audio DSP position.
/// </summary>
public sealed class EditorPositionSource
{
    public double GetPlaybackTime()
    {
        scrConductor conductor = scrConductor.instance ?? throw new InvalidOperationException(
                "scrConductor.instance is null. Playback time is unavailable.");
        return conductor.visualPos;
    }
}