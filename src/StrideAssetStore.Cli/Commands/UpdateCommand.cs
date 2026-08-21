// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Commands;

internal sealed class UpdateSettings : ProjectScopedSettings
{
    [CommandArgument(0, "[ASSET]")]
    [Description("Asset to update. Omit to update every outdated asset in the project.")]
    public string? Asset { get; init; }

    [CommandOption("--version <VERSION>")]
    [Description("Switch to a published version instead of following the current ref.")]
    public string? Version { get; init; }

    [CommandOption("--ref <REF>")]
    [Description("Switch to a raw git ref (branch, tag or commit).")]
    public string? Ref { get; init; }
}

/// <summary>
/// Brings installed assets up to date, or moves one onto another version — the two things the
/// desktop app's "My projects" page does, minus the clicking.
/// </summary>
internal sealed class UpdateCommand : AsyncCommand<UpdateSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, UpdateSettings settings, CancellationToken cancellation)
    {
        if (!CliOutput.RequireGit())
        {
            return 1;
        }

        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var catalog = CatalogAccess.ById(index);
        var installer = new AssetInstaller();
        var target = ProjectTarget.Resolve(settings.Target);
        var view = installer.Analyze(target, catalog);

        var switching = settings.Version is not null || settings.Ref is not null;
        if (switching && settings.Asset is null)
        {
            AnsiConsole.MarkupLine("[red]Name the asset to switch:[/] --version and --ref act on one asset at a time.");
            return 1;
        }

        // --project / --all-projects were inherited options this command ignored, and the "use the
        // full id" advice below could not fix the case they exist for: one asset referenced by two
        // projects of a solution has the same id in both, so --version was simply impossible there.
        var scope = settings.AllProjects || settings.Project is not null
            ? ProjectTarget.SelectProjects(installer, target, settings.Project, settings.AllProjects)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        var installed = view.Projects
            .Where(p => scope is null || scope.Contains(Path.GetFullPath(p.CsprojPath)))
            .SelectMany(p => p.Assets.Select(a => (Project: p, Asset: a)))
            .Where(x => settings.Asset is null || Matches(x.Asset, settings.Asset))
            .ToList();

        if (installed.Count == 0)
        {
            AnsiConsole.MarkupLine(settings.Asset is null
                ? "[grey]No store asset installed here.[/]"
                : $"[red]'{settings.Asset}' is not installed in this project.[/]");
            return 1;
        }

        return switching
            ? Switch(installer, installed, catalog, target, settings)
            : UpdateAll(installer, installed, catalog);
    }

    private static bool Matches(ProjectAsset asset, string query) =>
        asset.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static int UpdateAll(
        AssetInstaller installer,
        List<(ProjectNode Project, ProjectAsset Asset)> installed,
        IReadOnlyDictionary<string, IndexedAsset> catalog)
    {
        // NuGet-installed assets are the package manager's business, not ours.
        var updatable = installed.Where(x => x.Asset.Kind == "local").ToList();
        var skippedNuget = installed.Count - updatable.Count;

        var failed = 0;
        var changed = 0;
        foreach (var (_, asset) in updatable)
        {
            if (asset.Status == "up-to-date")
            {
                AnsiConsole.MarkupLineInterpolated($"[grey]• {asset.Name} is already up to date.[/]");
                continue;
            }

            if (string.IsNullOrEmpty(asset.CloneRoot) || !Directory.Exists(asset.CloneRoot))
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]⚠ {asset.Name}: its clone is missing — reinstall it with `add`.[/]");
                failed++;
                continue;
            }

            // A legacy clone records no ref. "main" was a guess, and a repository on `master` got
            // told git couldn't update a branch the user never chose. The catalog knows the real one.
            var reference = string.IsNullOrEmpty(asset.Ref)
                ? (catalog.TryGetValue(asset.Id, out var entry) ? entry.Latest.Ref : "main")
                : asset.Ref;
            var commit = installer.UpdateInstalled(asset.CloneRoot, reference);
            if (commit is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]✗ {asset.Name}: git couldn't update {reference}.[/]");
                failed++;
                continue;
            }

            changed++;
            AnsiConsole.MarkupLineInterpolated($"[green]✓ {asset.Name}[/] → {commit[..Math.Min(7, commit.Length)]} ({reference})");
        }

        if (skippedNuget > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]{skippedNuget} NuGet-installed asset(s) skipped — update those with dotnet add package.[/]");
        }

        if (changed == 0 && failed == 0)
        {
            AnsiConsole.MarkupLine("[green]Everything is up to date.[/]");
        }

        return failed > 0 ? 1 : 0;
    }

    private static int Switch(
        AssetInstaller installer,
        List<(ProjectNode Project, ProjectAsset Asset)> installed,
        IReadOnlyDictionary<string, IndexedAsset> catalog,
        string target,
        UpdateSettings settings)
    {
        if (installed.Count > 1)
        {
            var distinct = installed.Select(x => x.Asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count > 1)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]'{settings.Asset}' matches {distinct.Count} installed assets:[/] {string.Join(", ", distinct)}.");
                AnsiConsole.MarkupLine("[grey]Use the full id.[/]");
                return 1;
            }

            // Same asset, several projects: the id can't disambiguate it — the project can.
            var projects = string.Join(", ", installed.Select(x => x.Project.Name).Distinct());
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{installed[0].Asset.Name} is referenced by {installed.Count} projects:[/] {projects}.");
            AnsiConsole.MarkupLine("[grey]Pick one with --project <NAME>, or switch them all with --all-projects.[/]");
            return 1;
        }

        var (project, current) = installed[0];
        if (current.Kind != "local")
        {
            AnsiConsole.MarkupLine("[red]This asset was installed as a NuGet package.[/] Change its version with dotnet add package.");
            return 1;
        }

        if (!catalog.TryGetValue(current.Id, out var asset))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]'{current.Id}' is no longer in the catalog[/] — nothing to switch to.");
            return 1;
        }

        var newRef = settings.Ref ?? ResolveVersionRef(asset, settings.Version!);
        AnsiConsole.MarkupLineInterpolated($"[grey]{current.Name}:[/] {current.Ref} → {newRef}");

        var result = installer.SwitchRef(
            asset, current, project.CsprojPath, newRef, catalog, ProjectTarget.SolutionOf(target));
        return CliOutput.Report(result);
    }

    private static string ResolveVersionRef(IndexedAsset asset, string version)
    {
        var certified = asset.Certified.FirstOrDefault(c => c.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        if (certified is not null)
        {
            return certified.Tag ?? certified.Commit;
        }

        var tagged = asset.Versions.FirstOrDefault(v => v.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        if (tagged is not null)
        {
            return tagged.Tag;
        }

        var known = asset.Certified.Select(c => c.Version).Concat(asset.Versions.Select(v => v.Version)).Distinct().ToList();
        throw new InvalidOperationException(known.Count > 0
            ? $"'{asset.Id}' has no version {version}. Published: {string.Join(", ", known)}."
            : $"'{asset.Id}' has no published version — use --ref to follow a branch.");
    }
}
