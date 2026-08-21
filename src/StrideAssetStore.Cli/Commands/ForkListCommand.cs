// Copyright (c) 2026 Nicogo1705
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
        var (reached, forks) = await new ForkLister().TryListAsync(asset.Repo, cancellation);

        if (!reached)
        {
            // Saying "no forks" here would turn a failed request into a fact. GitHub allows 60
            // requests an hour per IP anonymously, so this is a normal thing to hit.
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]GitHub didn't answer for {asset.Manifest.Name} — rate limit, proxy, or no network.[/]");
            AnsiConsole.MarkupLine("[grey]Set GITHUB_TOKEN to raise the limit. `add --fork owner/repo` works regardless.[/]");
            return 1;
        }

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
