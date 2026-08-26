// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;

namespace StrideAssetStore.Cli.Commands;

internal sealed class DemoSettings : CatalogSettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id, or enough of it to be unambiguous.")]
    public string Asset { get; init; } = "";

    [CommandOption("--no-run")]
    [Description("Download and build it, but don't start it.")]
    public bool NoRun { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Don't ask before building and running the author's code.")]
    public bool Yes { get; init; }
}

/// <summary>
/// Downloads, builds and runs an asset's demo.
/// </summary>
/// <remarks>
/// The demo lives outside <c>AssetData/</c> on purpose — nobody wants it in every project that
/// installs the asset — so this fetches the asset the normal way and then widens that clone. It
/// asks first: unlike installing, which puts source in a project you then choose to compile,
/// this builds and runs somebody else's code on the spot.
/// </remarks>
internal sealed class DemoCommand : AsyncCommand<DemoSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, DemoSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var asset = CatalogAccess.Resolve(index, settings.Asset);
        if (asset.Latest.DemoProject is null)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]{asset.Manifest.Name} has no demo.[/]");
            AnsiConsole.MarkupLine("[grey]An asset ships one by adding a runnable Demo/Demo.csproj to its repository.[/]");
            return 1;
        }

        if (!CliOutput.RequireGit())
        {
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]Demo:[/] {asset.Manifest.Name} ({asset.Id})");
        AnsiConsole.MarkupLineInterpolated($"[grey]From:[/] {asset.Repo} [grey]at[/] {asset.Latest.Ref}");
        AnsiConsole.MarkupLine(
            "[yellow]This builds and runs code from that repository on your machine.[/]"
            + (asset.Certified.Count > 0 ? " [green]The asset has certified versions.[/]" : " [grey]It is not certified.[/]"));

        if (!CliOutput.Confirm("Continue?", settings.Yes))
        {
            AnsiConsole.MarkupLine("[grey]Nothing was run.[/]");
            return 1;
        }

        var installer = new AssetInstaller();
        var catalog = index.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);

        // The ref is passed on purpose: without it the clone lands in the cache's legacy flat root
        // while `add` uses the versioned one, and the same asset would be downloaded twice into two
        // folders. A demo you ran and an asset you installed are meant to be the same clone.
        var download = installer.DownloadToCache(asset, catalog, refFolder: asset.Latest.Ref);
        foreach (var message in download.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        if (!download.Success)
        {
            return 1;
        }

        var runner = new DemoRunner();

        // Asked of the cache rather than recomputed: the folder name is derived from the repo and
        // the ref by the installer, and a second implementation of that rule would drift from it.
        var cloneRoot = installer.ListCachedAssets(catalog)
            .FirstOrDefault(c => string.Equals(c.Id, asset.Id, StringComparison.Ordinal))?.CloneRoot;
        if (cloneRoot is null)
        {
            AnsiConsole.MarkupLine("[red]The download reported success but the clone is not in the cache.[/]");
            return 1;
        }

        var materialized = runner.Materialize(cloneRoot, asset);
        foreach (var message in materialized.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        if (!materialized.Success || materialized.ProjectPath is not { } project)
        {
            return 1;
        }

        var built = await AnsiConsole.Status()
            .StartAsync("Building the demo…", _ => runner.BuildAsync(project, cancellation: cancellation));

        foreach (var message in built.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        if (!built.Success)
        {
            return 1;
        }

        if (settings.NoRun)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Built. Run it with:[/] dotnet run -c Release --project {project}");
            return 0;
        }

        var started = runner.Start(project);
        foreach (var message in started.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        return started.Success ? 0 : 1;
    }
}
