// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;

namespace StrideAssetStore.Core.Local.Catalog;

/// <summary>Reads the index from a local file.</summary>
public sealed class FileCatalogSource(string path) : ICatalogSource
{
    public async Task<IndexLock> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return StrideAssetStoreJson.Deserialize<IndexLock>(json);
    }
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
