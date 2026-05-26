using UnityEngine;

namespace RDETerminal.UI;

public static class TerminalBootstrap
{
    private static TerminalWindow _window;

    public static void Ensure()
    {
        if (_window != null)
        {
            return;
        }

        GameObject go = new("RDETerminal");
        Object.DontDestroyOnLoad(go);
        _window = go.AddComponent<TerminalWindow>();
    }
}