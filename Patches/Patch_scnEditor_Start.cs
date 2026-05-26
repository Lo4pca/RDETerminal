using HarmonyLib;
using RDLevelEditor;
using RDETerminal.UI;

[HarmonyPatch(typeof(scnEditor), nameof(scnEditor.Start))]
public class Patch_scnEditor_Start
{
    static void Postfix()
    {
        TerminalBootstrap.Ensure();
    }
}