// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace StrideAssetStore.Core.Tests;

/// <summary>
/// Locates the sibling AssetContainer repository, used only for its JSON schemas and catalog during
/// validation. Tests no longer depend on any published asset repository — asset data is synthesized
/// on the fly (see <see cref="SyntheticWorkspace"/>). Tests that need the schemas skip when it's absent.
/// </summary>
internal static class TestPaths
{
    /// <summary>The directory that holds the sibling AssetContainer, or null.</summary>
    public static string? Workspace { get; } = FindWorkspace();

    public static string Container => Path.Combine(Workspace!, "AssetContainer");

    /// <summary>
    /// Whether the schemas are reachable. Skipping is a convenience for a developer who hasn't
    /// cloned the registry — never for CI, where a silent skip meant five test files reported green
    /// without executing anything. There, a missing checkout is a failure.
    /// </summary>
    public static bool Available =>
        Workspace is not null
        || (Environment.GetEnvironmentVariable("CI") is "true" or "True"
            ? throw new InvalidOperationException(
                "The sibling AssetContainer checkout is missing, so the schema and validation tests "
                + "would silently pass. Check it out next to this repository.")
            : false);

    private static string? FindWorkspace()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "AssetContainer", "schemas")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
