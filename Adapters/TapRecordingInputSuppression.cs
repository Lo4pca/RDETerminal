namespace RDETerminal.Adapters;

/// <summary>
/// Controls whether the game's RDInput path should be hidden from the game
/// while the interactive recorder is armed/recording.
/// </summary>
internal static class TapRecordingInputSuppression
{
    private static bool _recording;
    private static int _suppressThroughFrame = -1;

    internal static bool Active
        => _recording || UnityEngine.Time.frameCount <= _suppressThroughFrame;

    internal static void Begin()
    {
        _recording = true;
        _suppressThroughFrame = -1;
    }

    internal static void EndAfterCurrentFrame()
    {
        _recording = false;
        _suppressThroughFrame = UnityEngine.Time.frameCount + 1;
    }

    internal static void ForceEnd()
    {
        _recording = false;
        _suppressThroughFrame = -1;
    }
}