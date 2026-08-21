// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Core.Local.Catalog;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Local;

/// <summary>
/// Gets the catalog for the consumer-facing commands. It is downloaded once and kept on disk, so a
/// plane, a flaky network or a rate-limited raw.githubusercontent doesn't stop you from listing or
/// updating what is already installed.
/// </summary>
internal static class CatalogAccess
{
    private static string CacheFile => Path.Combine(AssetInstaller.AppRoot, "catalog.lock.json");

    /// <summary>
    /// Loads the catalog, preferring the network and falling back to the last snapshot. Returns the
    /// index and whether it came from the cache, so commands can say so rather than silently acting
    /// on stale data.
    /// </summary>
    public static async Task<(IndexLock Index, bool FromCache)> LoadAsync(
        string? indexUrl, bool offline, CancellationToken cancellation = default)
    {
        var cache = new FileCatalogCache(CacheFile);

        if (offline)
        {
            return (await cache.TryLoadAsync(cancellation)
                ?? throw new InvalidOperationException(
                    "No catalog snapshot on this machine yet, so --offline has nothing to read. Run once with network access."),
                true);
        }

        // Short timeout: the fallback below is better than a two-minute stall on a degraded host.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var url = new Uri(indexUrl ?? CatalogDefaults.IndexUrl);

        try
        {
            var index = await new HttpCatalogSource(http, url).LoadAsync(cancellation);
            await cache.SaveAsync(index, cancellation);
            return (index, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var cached = await cache.TryLoadAsync(cancellation)
                ?? throw new InvalidOperationException($"Couldn't fetch the catalog from {url} and no snapshot is cached: {ex.Message}");
            return (cached, true);
        }
    }

    /// <summary>The catalog keyed by id, which is what the installer takes.</summary>
    public static IReadOnlyDictionary<string, IndexedAsset> ById(IndexLock index) =>
        index.Assets.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds one asset by id. Ids are long, so an unambiguous case-insensitive suffix or substring
    /// is accepted too: <c>add grass</c> beats copying <c>com.nicogo.stride-gpu-grass</c> by hand.
    /// </summary>
    public static IndexedAsset Resolve(IndexLock index, string idOrFragment)
    {
        var exact = index.Assets.FirstOrDefault(a => a.Id.Equals(idOrFragment, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var matches = index.Assets
            .Where(a => a.Id.Contains(idOrFragment, StringComparison.OrdinalIgnoreCase)
                || a.Manifest.Name.Contains(idOrFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches switch
        {
            [var only] => only,
            [] => throw new InvalidOperationException($"No asset matches '{idOrFragment}'. Try: strideassetstore search {idOrFragment}"),
            _ => throw new InvalidOperationException(
                $"'{idOrFragment}' matches {matches.Count} assets ({string.Join(", ", matches.Take(4).Select(m => m.Id))}"
                + $"{(matches.Count > 4 ? ", …" : "")}). Use the full id."),
        };
    }
}
