using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RDETerminal;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private static new ManualLogSource Logger;
    public static void LogWarn(object message)
    {
        Logger.LogWarning(message);
    }

    public static void LogError(object message)
    {
        Logger.LogError(message);
    }

    public static void LogInfo(object message)
    {
        Logger.LogInfo(message);
    }
    void Awake()
    {
        Logger = base.Logger;
        Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
    }
}