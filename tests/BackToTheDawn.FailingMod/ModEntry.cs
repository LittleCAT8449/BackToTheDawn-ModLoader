using BackToTheDawn.ModAPI;

namespace BackToTheDawn.FailingMod;

public sealed class FailingModEntry : IMod
{
    public void Initialize(ModContext context)
    {
        context.Logger.Info("Failing lifecycle test Mod is about to throw intentionally.");
        throw new InvalidOperationException("Intentional lifecycle test failure.");
    }

    public void Shutdown()
    {
    }
}
