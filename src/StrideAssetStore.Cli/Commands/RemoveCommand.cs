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

        // --project / --all-projects are inherited options that this command used to ignore: it
        // removed the asset from every project holding it, which is the silent damage the ambiguity
        // check below exists to prevent. Without either flag, a solution-wide removal must be asked
        // for explicitly.
        var scope = settings.AllProjects || settings.Project is not null
            ? ProjectTarget.SelectProjects(installer, target, settings.Project, settings.AllProjects)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var matches = view.Projects
            .Where(p => scope is null || scope.Contains(Path.GetFullPath(p.CsprojPath)))
            .SelectMany(p => p.Assets.Select(a => (Project: p, Asset: a)))
            .Where(x => x.Asset.Id.Contains(settings.Asset, StringComparison.OrdinalIgnoreCase)
                || x.Asset.Name.Contains(settings.Asset, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated(scope is null
                ? $"[red]'{settings.Asset}' is not installed in this project.[/]"
                : (FormattableString)$"[red]'{settings.Asset}' is not installed in the selected project(s).[/]");
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
        var projectCount = matches.Select(m => m.Project.CsprojPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (scope is null && projectCount > 1)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{name} is referenced by {projectCount} projects:[/] {string.Join(", ", matches.Select(m => m.Project.Name).Distinct())}.");
            AnsiConsole.MarkupLine("[grey]Pick one with --project <NAME>, or remove it everywhere with --all-projects.[/]");
            return 1;
        }

        var targets = string.Join(", ", matches.Select(m => m.Project.Name).Distinct());
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
