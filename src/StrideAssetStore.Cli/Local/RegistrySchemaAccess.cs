// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Local.Validation;

namespace StrideAssetStore.Cli.Local;

/// <summary>
/// Gets the registry's manifest schema and catalog so `check` can judge a manifest by the same
/// rules the registry's CI will.
/// </summary>
/// <remarks>
/// They are fetched from the registry repository rather than shipped with the tool: a copy compiled
/// into the CLI drifts the moment the schema changes, and drift here is worse than no check at all
/// - it tells the author their manifest is fine right up to the pull request that rejects it.
/// Cached on disk after the first run, so being offline costs the schema check, not the command.
/// </remarks>
internal static class RegistrySchemaAccess
{
    private static string CacheRoot => Path.Combine(AssetInstaller.AppRoot, "registry-rules");

    /// <summary>What the manifest is judged against, or null when it could not be obtained.</summary>
    internal sealed record Rules(SchemaValidator ManifestSchema, Catalog Catalog, bool FromCache);

    /// <summary>
    /// Loads the rules from a local AssetContainer checkout when given one, otherwise from the
    /// registry repository, otherwise from the last copy on this machine.
    /// </summary>
    /// <param name="containerPath">An AssetContainer checkout to read instead of the network.</param>
    /// <param name="registry">Registry repository as <c>owner/repo</c>.</param>
    /// <param name="branch">Branch to read the rules from.</param>
    /// <param name="failure">Why there are no rules, when the result is null.</param>
    public static Rules? Load(string? containerPath, string registry, string branch, out string? failure)
    {
        failure = null;

        if (!string.IsNullOrWhiteSpace(containerPath))
        {
            var root = Path.GetFullPath(containerPath);
            var schema = Path.Combine(root, "schemas", "manifest.schema.json");
            var catalog = Path.Combine(root, "catalog");

            if (!File.Exists(schema) || !Directory.Exists(catalog))
            {
                failure = $"{root} does not look like an AssetContainer checkout (no schemas/manifest.schema.json or catalog/).";
                return null;
            }

            return new Rules(SchemaValidator.FromFile(schema), Catalog.Load(catalog), FromCache: false);
        }

        var cache = Path.Combine(CacheRoot, registry.Replace('/', '_'), branch);
        var files = new[]
        {
            ("schemas/manifest.schema.json", Path.Combine(cache, "schemas", "manifest.schema.json")),
            ("catalog/categories.json", Path.Combine(cache, "catalog", "categories.json")),
            ("catalog/licenses.json", Path.Combine(cache, "catalog", "licenses.json")),
        };

        var downloaded = TryDownload(registry, branch, files, out var downloadError);
        if (!downloaded && !files.All(f => File.Exists(f.Item2)))
        {
            failure = downloadError;
            return null;
        }

        return new Rules(
            SchemaValidator.FromFile(files[0].Item2),
            Catalog.Load(Path.Combine(cache, "catalog")),
            FromCache: !downloaded);
    }

    private static bool TryDownload(
        string registry, string branch, (string Remote, string Local)[] files, out string? error)
    {
        error = null;

        // Short timeout: a degraded host should cost the schema check, not a two-minute stall.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        try
        {
            // Downloaded to memory first: a half-written cache is worse than none, because the next
            // run would read it happily and validate against a truncated schema.
            var contents = new string[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                var url = $"https://raw.githubusercontent.com/{registry}/{branch}/{files[i].Remote}";
                contents[i] = http.GetStringAsync(url).GetAwaiter().GetResult();
            }

            for (var i = 0; i < files.Length; i++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(files[i].Local)!);
                File.WriteAllText(files[i].Local, contents[i]);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"could not read the rules from {registry}@{branch}: {ex.Message}";
            return false;
        }
    }
}
