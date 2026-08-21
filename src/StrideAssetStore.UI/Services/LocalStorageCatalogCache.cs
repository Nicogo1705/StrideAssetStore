// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;
using Microsoft.JSInterop;

namespace StrideAssetStore.App.Services;

/// <summary>Browser-localStorage implementation of the catalog cache (offline fallback in WASM).</summary>
public sealed class LocalStorageCatalogCache(IJSRuntime js, string key = "assetstore.index") : ICatalogCache
{
    public async Task<IndexLock?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, key);
            return string.IsNullOrEmpty(json) ? null : StrideAssetStoreJson.Deserialize<IndexLock>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(IndexLock index, CancellationToken cancellationToken = default)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", cancellationToken, key, StrideAssetStoreJson.Serialize(index));
        }
        catch
        {
            // Best-effort cache; ignore storage failures (quota, private mode, etc.).
        }
    }
}
