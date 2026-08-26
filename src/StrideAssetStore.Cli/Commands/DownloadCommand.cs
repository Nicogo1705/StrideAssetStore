// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;

namespace StrideAssetStore.Cli.Commands;

internal sealed class DownloadSettings : CatalogSettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id, or enough of it to be unambiguous.")]
    public string Asset { get; init; } = "";

    [CommandOption("--version <VERSION>")]
    [Description("Download a released version instead of the followed branch.")]
    public string? Version { get; init; }

    [CommandOption("--ref <REF>")]
    [Description("Download a raw git ref (branch, tag or commit).")]
    public string? Ref { get; init; }

    [CommandOption("--fork <OWNER/REPO>")]
    [Description("Download someone's fork instead of the author's repository.")]
    public string? Fork { get; init; }

    [CommandOption("--demo")]
    [Description("Unpack the demo too, so it is ready to build.")]
    public bool Demo { get; init; }
}

/// <summary>
/// Downloads an asset into the shared cache without touching any project.
/// </summary>
/// <remarks>
/// The counterpart of the app's "install as a shared asset". Useful before there is a project to
/// install into — filling the cache ahead of a flight, fetching what a demo needs, or looking at
/// an asset's source before deciding. The clone is the same one `add` would use, in the same
/// place, so a later `add` finds it already there rather than downloading it twice.
/// </remarks>
internal sealed class DownloadCommand : AsyncCommand<DownloadSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, DownloadSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        if (!CliOutput.RequireGit())
        {
            return 1;
        }

        var asset = CatalogAccess.Resolve(index, settings.Asset);
        var reference = Reference(asset, settings);
        var installer = new AssetInstaller();
        var catalog = index.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);

        AnsiConsole.MarkupLineInterpolated($"[grey]Asset:[/] {asset.Manifest.Name} ({asset.Id})");
        AnsiConsole.MarkupLineInterpolated($"[grey]Ref:[/] {reference}");

        var result = installer.DownloadToCache(asset, catalog, refFolder: reference, fork: settings.Fork);
        foreach (var message in result.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        if (!result.Success)
        {
            return 1;
        }

        var cached = installer.ListCachedAssets(catalog)
            .FirstOrDefault(c => string.Equals(c.Id, asset.Id, StringComparison.Ordinal));

        if (cached is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]In:[/] {cached.CloneRoot}");
        }

        if (settings.Demo && cached is not null)
        {
            var demo = new DemoRunner().Materialize(cached.CloneRoot, asset);
            foreach (var message in demo.Messages)
            {
                AnsiConsole.MarkupLineInterpolated($"{message}");
            }

            if (demo.ProjectPath is { } project)
            {
                AnsiConsole.MarkupLineInterpolated($"[grey]Demo:[/] {project}");
            }
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Nothing was added to a project. `add {asset.Id}` uses this clone; `list --cached` shows them all.[/]");
        return 0;
    }

    /// <summary>Same rule as `add`: a raw ref wins, then a published version, then the followed branch.</summary>
    private static string Reference(Core.Models.IndexedAsset asset, DownloadSettings settings)
    {
        if (settings.Ref is { Length: > 0 } raw)
        {
            return raw;
        }

        if (settings.Version is not { Length: > 0 } version)
        {
            return asset.Latest.Ref;
        }

        var certified = asset.Certified.FirstOrDefault(c => c.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        if (certified is not null)
        {
            return certified.Tag ?? certified.Commit;
        }

        var tagged = asset.Versions.FirstOrDefault(v => v.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        return tagged?.Tag
            ?? throw new InvalidOperationException($"'{asset.Id}' has no version {version}.");
    }
}
