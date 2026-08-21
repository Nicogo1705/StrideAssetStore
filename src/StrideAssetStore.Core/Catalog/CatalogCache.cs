// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Core.Catalog;

/// <summary>Persists the last known index for offline use.</summary>
/// <remarks>
/// Abstracted because the hosts have nothing in common here: the browser storefront caches through
/// localStorage/IndexedDB, while the desktop app and the CLI write a file (see
/// <c>FileCatalogCache</c> in StrideAssetStore.Core.Local, which is the only side allowed to touch
/// a filesystem).
/// </remarks>
public interface ICatalogCache
{
    Task<IndexLock?> TryLoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IndexLock index, CancellationToken cancellationToken = default);
}
