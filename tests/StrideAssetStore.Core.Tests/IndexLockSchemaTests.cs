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

        // Every optional field carries a value: the serializer omits nulls, so a sample that leaves
        // them unset validates whatever the schema says — which is how `forks` reached the published
        // index while the schema still forbade it, with this test green throughout.
        var index = Index(
            Asset("com.test.a", "A", "Scripts", tags: ["x"], certified: true) with { Stars = 3, Forks = 1 },
            Asset("com.test.b", "B", "Shaders") with { Stars = 0, Forks = 0 });

        var json = StrideAssetStoreJson.Serialize(index);
        var report = new ValidationReport();
        SchemaValidator
            .FromFile(Path.Combine(TestPaths.Container, "schemas", "index-lock.schema.json"))
            .Validate(json, report, "index-lock");

        Assert.False(report.HasErrors, string.Join(" | ", report.Messages));
    }
}
