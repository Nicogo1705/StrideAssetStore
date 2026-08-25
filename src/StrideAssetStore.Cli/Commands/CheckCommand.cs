// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Serialization;

namespace StrideAssetStore.Cli.Commands;

internal sealed class CheckSettings : CommandSettings
{
    [CommandArgument(0, "[PATH]")]
    [Description("The asset repository to check. Defaults to the current directory.")]
    public string? Path { get; init; }

    [CommandOption("--strict")]
    [Description("Treat warnings as failures too (for CI).")]
    public bool Strict { get; init; }
}

/// <summary>
/// Checks an asset repository the way the store will read it, before anyone else does.
/// </summary>
/// <remarks>
/// The registry's own <c>validate</c> answers a different question — it needs a checkout of the
/// AssetContainer and judges entries already submitted. This one runs where the author is: is the
/// manifest there and complete, does the thumbnail it names exist, is there a README to render on
/// the asset page, is there a project under AssetData for the store to build against, and is there
/// build output that should not be committed. Every one of these has shipped broken at least once,
/// and each was only visible after the pull request.
/// </remarks>
internal sealed class CheckCommand : Command<CheckSettings>
{
    private const string AssetData = "AssetData";

    protected override int Execute(CommandContext context, CheckSettings settings, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(settings.Path) ? Environment.CurrentDirectory : settings.Path);

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{root} does not exist.[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]Checking[/] {root}");
        AnsiConsole.WriteLine();

