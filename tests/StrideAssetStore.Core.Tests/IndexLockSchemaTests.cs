// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;
using StrideAssetStore.Core.Local.Validation;
using static StrideAssetStore.Core.Tests.CatalogTestData;

namespace StrideAssetStore.Core.Tests;

/// <summary>Guards against index model ↔ index-lock.schema.json drift: a serialized index must validate.</summary>
public sealed class IndexLockSchemaTests
{
    private static SchemaValidator Schema() =>
        SchemaValidator.FromFile(Path.Combine(TestPaths.Container, "schemas", "index-lock.schema.json"));

    /// <summary>
    /// The published index, exactly as the registry serves it. A synthesized sample only exercises
    /// the fields it happens to set — <c>forks</c> reached the published index while the schema still
    /// forbade it, with this test green throughout — whereas the real file carries whatever the
    /// indexer actually writes, item schemas included.
    /// </summary>
    [Fact]
    public void The_published_index_validates_against_the_schema()
    {
        if (!TestPaths.Available)
        {
            return;
        }

        var indexPath = Path.Combine(TestPaths.Container, "index.lock.json");
        Assert.True(File.Exists(indexPath), $"No index.lock.json in {TestPaths.Container} — the schema guard has nothing to check.");

        var report = new ValidationReport();
        Schema().Validate(File.ReadAllText(indexPath), report, "index-lock");

        Assert.False(report.HasErrors, string.Join(" | ", report.Messages));
    }

    /// <summary>
    /// And every property the model can write is checked by one of the two documents above — the
    /// published index carries whatever the indexer emits today, the sample has to carry the rest.
    /// Without this, a field no asset happens to use (a new one, or `deprecated`) is validated by
    /// nothing and can contradict the schema unnoticed. That is how `forks` shipped.
    /// </summary>
    [Fact]
    public void Every_indexed_asset_property_is_covered_by_one_of_them()
    {
        if (!TestPaths.Available)
        {
            return;
        }

        var published = File.ReadAllText(Path.Combine(TestPaths.Container, "index.lock.json"));
        var sample = StrideAssetStoreJson.Serialize(Sample());

        var missing = typeof(IndexedAsset).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => StrideAssetStoreJson.Write.PropertyNamingPolicy!.ConvertName(p.Name))
            .Where(name => !published.Contains($"\"{name}\":", StringComparison.Ordinal)
                && !sample.Contains($"\"{name}\":", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            $"Validated by neither the published index nor the sample: {string.Join(", ", missing)}. "
            + "Add it to the sample below so the schema is actually checked against it.");
    }

    /// <summary>A round-trip of the model itself, so the guard still says something with no registry checkout.</summary>
    [Fact]
    public void Serialized_index_validates_against_the_schema()
    {
        if (!TestPaths.Available)
        {
            return;
        }

        var report = new ValidationReport();
        Schema().Validate(StrideAssetStoreJson.Serialize(Sample()), report, "index-lock");

        Assert.False(report.HasErrors, string.Join(" | ", report.Messages));
    }

    /// <summary>Carries the optional fields the published index doesn't happen to use.</summary>
    private static IndexLock Sample() => Index(
        Asset("com.test.a", "A", "Scripts", tags: ["x"], certified: true) with
        {
            Stars = 3,
            Forks = 1,
            Deprecated = new DeprecationInfo { Reason = "Superseded.", Successor = "com.test.b" },
        },
        Asset("com.test.b", "B", "Shaders") with { Stars = 0, Forks = 0 });
}
