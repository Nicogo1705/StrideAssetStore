// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.App.Services;

/// <summary>Holds the loaded catalog and notifies components when it changes.</summary>
public sealed class CatalogState(CatalogLoader loader)
{
    public AssetCatalog? Catalog { get; private set; }

    public bool Loading { get; private set; }

    public bool FromCache { get; private set; }

    public string? Error { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Seed for the "Discover" shuffle. Held here, not in the page: the page component is destroyed
    /// on every navigation, so a field there re-seeded each time the user came back from an asset —
    /// the opposite of the stable order it was meant to give.
    /// </summary>
    public int ShuffleSeed { get; private set; } = Random.Shared.Next();

    /// <summary>Draws a new "Discover" order (the 🎲 button).</summary>
    public void Reshuffle() => ShuffleSeed = Random.Shared.Next();

    /// <summary>Loads the catalog once. Safe to call from multiple components.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (Catalog is not null || Loading)
        {
            return;
        }

        Loading = true;
        Error = null;
        Notify();

        try
        {
            var result = await loader.LoadAsync();
            Catalog = result.Catalog;
            FromCache = result.FromCache;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
            Notify();
        }
    }

    public IndexedAsset? Find(string id) =>
        Catalog?.Assets.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    private void Notify() => Changed?.Invoke();
}
