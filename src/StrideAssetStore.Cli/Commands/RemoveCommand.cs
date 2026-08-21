// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;

namespace StrideAssetStore.Cli.Commands;

internal sealed class RemoveSettings : ProjectScopedSettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset to remove from the project.")]
    public string Asset { get; init; } = "";

    [CommandOption("--delete-clone")]
    [Description("Also delete the downloaded copy from the shared cache. Other projects following it will break.")]
    public bool DeleteClone { get; init; }
}

/// <summary>Removes an asset's reference from a project, and optionally its cached copy.</summary>
internal sealed class RemoveCommand : AsyncCommand<RemoveSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, RemoveSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var installer = new AssetInstaller();
        var target = ProjectTarget.Resolve(settings.Target);
        var view = installer.Analyze(target, CatalogAccess.ById(index));

        var matches = view.Projects
            .SelectMany(p => p.Assets.Select(a => (Project: p, Asset: a)))
            .Where(x => x.Asset.Id.Contains(settings.Asset, StringComparison.OrdinalIgnoreCase)
                || x.Asset.Name.Contains(settings.Asset, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]'{settings.Asset}' is not installed in this project.[/]");
            return 1;
        }

        // Removing from the wrong project is silent damage, so an ambiguous name stops here.
        var distinct = matches.Select(m => m.Asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 1)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]'{settings.Asset}' matches {distinct.Count} assets:[/] {string.Join(", ", distinct)}.");
            return 1;
        }

        var name = matches[0].Asset.Name;
        var targets = string.Join(", ", matches.Select(m => m.Project.Name));
        if (!CliOutput.Confirm($"Remove {name} from {targets}?", settings.Yes))
        {
            return 1;
        }

        var failed = 0;
        foreach (var (project, asset) in matches)
        {
            var removed = asset.Kind == "nuget"
                ? installer.UninstallNuget(project.CsprojPath, asset.PackageId ?? asset.Id)
                : installer.UninstallLocal(project.CsprojPath, asset.RawInclude);

            if (removed)
            {
                AnsiConsole.MarkupLineInterpolated($"[green]✓ Removed {asset.Name} from {project.Name}.[/]");
            }
            else
            {
                failed++;
                AnsiConsole.MarkupLineInterpolated($"[red]✗ Couldn't remove {asset.Name} from {project.Name}.[/]");
            }
        }

        if (settings.DeleteClone)
        {
            foreach (var root in matches.Select(m => m.Asset.CloneRoot).Where(r => !string.IsNullOrEmpty(r)).Distinct())
            {
                if (installer.DeleteClone(root))
                {
                    AnsiConsole.MarkupLineInterpolated($"[green]✓ Deleted {root}.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]✗ Couldn't delete {root}.[/]");
                }
            }
        }

        return failed > 0 ? 1 : 0;
    }
}
