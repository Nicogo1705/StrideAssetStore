// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using StrideAssetStore.Core.Serialization;
using StrideAssetStore.Core.Local.Validation;
using static StrideAssetStore.Core.Tests.CatalogTestData;

namespace StrideAssetStore.Core.Tests;

/// <summary>Guards against index model ↔ index-lock.schema.json drift: a serialized index must validate.</summary>
public sealed class IndexLockSchemaTests
{
    [Fact]
    public void Serialized_index_validates_against_the_schema()
    {
        if (!TestPaths.Available)
        {
            return;
        }

        var index = Index(
            Asset("com.test.a", "A", "Scripts", tags: ["x"], certified: true),
            Asset("com.test.b", "B", "Shaders"));

        var json = StrideAssetStoreJson.Serialize(index);
        var report = new ValidationReport();
        SchemaValidator
            .FromFile(Path.Combine(TestPaths.Container, "schemas", "index-lock.schema.json"))
            .Validate(json, report, "index-lock");

        Assert.False(report.HasErrors, string.Join(" | ", report.Messages));
    }
}
