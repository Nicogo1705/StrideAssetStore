// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;

namespace StrideAssetStore.Cli.Commands;

internal sealed class ListSettings : ProjectScopedSettings
{
    [CommandOption("--cached")]
    [Description("List what sits in the shared per-machine cache instead of what this project references.")]
    public bool Cached { get; init; }
}

/// <summary>
/// Shows what is installed: the assets this project references with their status, or everything
/// downloaded on this machine. The command-line view of "My projects" and "My assets".
/// </summary>
internal sealed class ListCommand : AsyncCommand<ListSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, ListSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var installer = new AssetInstaller();
        var catalog = CatalogAccess.ById(index);

        return settings.Cached ? ListCached(installer, catalog) : ListProject(installer, catalog, settings);
    }

    private static int ListCached(
        AssetInstaller installer, IReadOnlyDictionary<string, Core.Models.IndexedAsset> catalog)
    {
        var cached = installer.ListCachedAssets(catalog);
        if (cached.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Nothing downloaded yet. The cache lives at {AssetInstaller.GlobalCacheRoot}.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Asset");
        table.AddColumn("Ref");
        table.AddColumn("Status");
        table.AddColumn("Size");

        foreach (var asset in cached)
        {
            table.AddRow(
                Markup.Escape(asset.Name),
                Markup.Escape(string.IsNullOrEmpty(asset.Ref) ? "(legacy)" : asset.Ref),
                CliOutput.StatusMarkup(asset.Status),
                Markup.Escape(FormatSize(asset.SizeBytes)));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated($"[grey]{AssetInstaller.GlobalCacheRoot}[/]");
        return 0;
    }

    private static int ListProject(
        AssetInstaller installer, IReadOnlyDictionary<string, Core.Models.IndexedAsset> catalog, ListSettings settings)
    {
        var target = ProjectTarget.Resolve(settings.Target);
        var view = installer.Analyze(target, catalog);
        AnsiConsole.MarkupLineInterpolated($"[grey]Target:[/] {view.Path}");

        var any = false;
        foreach (var project in view.Projects)
        {
            if (project.Assets.Count == 0)
            {
                continue;
            }

            any = true;
            AnsiConsole.MarkupLineInterpolated($"[bold]{project.Name}[/]");

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Asset");
            table.AddColumn("Kind");
            table.AddColumn("Ref");
            table.AddColumn("Status");
            table.AddColumn("Stride");

            foreach (var asset in project.Assets)
            {
                table.AddRow(
                    Markup.Escape(asset.Name),
                    // A fork is not the store's asset; saying "local" would hide that.
                    asset.Fork is null ? Markup.Escape(asset.Kind) : "[yellow]fork[/]",
                    Markup.Escape(string.IsNullOrEmpty(asset.Ref) ? "-" : asset.Ref),
                    CliOutput.StatusMarkup(asset.Status),
                    Markup.Escape(asset.StrideVersion ?? "-"));
            }

            AnsiConsole.Write(table);

            foreach (var forked in project.Assets.Where(a => a.Fork is not null))
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[yellow]⚠ {forked.Name} comes from the fork {forked.Fork}[/] — not the store's copy, so no certification and no hash check.");
            }
        }

        if (!any)
        {
            AnsiConsole.MarkupLine("[grey]No store asset referenced here yet.[/] Add one with: strideassetstore add <asset>");
        }

        foreach (var dangling in view.Dangling)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]⚠ {dangling.Name} is listed in the solution but its files are gone.[/] Re-download it, or remove the entry.");
        }

        return 0;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
