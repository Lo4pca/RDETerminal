using HarmonyLib;
using RDETerminal.Adapters;

namespace RDETerminal.Patches;

/// <summary>
/// Prevents recording taps from becoming gameplay input. The recorder itself
/// uses UnityEngine.Input directly, so these patches do not hide the tap/stop
/// keys from the recorder.
/// </summary>
[HarmonyPatch(typeof(RDInput), nameof(RDInput.CheckForStateInKey))]
internal static class Patch_RDInput_CheckForStateInKey
{
    private static bool Prefix(ref bool __result)
    {
        if (!TapRecordingInputSuppression.Active)
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(RDInput), nameof(RDInput.Update))]
internal static class Patch_RDInput_Update
{
    private static void Postfix()
    {
        if (!TapRecordingInputSuppression.Active)
        {
            return;
        }

        // RDInput.Update caches these values for scnGame.Update(). Clear all
        // cached actions so the recording input cannot start, hit, pause,
        // navigate, or otherwise affect gameplay/editor transport state.
        RDInput.p1Press = false;
        RDInput.p1IsPressed = false;
        RDInput.p1Release = false;
        RDInput.p1HoldTime = 0f;

        RDInput.p2Press = false;
        RDInput.p2IsPressed = false;
        RDInput.p2Release = false;
        RDInput.p2HoldTime = 0f;

        RDInput.anyPlayerPress = false;
        RDInput.anyPlayerIsPressed = false;
        RDInput.anyPlayerRelease = false;

        RDInput.skipPressed = false;
        RDInput.restartPressed = false;
        RDInput.cancelPress = false;
        RDInput.cancelIsPressed = false;
        RDInput.cancelRelease = false;
        RDInput.quitPressed = false;

        RDInput.leftPress = false;
        RDInput.leftIsPressed = false;
        RDInput.leftRelease = false;
        RDInput.rightPress = false;
        RDInput.rightIsPressed = false;
        RDInput.rightRelease = false;
        RDInput.upPress = false;
        RDInput.upIsPressed = false;
        RDInput.upRelease = false;
        RDInput.downPress = false;
        RDInput.downIsPressed = false;
        RDInput.downRelease = false;
        RDInput.finerControlPress = false;
        RDInput.finerControlIsPressed = false;
        RDInput.finerControlRelease = false;
        RDInput.faceLeftPress = false;
        RDInput.faceLeftIsPressed = false;
        RDInput.faceLeftRelease = false;
        RDInput.faceUpPress = false;
        RDInput.faceUpIsPressed = false;
        RDInput.faceUpRelease = false;
        RDInput.selectPress = false;
        RDInput.selectIsPressed = false;
        RDInput.selectRelease = false;

        RDInput.xAxis = 0f;
        RDInput.yAxis = 0f;
    }
}