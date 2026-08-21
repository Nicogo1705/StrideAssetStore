// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;

namespace StrideAssetStore.Core.Catalog;

/// <summary>Loads the aggregated index (<c>index.lock.json</c>) from somewhere.</summary>
public interface ICatalogSource
{
    Task<IndexLock> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Downloads the index over HTTP (e.g. a raw GitHub URL). Works in Blazor WASM.</summary>
public sealed class HttpCatalogSource(HttpClient client, Uri url) : ICatalogSource
{
    public async Task<IndexLock> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await client.GetStringAsync(url, cancellationToken);
        return StrideAssetStoreJson.Deserialize<IndexLock>(json);
    }
}
