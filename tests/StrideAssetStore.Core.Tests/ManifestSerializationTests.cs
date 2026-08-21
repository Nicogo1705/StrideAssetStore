// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;

namespace StrideAssetStore.Core.Tests;

public sealed class ManifestSerializationTests
{
    [Fact]
    public void Parses_nuget_and_import_mode()
    {
        var manifest = StrideAssetStoreJson.Deserialize<AssetManifest>("""
            {
              "schemaVersion": 1,
              "id": "com.test.widget",
              "name": "Widget",
              "authors": [{ "name": "Tester" }],
              "description": "A test widget.",
              "category": "Scripts",
              "license": "MIT",
              "defaultImport": "nuget",
              "nuget": { "packageId": "Tester.Widget", "packageVersion": "1.0.0" }
            }
            """);

        Assert.Equal("nuget", manifest.DefaultImport);
        Assert.NotNull(manifest.Nuget);
        Assert.Equal("Tester.Widget", manifest.Nuget!.PackageId);
        Assert.Equal("1.0.0", manifest.Nuget.PackageVersion);
    }

    [Fact]
    public void Nuget_is_absent_by_default()
    {
        var manifest = StrideAssetStoreJson.Deserialize<AssetManifest>("""
            {
              "schemaVersion": 1,
              "id": "com.test.widget",
              "name": "Widget",
              "authors": [{ "name": "Tester" }],
              "description": "A test widget.",
              "category": "Scripts",
              "license": "MIT"
            }
            """);

        Assert.Null(manifest.Nuget);
        Assert.Null(manifest.DefaultImport);
    }
}
