namespace BackToTheDawn.ModAPI;

/// <summary>
/// Entry point implemented by a custom-loader Mod.
/// </summary>
public interface IMod
{
    void Initialize(ModContext context);

    void Shutdown();
}

/// <summary>
/// Logger supplied through ModContext so Mods do not depend on BepInEx logging types.
/// </summary>
public interface IModLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message);
}

internal sealed class NullModLogger : IModLogger
{
    public static NullModLogger Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message)
    {
    }
}