        var report = Run(root, quiet: false);
        AnsiConsole.WriteLine();
        return report.Conclude(settings.Strict);
    }

    /// <summary>
    /// Runs every check and hands back what it found. <paramref name="quiet"/> keeps the passes and
    /// warnings to itself and prints only what is wrong — which is what `publish` wants: it calls
    /// this to decide whether a pull request is worth opening, not to produce a report.
    /// </summary>
    internal static Report Run(string root, bool quiet)
    {
        var report = new Report(quiet);
        var manifest = CheckManifest(root, report);
        CheckMedia(root, manifest, report);
        CheckReadme(root, report);
        CheckProject(root, report);
        return report;
    }

    /// <summary>The manifest is the file everything else is judged against, so it is read first.</summary>
    private static AssetManifest? CheckManifest(string root, Report report)
    {
        var path = Path.Combine(root, AssetData, "manifest.json");
        if (!File.Exists(path))
        {
            report.Fail($"{AssetData}/manifest.json is missing — the store reads this file and nothing else identifies the asset.");
            return null;
        }

        AssetManifest? manifest;
        try
        {
            manifest = StrideAssetStoreJson.Deserialize<AssetManifest>(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            report.Fail($"{AssetData}/manifest.json is not valid JSON: {ex.Message}");
            return null;
        }

        if (manifest is null)
        {
            report.Fail($"{AssetData}/manifest.json could not be read as a manifest.");
            return null;
        }

        report.Pass($"{AssetData}/manifest.json reads as {manifest.Name}");

        if (!AssetId.IsValid(manifest.Id))
        {
            report.Fail($"id '{manifest.Id}' is not a valid store id — reverse-DNS and lowercase, e.g. com.you.cool-thing.");
        }
        else if (manifest.Id.Contains("com.you.", StringComparison.OrdinalIgnoreCase)
            || manifest.Id.Contains("yourname", StringComparison.OrdinalIgnoreCase))
        {
            report.Fail($"id '{manifest.Id}' still carries the template's placeholder.");
        }

        Required(report, "name", manifest.Name);
        Required(report, "description", manifest.Description);
        Required(report, "category", manifest.Category);
        Required(report, "license", manifest.License);

        if (manifest.Authors.Count == 0)
        {
            report.Warn("no authors listed — the asset page will have nobody to credit.");
        }

        if (manifest.Tags.Count == 0)
        {
            report.Warn("no tags — tags are how people find an asset that isn't named after what it does.");
        }

        if (manifest.Nuget is { } nuget && string.IsNullOrWhiteSpace(nuget.PackageId))
        {
            report.Fail("nuget.packageId is empty — remove the nuget block, or fill it in.");
        }

        if (string.Equals(manifest.DefaultImport, "nuget", StringComparison.OrdinalIgnoreCase) && manifest.Nuget is null)
        {
            report.Fail("defaultImport is 'nuget' but no nuget package is declared.");
        }

        return manifest;
    }

    /// <summary>
    /// Thumbnail and media are read from the repository root, not from the clone: they are display
    /// files the storefront fetches raw. A path that doesn't resolve is a broken image on the store
    /// page, and the author is the last person to see it.
    /// </summary>
    private static void CheckMedia(string root, AssetManifest? manifest, Report report)
    {
        if (manifest is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.Thumbnail))
        {
            report.Warn("no thumbnail — the catalog card falls back to a letter.");
        }
        else if (!File.Exists(Path.Combine(root, manifest.Thumbnail)))
        {
            report.Fail($"thumbnail '{manifest.Thumbnail}' is declared but missing from the repository.");
        }
        else
        {
            report.Pass($"thumbnail {manifest.Thumbnail}");
        }

        if (manifest.Media.Count == 0)
        {
            report.Warn("no media — the asset page will show the thumbnail and nothing else.");
            return;
        }

        var missing = manifest.Media.Where(m => !File.Exists(Path.Combine(root, m))).ToList();
        foreach (var item in missing)
        {
            report.Fail($"media '{item}' is declared but missing from the repository.");
        }

        if (missing.Count == 0)
        {
            report.Pass($"{manifest.Media.Count} media file(s) present");
        }

        // The template ships one placeholder screenshot so the manifest is never empty, and leaving
        // it in is the most common thing to forget. Matched by its exact path, not by the word
        // "placeholder": an asset whose subject is placeholder textures names its files that way,
        // and telling its author to replace them is nonsense.
        const string templateScreenshot = "media/screenshot.png";
        if (manifest.Media.Any(m => string.Equals(m, templateScreenshot, StringComparison.OrdinalIgnoreCase))
            && File.Exists(Path.Combine(root, "media", "screenshot.png")))
        {
            report.Warn($"'{templateScreenshot}' is the path the template's placeholder ships at — check it is a real capture by now.");
        }
    }

    private static void CheckReadme(string root, Report report)
    {
        // Listed, then filtered — not globbed. A search pattern is matched case-sensitively on
        // Linux and macOS, so "README*" quietly misses the readme.md half the world writes.
        var readme = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("README", StringComparison.OrdinalIgnoreCase));

        if (readme is null)
        {
            report.Warn("no README.md at the repository root — the asset page falls back to the one-line description.");
            return;
        }

        var text = File.ReadAllText(readme);
        report.Pass($"{Path.GetFileName(readme)} ({text.Length} chars) will be rendered on the asset page");

        if (text.Contains("Your Asset Name", StringComparison.OrdinalIgnoreCase))
        {
            report.Fail("README.md still carries the template's title ('Your Asset Name').");
        }
    }

    /// <summary>
    /// What installing actually copies: everything under <c>AssetData/</c>. A project has to be
    /// there, and build output must not — a clone is a sparse checkout of this folder, so anything
    /// committed here lands in every user's cache.
    /// </summary>
    private static void CheckProject(string root, Report report)
    {
        var assetData = Path.Combine(root, AssetData);
        if (!Directory.Exists(assetData))
        {
            report.Fail($"{AssetData}/ is missing — it is the only folder an install copies.");
            return;
        }

        var projects = Directory.EnumerateFiles(assetData, "*.csproj", SearchOption.AllDirectories).ToList();
        if (projects.Count == 0)
        {
            report.Fail($"no .csproj under {AssetData}/ — nothing for a project to reference.");
        }
        else
        {
            report.Pass($"{projects.Count} project(s) under {AssetData}/");

            var stride = projects
                .Select(File.ReadAllText)
                .Any(text => text.Contains("Stride.", StringComparison.OrdinalIgnoreCase));
            if (!stride)
            {
                report.Warn($"no Stride.* package reference found under {AssetData}/ — the store shows 'Stride version: unknown'.");
            }
        }

        // Build output on disk is normal — every build makes some. Committed build output is the
        // problem, because a clone is a sparse checkout of this folder: whatever git tracks here
        // lands in every user's cache. So the question is asked of git, not of the filesystem.
        var tracked = TrackedBuildOutput(root);
        if (tracked is null)
        {
            report.Warn($"not a git repository (or git is missing) — couldn't check whether build output is committed under {AssetData}/.");
        }
        else if (tracked.Count > 0)
        {
            report.Fail($"{tracked.Count} committed build-output file(s) under {AssetData}/ (e.g. {tracked[0]}) — every user downloads them. Add bin/ and obj/ to .gitignore, then `git rm -r --cached` them.");
        }
        else
        {
            report.Pass("no committed build output");
        }
    }

    /// <summary>Files git tracks under AssetData/bin or AssetData/obj, or null when git can't answer.</summary>
    private static IReadOnlyList<string>? TrackedBuildOutput(string root)
    {
        var listed = Core.Local.Shell.ProcessRunner
            .RunAsync("git", ["-C", root, "ls-files", "--", $"{AssetData}/"], root)
            .GetAwaiter().GetResult();

        if (!listed.Ok)
        {
            return null;
        }

        return listed.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || line.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void Required(Report report, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            report.Fail($"{field} is empty.");
        }
    }

    /// <summary>Collects the findings so the exit code can be decided from all of them at once.</summary>
    internal sealed class Report(bool quiet = false)
    {
        private int _failures;
        private int _warnings;

        /// <summary>How many things are wrong — the number `publish` refuses to submit over.</summary>
        public int Failures => _failures;

        public void Pass(string message)
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLineInterpolated($"[green]✓[/] {message}");
            }
        }

        public void Warn(string message)
        {
            _warnings++;
            if (!quiet)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]⚠[/] {message}");
            }
        }

        // Always printed, quiet or not: a caller that suppressed the running commentary still has
        // to be told why nothing happened.
        public void Fail(string message)
        {
            _failures++;
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] {message}");
        }

        public int Conclude(bool strict)
        {
            if (_failures > 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]{_failures} problem(s)[/]{(_warnings > 0 ? $" and {_warnings} warning(s)" : "")} — fix these before publishing.");
                return 1;
            }

            if (_warnings > 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[yellow]{_warnings} warning(s)[/] — publishable, but worth a look.");
                return strict ? 1 : 0;
            }

            AnsiConsole.MarkupLine("[green]✓ Ready to publish.[/]");
            return 0;
        }
    }
}
