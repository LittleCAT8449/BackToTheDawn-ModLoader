using BackToTheDawn.ModAPI;

namespace BackToTheDawn.DependencyMod;

public sealed class DependencyModEntry : IMod
{
    public void Initialize(ModContext context)
    {
        context.Logger.Info(
            "Dependency test Mod initialized after dev.backtothedawn.examplemod.");
    }

    public void Shutdown()
    {
    }
}
