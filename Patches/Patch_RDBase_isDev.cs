using HarmonyLib;
[HarmonyPatch(typeof(RDBase), "isDev", MethodType.Getter)]
public static class Patch_RDBase_isDev
{
    static bool Prefix(ref bool __result)
    {
        if (!DevModeController.OverrideEnabled)
        {
            return true;
        }
        __result = DevModeController.OverrideValue;
        return false;
    }
}