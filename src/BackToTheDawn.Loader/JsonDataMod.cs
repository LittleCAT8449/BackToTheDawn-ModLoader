using BackToTheDawn.ModAPI;

namespace BackToTheDawn.Loader;

/// <summary>Runs the JSON-backed features selected by a data-only Mod manifest.</summary>
internal sealed class JsonDataMod : IMod
{
    private JsonPhoneMod? _phoneMod;
    private JsonShopMod? _shopMod;
    private JsonTaskMod? _taskMod;

    internal static ModManifest? LoadManifest(string path) => JsonPhoneMod.LoadManifest(path);

    public void Initialize(ModContext context)
    {
        if (context.Manifest.IsJsonPhoneMod)
        {
            _phoneMod = new JsonPhoneMod();
            _phoneMod.Initialize(context);
        }

        if (context.Manifest.IsJsonShopMod)
        {
            _shopMod = new JsonShopMod();
            _shopMod.Initialize(context);
        }

        _taskMod = new JsonTaskMod();
        _taskMod.Initialize(context, required: context.Manifest.IsJsonTaskMod);
    }

    public void Shutdown()
    {
        _shopMod?.Shutdown();
        _phoneMod?.Shutdown();
        _taskMod?.Shutdown();
        _shopMod = null;
        _phoneMod = null;
        _taskMod = null;
    }
}
