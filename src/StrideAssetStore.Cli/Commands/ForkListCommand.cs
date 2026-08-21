// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Git;

namespace StrideAssetStore.Cli.Commands;

internal sealed class ForkListSettings : CatalogSettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id, or enough of it to be unambiguous.")]
    public string Asset { get; init; } = "";
}

/// <summary>Lists an asset's forks, so `add --fork` can be given a name that exists.</summary>
internal sealed class ForkListCommand : AsyncCommand<ForkListSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, ForkListSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var asset = CatalogAccess.Resolve(index, settings.Asset);
        var forks = await new ForkLister().ListAsync(asset.Repo, cancellation);

        if (forks.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]No fork of {asset.Manifest.Name} on GitHub.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Fork");
        table.AddColumn("★");
        table.AddColumn("Last push");

        foreach (var fork in forks)
        {
            table.AddRow(
                Markup.Escape(fork.FullName),
                Markup.Escape(fork.Stars.ToString()),
                Markup.Escape(fork.PushedAt?.ToString("yyyy-MM-dd") ?? "-"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Install one with:[/] strideassetstore add {settings.Asset} --fork {forks[0].FullName}");
        return 0;
    }
}
