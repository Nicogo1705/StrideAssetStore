// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Catalog;

namespace StrideAssetStore.Cli.Commands;

internal sealed class SearchSettings : CatalogSettings
{
    [CommandArgument(0, "[QUERY]")]
    [Description("Words to look for in names, ids, tags and descriptions. Omit to list everything.")]
    public string? Query { get; init; }

    [CommandOption("--category <NAME>")]
    [Description("Only assets in this category.")]
    public string? Category { get; init; }

    [CommandOption("--stride <VERSION>")]
    [Description("Only assets compatible with this Stride version (e.g. 4.4.0-beta5).")]
    public string? Stride { get; init; }

    [CommandOption("--certified")]
    [Description("Only assets with a certified version.")]
    public bool Certified { get; init; }

    [CommandOption("-n|--take <COUNT>")]
    [Description("How many results to show. Defaults to 20.")]
    [DefaultValue(20)]
    public int Take { get; init; } = 20;
}

/// <summary>Finds assets in the catalog — the step before `add`, since ids are long.</summary>
internal sealed class SearchCommand : AsyncCommand<SearchSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, SearchSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var results = new AssetCatalog(index).Query(new CatalogQuery
        {
            Text = settings.Query,
            Category = settings.Category,
            StrideVersion = settings.Stride,
            Certified = settings.Certified ? CertifiedFilter.CertifiedOnly : CertifiedFilter.All,
            // Only consulted when there is no query text: with one, Query ranks by relevance.
            SortBy = CatalogSort.Stars,
        });

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No asset matches.[/]");
            return 1;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Id");
        table.AddColumn("Name");
        table.AddColumn("Stride");
        table.AddColumn("★");
        table.AddColumn("");

        var take = Math.Max(1, settings.Take);
        foreach (var asset in results.Take(take))
        {
            table.AddRow(
                Markup.Escape(asset.Id),
                Markup.Escape(asset.Manifest.Name),
                Markup.Escape(asset.Latest.DetectedStrideVersion ?? "-"),
                Markup.Escape((asset.Stars ?? 0).ToString()),
                asset.Certified.Count > 0 ? "[green]certified[/]" : "");
        }

        AnsiConsole.Write(table);
        if (results.Count > take)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]{results.Count - take} more — refine the query or raise --take.[/]");
        }

        return 0;
    }
}
