// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;

namespace StrideAssetStore.Core.Catalog;

/// <summary>Persists the last known index for offline use.</summary>
/// <remarks>
/// Abstracted so non-filesystem hosts (Blazor WASM via localStorage/IndexedDB) can supply their own
/// implementation; <see cref="FileCatalogCache"/> is used by desktop and the CLI.
/// </remarks>
public interface ICatalogCache
{
    Task<IndexLock?> TryLoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IndexLock index, CancellationToken cancellationToken = default);
}

/// <summary>Caches the index as a JSON file on disk.</summary>
public sealed class FileCatalogCache(string path) : ICatalogCache
{
    public async Task<IndexLock?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return StrideAssetStoreJson.Deserialize<IndexLock>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(IndexLock index, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, StrideAssetStoreJson.Serialize(index), cancellationToken);
    }
}
