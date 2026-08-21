// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace StrideAssetStore.Core.Catalog;

/// <summary>
/// Where the aggregated catalog lives by default. Each host may override it from configuration
/// (see <c>Catalog:IndexUrl</c>), but they must agree when nothing is configured — the CLI, the
/// desktop app and the storefront are supposed to see the same assets.
/// </summary>
public static class CatalogDefaults
{
    /// <summary>The registry's generated <c>index.lock.json</c>, refreshed daily by its CI.</summary>
    public const string IndexUrl =
        "https://raw.githubusercontent.com/Nicogo1705/AssetContainer/main/index.lock.json";
}
