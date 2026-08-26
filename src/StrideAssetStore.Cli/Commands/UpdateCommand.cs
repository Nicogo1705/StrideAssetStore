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

    [CommandOption("--cached")]
    [Description("Update every clone in the shared cache, whatever project uses it. Needs no project.")]
    public bool Cached { get; init; }
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

        if (settings.Cached)
        {
            // Before the project is resolved: the point of --cached is to work from anywhere,
            // including a directory that has no solution anywhere above it.
            if (settings.Version is not null || settings.Ref is not null)
            {
                AnsiConsole.MarkupLine(
                    "[red]--version and --ref change what a project references[/] — they need a project, so they don't combine with --cached.");
                return 1;
            }

            return UpdateCache(installer, catalog, settings.Asset);
        }

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
        Matches(asset.Id, asset.Name, query);

    private static bool Matches(string id, string name, string query) =>
        id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || name.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Updates the clones in the shared cache rather than one project's assets.
    /// </summary>
    /// <remarks>
    /// The project-scoped path can only reach what the current solution happens to reference, so
    /// someone with several solutions had to walk into each one to update the same clone. Clones are
    /// shared: refreshing one here refreshes it for every project on the machine. This is what the
    /// desktop app's "My assets" page does, and it uses the same two calls.
    /// </remarks>
    private static int UpdateCache(
        AssetInstaller installer,
        IReadOnlyDictionary<string, IndexedAsset> catalog,
        string? filter)
    {
        var cached = installer.ListCachedAssets(catalog)
            .Where(a => filter is null || Matches(a.Id, a.Name, filter))
            .ToList();

        if (cached.Count == 0)
        {
            AnsiConsole.MarkupLine(filter is null
                ? "[grey]Nothing in the cache — install an asset with `add`, or fetch one with `download`.[/]"
                : $"[red]'{filter}' is not in the cache.[/]");
            return 1;
        }

        var changed = 0;
        var failed = 0;
        foreach (var asset in cached)
        {
            if (asset.Status == "up-to-date")
            {
                AnsiConsole.MarkupLineInterpolated($"[grey]• {asset.Name} is already up to date.[/]");
                continue;
            }

            // Ahead of the catalogue: this clone followed its branch past the last index build.
            // Fetching would not move it, and calling that an update would be a lie.
            if (asset.Status == "ahead")
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[blue]• {asset.Name} is ahead of the catalogue[/] — nothing to fetch.");
                continue;
            }

            // A legacy clone records no ref in its path; the catalogue knows the one it follows.
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
            AnsiConsole.MarkupLineInterpolated(
                $"[green]✓ {asset.Name}[/] → {commit[..Math.Min(7, commit.Length)]} ({reference})");
        }

        if (changed == 0 && failed == 0)
        {
            AnsiConsole.MarkupLine("[green]Every clone in the cache is up to date.[/]");
        }

        return failed > 0 ? 1 : 0;
    }

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
        var distinct = installed.Select(x => x.Asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 1)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]'{settings.Asset}' matches {distinct.Count} installed assets:[/] {string.Join(", ", distinct)}.");
            AnsiConsole.MarkupLine("[grey]Use the full id.[/]");
            return 1;
        }

        // One asset, several projects. --all-projects is an answer to that, so honour it instead of
        // suggesting it back to someone who already passed it.
        if (installed.Count > 1 && !settings.AllProjects)
        {
            var projects = string.Join(", ", installed.Select(x => x.Project.Name).Distinct());
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{installed[0].Asset.Name} is referenced by {installed.Count} projects:[/] {projects}.");
            AnsiConsole.MarkupLine("[grey]Pick one with --project <NAME>, or switch them all with --all-projects.[/]");
            return 1;
        }

        if (installed[0].Asset.Kind != "local")
        {
            AnsiConsole.MarkupLine("[red]This asset was installed as a NuGet package.[/] Change its version with dotnet add package.");
            return 1;
        }

        if (!catalog.TryGetValue(installed[0].Asset.Id, out var asset))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]'{installed[0].Asset.Id}' is no longer in the catalog[/] — nothing to switch to.");
            return 1;
        }

        var newRef = settings.Ref ?? ResolveVersionRef(asset, settings.Version!);
        var solution = ProjectTarget.SolutionOf(target);
        var failed = 0;
        foreach (var (project, current) in installed)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]{current.Name} in {project.Name}:[/] {current.Ref} → {newRef}");
            if (CliOutput.Report(installer.SwitchRef(asset, current, project.CsprojPath, newRef, catalog, solution)) != 0)
            {
                failed++;
            }
        }

        return failed > 0 ? 1 : 0;
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
