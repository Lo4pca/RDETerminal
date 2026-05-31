namespace RDETerminal.Adapters;

/// <summary>
/// Retained for potential external use or future extension.
/// Internal plugin code no longer uses this class — all dependencies
/// are wired explicitly through <see cref="UI.TerminalBootstrap"/>.
/// </summary>
public sealed class BridgeServices
{
    public static BridgeServices Shared { get; } = new BridgeServices();

    public ReflectionGameEventBridge EventBridge { get; }

    private BridgeServices()
    {
        EventBridge = new ReflectionGameEventBridge();
    }
}