// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.App.Services;
using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Desktop.Services;

/// <summary>
/// Desktop implementation of <see cref="IDemoRunner"/>: the same download, unpack, build and start
/// the CLI's <c>demo</c> command runs, through the shared <see cref="DemoRunner"/>.
/// </summary>
/// <remarks>
/// The work happens off the render thread. Cloning and building take minutes on a cold cache, and
/// a Blazor circuit that spends them inside a click handler is a page that stops answering — the
/// progress this reports would never reach the browser it is meant for.
/// </remarks>
public sealed class DesktopDemoRunner(AssetInstaller installer, ICatalogSource catalog) : IDemoRunner
{
    private readonly DemoRunner _demo = new();

    public bool CanRun => true;

    public async Task<DemoProgress> RunAsync(
        IndexedAsset asset, IProgress<DemoProgress>? progress = null, CancellationToken ct = default)
    {
        if (asset.Latest.DemoProject is null)
        {
            return Fail(progress, $"{asset.Manifest.Name} has no demo.");
        }

        var index = await catalog.LoadAsync(ct);
        var byId = index.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);

        progress?.Report(new DemoProgress(DemoStage.Downloading, "Downloading the asset…"));
        // With the ref, so this lands where `add` puts it — the versioned layout — instead of the
        // legacy flat root, which would download the same asset a second time.
        var download = await Task.Run(() => installer.DownloadToCache(asset, byId, refFolder: asset.Latest.Ref), ct);
        if (!download.Success)
        {
            return Fail(progress, download.Messages.LastOrDefault() ?? "The download failed.");
        }

        // The installer decides where a clone lives, from the repo and the ref; asking it beats a
        // second copy of that rule here.
        var cloneRoot = await Task.Run(
            () => installer.ListCachedAssets(byId)
                .FirstOrDefault(c => string.Equals(c.Id, asset.Id, StringComparison.Ordinal))?.CloneRoot,
            ct);

        if (cloneRoot is null)
        {
            return Fail(progress, "The asset downloaded but its clone is not in the cache.");
        }

        var materialized = await Task.Run(() => _demo.Materialize(cloneRoot, asset), ct);
        if (!materialized.Success || materialized.ProjectPath is not { } project)
        {
            return Fail(progress, materialized.Messages.LastOrDefault() ?? "The demo could not be unpacked.");
        }

        progress?.Report(new DemoProgress(DemoStage.Building,
            "Building the demo — the first run downloads Stride, so this takes a few minutes…"));
        var built = await _demo.BuildAsync(project, cancellation: ct);
        if (!built.Success)
        {
            // The author's own build errors, not ours: a demo that doesn't compile is something
            // only they can fix, and "it failed" without the reason is a bug report nobody can act on.
            return Fail(progress, string.Join(" ", built.Messages));
        }

        var started = await Task.Run(() => _demo.Start(project), ct);
        if (!started.Success)
        {
            return Fail(progress, started.Messages.LastOrDefault() ?? "The demo could not be started.");
        }

        var running = new DemoProgress(DemoStage.Running, "The demo is starting — its window opens in a moment.");
        progress?.Report(running);
        return running;
    }

    private static DemoProgress Fail(IProgress<DemoProgress>? progress, string message)
    {
        var failure = new DemoProgress(DemoStage.Failed, message);
        progress?.Report(failure);
        return failure;
    }
}
