namespace RDETerminal.Adapters;

public sealed class BridgeServices
{
    public static BridgeServices Shared { get; } = new BridgeServices();

    public ReflectionGameEventBridge EventBridge { get; }

    private BridgeServices()
    {
        EventBridge = new ReflectionGameEventBridge();
    }
}
