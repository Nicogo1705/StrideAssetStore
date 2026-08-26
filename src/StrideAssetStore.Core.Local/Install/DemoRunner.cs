// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Local.Git;
using StrideAssetStore.Core.Local.Shell;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Core.Local.Install;

/// <summary>What happened to a demo: where it is, and why it isn't running if it isn't.</summary>
/// <param name="Success">Whether the demo was started.</param>
/// <param name="Messages">Progress, in the order it happened.</param>
/// <param name="ProjectPath">The demo project, once the clone has it.</param>
public sealed record DemoResult(bool Success, IReadOnlyList<string> Messages, string? ProjectPath = null);

/// <summary>
/// Builds and runs an asset's demo from the shared cache.
/// </summary>
/// <remarks>
/// An install materialises only <c>AssetData/</c> — the demo lives outside it and is deliberately
/// not part of what every project downloads. Running one therefore starts by widening the existing
/// clone rather than fetching a second copy: same folder, same commit, one <c>git checkout</c>
/// away. The build is the slow part (Stride's packages, then the demo's own content), which is why
/// callers get progress rather than a spinner.
/// </remarks>
public sealed class DemoRunner(GitClient? git = null)
{
    private readonly GitClient _git = git ?? new GitClient();

    /// <summary>Where the demo of a cached clone lives, whether or not it has been materialised.</summary>
    public static string ProjectPath(string cloneRoot, IndexedAsset asset) =>
        Path.Combine(cloneRoot, (asset.Latest.DemoProject ?? "Demo/Demo.csproj").Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Makes the demo present on disk. The clone exists but is sparse, so this is a checkout, not
    /// a download — nothing goes over the network.
    /// </summary>
    public DemoResult Materialize(string cloneRoot, IndexedAsset asset)
    {
        var messages = new List<string>();
        var project = ProjectPath(cloneRoot, asset);
        if (File.Exists(project))
        {
            return new DemoResult(true, messages, project);
        }

        if (!Directory.Exists(cloneRoot))
        {
            return new DemoResult(false, [$"✗ {asset.Manifest.Name} is not downloaded yet."]);
        }

        messages.Add("• Unpacking the demo from the clone…");
        _git.DisableSparseCheckout(cloneRoot);

        return File.Exists(project)
            ? new DemoResult(true, messages, project)
            : new DemoResult(false,
                [.. messages, $"✗ {asset.Manifest.Name} has no demo at {asset.Latest.DemoProject ?? "Demo/Demo.csproj"}."]);
    }

    /// <summary>
    /// Builds the demo. Slow on a cold NuGet cache — Stride is a large dependency and the demo's
    /// own assets are compiled at build time — so <paramref name="progress"/> gets the raw output.
    /// </summary>
    public async Task<DemoResult> BuildAsync(
        string projectPath, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        progress?.Report("• Building the demo (first run downloads Stride — this takes a while)…");

        var result = await ProcessRunner.RunAsync(
            "dotnet", ["build", "-c", "Release", "--nologo"], directory,
            timeout: TimeSpan.FromMinutes(20), cancellation: cancellation);

        if (result.Ok)
        {
            return new DemoResult(true, ["✓ Demo built."], projectPath);
        }

        // The compiler's own words: a demo that fails to build is the author's problem to fix, and
        // "the build failed" without them is a bug report nobody can act on.
        var lines = (result.StdOut + result.StdErr)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains(": error", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(5)
            .ToList();

        return new DemoResult(false, ["✗ The demo failed to build.", .. lines], projectPath);
    }

    /// <summary>
    /// Starts the built demo, detached: it is a windowed game the user closes when they are done,
    /// not a command whose exit code anyone waits for.
    /// </summary>
    public DemoResult Start(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
            };
            foreach (var argument in (string[])["run", "-c", "Release", "--no-build"])
            {
                info.ArgumentList.Add(argument);
            }

            return System.Diagnostics.Process.Start(info) is not null
                ? new DemoResult(true, ["✓ The demo is starting — its window opens in a moment."], projectPath)
                : new DemoResult(false, ["✗ The demo could not be started."], projectPath);
        }
        catch (Exception ex)
        {
            return new DemoResult(false, [$"✗ The demo could not be started: {ex.Message}"], projectPath);
        }
    }
}
