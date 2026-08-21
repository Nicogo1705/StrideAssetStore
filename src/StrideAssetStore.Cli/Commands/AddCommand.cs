// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Commands;

internal sealed class AddSettings : ProjectScopedSettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id, or enough of it to be unambiguous.")]
    public string Asset { get; init; } = "";

    [CommandOption("--version <VERSION>")]
    [Description("Install a released version (a git tag published by the author) instead of the followed branch.")]
    public string? Version { get; init; }

    [CommandOption("--ref <REF>")]
    [Description("Install a raw git ref (branch, tag or commit). Advanced; --version is usually what you want.")]
    public string? Ref { get; init; }

    [CommandOption("--nuget")]
    [Description("Add the asset's published NuGet package instead of cloning its source.")]
    public bool Nuget { get; init; }

    [CommandOption("--source")]
    [Description("Clone the source even when the asset suggests its NuGet package.")]
    public bool Source { get; init; }


    [CommandOption("--fork <OWNER/REPO>")]
    [Description("Install from a fork of the asset instead of the author's repository — its own tags, its own history, no certification.")]
    public string? Fork { get; init; }

    [CommandOption("--stride <VERSION>")]
    [Description("Rewrite the installed asset's Stride package references to this version, when it targets another one.")]
    public string? Stride { get; init; }
}

/// <summary>
/// Installs an asset into the project you are standing in — the command-line half of the desktop
/// app's Install page, down to the same shared cache and the same portable reference.
/// </summary>
internal sealed class AddCommand : AsyncCommand<AddSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AddSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        CliOutput.NoteCatalogSource(fromCache, index);

        var asset = CatalogAccess.Resolve(index, settings.Asset);
        var target = ProjectTarget.Resolve(settings.Target);
        var installer = new AssetInstaller();
        var projects = ProjectTarget.SelectProjects(installer, target, settings.Project, settings.AllProjects);

        AnsiConsole.MarkupLineInterpolated($"[grey]Asset:[/] {asset.Manifest.Name} ({asset.Id})");
        AnsiConsole.MarkupLineInterpolated($"[grey]Into:[/] {string.Join(", ", projects.Select(Path.GetFileName))}");

        var useNuget = settings.Nuget
            || (!settings.Source && asset.Manifest.Nuget is not null
                && string.Equals(asset.Manifest.DefaultImport, "nuget", StringComparison.OrdinalIgnoreCase));

        // A fork is source code, and a NuGet package is whatever its author published — asking for
        // both would have quietly installed the official package and ignored the fork entirely.
        if (useNuget && settings.Fork is not null)
        {
            AnsiConsole.MarkupLine(
                "[red]A fork can't be installed as a NuGet package.[/] Add --source to clone it, or drop --fork.");
            return 1;
        }

        if (useNuget)
        {
            if (asset.Manifest.Nuget is null)
            {
                AnsiConsole.MarkupLine("[red]This asset is not published on NuGet.[/] Drop --nuget to clone its source.");
                return 1;
            }

            return CliOutput.Report(installer.InstallNuget(asset, projects));
        }

        if (!CliOutput.RequireGit())
        {
            return 1;
        }

        var reference = ResolveRef(asset, settings);
        AnsiConsole.MarkupLineInterpolated($"[grey]Ref:[/] {reference}");

        if (settings.Fork is { } fork)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Fork:[/] {fork}");
        }

        var result = installer.Install(
            asset,
            reference,
            projects,
            CatalogAccess.ById(index),
            solutionPath: ProjectTarget.SolutionOf(target),
            fork: settings.Fork);

        var exit = CliOutput.Report(result);
        if (exit == 0 && settings.Stride is { } stride)
        {
            exit = Retarget(installer, target, CatalogAccess.ById(index), asset.Id, stride);
        }

        return exit;
    }

    /// <summary>
    /// Points the freshly installed asset at the Stride version this game uses. An asset targets
    /// whatever its author had; without this the project restores two Stride versions at once, or
    /// fails outright when the author's is not on your feeds.
    /// </summary>
    private static int Retarget(
        AssetInstaller installer, string target,
        IReadOnlyDictionary<string, IndexedAsset> catalog, string assetId, string strideVersion)
    {
        var installed = installer.Analyze(target, catalog).Projects
            .SelectMany(p => p.Assets)
            .FirstOrDefault(a => a.Id.Equals(assetId, StringComparison.OrdinalIgnoreCase));

        if (installed is null || string.IsNullOrEmpty(installed.CloneRoot))
        {
            AnsiConsole.MarkupLine("[yellow]⚠ Installed, but its clone couldn't be located to retarget Stride.[/]");
            return 1;
        }

        var changed = installer.RetargetStride(installer.CloneCsprojs(installed.CloneRoot), strideVersion);
        AnsiConsole.MarkupLineInterpolated($"[green]✓ Retargeted {changed} project(s) to Stride {strideVersion}.[/]");
        return 0;
    }

    /// <summary>
    /// Turns the user's intent into a git ref. A version is matched against what the author actually
    /// published — certified releases first, then plain tags — so a typo fails here rather than
    /// cloning something that merely happens to exist.
    /// </summary>
    private static string ResolveRef(IndexedAsset asset, AddSettings settings)
    {
        if (settings.Ref is { } raw)
        {
            return raw;
        }

        if (settings.Version is not { } version)
        {
            return asset.Latest.Ref;
        }

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
            : $"'{asset.Id}' has no published version yet — install its followed branch by omitting --version.");
    }
}
