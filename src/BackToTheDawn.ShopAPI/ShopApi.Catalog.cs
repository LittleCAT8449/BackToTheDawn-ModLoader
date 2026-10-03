using BackToTheDawn.ModAPI;

namespace BackToTheDawn.ShopAPI;

/// <summary>Read-only shop and goods catalog queries available alongside registration.</summary>
public sealed partial class ShopApi
{
    public IReadOnlyList<ShopDescriptor> Catalog => ShopCatalog.All;

    public bool IsAvailable => ShopCatalog.IsAvailable;

    public bool TryGet(ShopKey key, out ShopDescriptor descriptor) =>
        ShopCatalog.TryGet(key, out descriptor);

    public bool TryGet(string key, out ShopDescriptor descriptor) =>
        ShopCatalog.TryGet(key, out descriptor);

    public bool TryGetByNativeId(int nativeShopId, out ShopDescriptor descriptor) =>
        ShopCatalog.TryGetByNativeId(nativeShopId, out descriptor);

    public bool IsGoodsAvailable => ShopGoodsCatalog.IsAvailable;

    public IReadOnlyList<ShopGoodsDefinition> Goods => ShopGoodsCatalog.All;

    public IReadOnlyList<ShopGoodsDefinition> GetGoods(ShopKey key) =>
        ShopGoodsCatalog.GetGoods(key);

    public bool TryGetGoods(
        ShopKey shopKey,
        ItemKey itemKey,
        out ShopGoodsDefinition goods) =>
        ShopGoodsCatalog.TryGet(shopKey, itemKey, out goods);
}
