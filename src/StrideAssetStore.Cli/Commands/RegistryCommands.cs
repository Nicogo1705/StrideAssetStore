// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Registry;
using StrideAssetStore.Core.Local.Shell;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Commands;

/// <summary>Options shared by every command that opens a pull request against the registry.</summary>
internal class RegistrySettings : CommandSettings
{
    [CommandOption("--registry <OWNER/REPO>")]
    [Description("Registry repository to submit to. Defaults to the official one.")]
    [DefaultValue("Nicogo1705/AssetContainer")]
    public string Registry { get; init; } = "Nicogo1705/AssetContainer";

    [CommandOption("--branch <NAME>")]
    [Description("Branch the pull request targets.")]
    [DefaultValue("main")]
    public string Branch { get; init; } = "main";

    /// <summary>The registry as the publisher wants it, or null when the value is malformed.</summary>
    public RegistryPublisher? Publisher()
    {
        var parts = Registry.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? new RegistryPublisher(parts[0], parts[1], Branch) : null;
    }
}

/// <summary>Shared shape of the four registry commands: check gh, run the flow, report the PR.</summary>
internal static class RegistryFlow
{
    public static async Task<int> RunAsync(
        RegistrySettings settings, string doing, Func<RegistryPublisher, Task<PublishResult>> action)
    {
        if (!CliOutputGuards.RequireGh())
        {
            return 1;
        }

        if (settings.Publisher() is not { } publisher)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]'{settings.Registry}' is not an owner/repo pair.[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]{doing} — forking, branching and opening a pull request…[/]");
        var result = await action(publisher);

        if (!result.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗ {result.Error ?? "The pull request could not be opened."}[/]");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[green]✓ Pull request opened:[/] {result.PullRequestUrl}");
        AnsiConsole.MarkupLine("[grey]A maintainer reviews it; the catalog picks the change up once it is merged.[/]");
        return 0;
    }
}

internal sealed class PublishSettings : RegistrySettings
{
    [CommandArgument(0, "[PATH]")]
    [Description("The asset repository to submit. Defaults to the current directory.")]
    public string? Path { get; init; }

    [CommandOption("--ref <REF>")]
    [Description("Branch the store should follow. Defaults to the repository's current branch.")]
    public string? Ref { get; init; }

    [CommandOption("--repo <URL>")]
    [Description("Repository URL to register. Defaults to the checkout's 'origin' remote.")]
    public string? Repo { get; init; }

    [CommandOption("--force")]
    [Description("Submit even when `check` finds problems.")]
    public bool Force { get; init; }
}

/// <summary>
/// Submits an asset to the registry — the app's "Manage store assets" form, as a command.
/// </summary>
/// <remarks>
/// Reads the three facts it needs from the checkout you are standing in rather than asking for
/// them: the id from <c>AssetData/manifest.json</c>, the repository from the <c>origin</c> remote,
/// the followed branch from HEAD. And it runs <c>check</c> first: a registry entry pointing at a
/// repository with a missing manifest or a broken media path is a pull request a maintainer has to
/// reject, which is slower for everyone than being told here.
/// </remarks>
internal sealed class PublishCommand : AsyncCommand<PublishSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, PublishSettings settings, CancellationToken cancellation)
    {
        var root = System.IO.Path.GetFullPath(
            string.IsNullOrWhiteSpace(settings.Path) ? Environment.CurrentDirectory : settings.Path);

        var manifest = RepoFacts.Manifest(root);
        if (manifest is null)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]No readable AssetData/manifest.json under {root}.[/] Run this from your asset repository.");
            return 1;
        }

        var repo = settings.Repo ?? RepoFacts.OriginUrl(root);
        if (repo is null)
        {
            AnsiConsole.MarkupLine("[red]No 'origin' remote to register.[/] Push the repository to GitHub first, or pass --repo.");
            return 1;
        }

        var followed = settings.Ref ?? RepoFacts.CurrentBranch(root) ?? "main";

        // The same checks `check` runs, and for the same reason — only here they gate a pull
        // request rather than informing one.
        if (!settings.Force)
        {
            var problems = CheckCommand.Run(root, quiet: true).Failures;
            if (problems > 0)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]{problems} problem(s) — run `strideassetstore check` and fix them, or pass --force.[/]");
                return 1;
            }
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]Asset:[/] {manifest.Name} ({manifest.Id})");
        AnsiConsole.MarkupLineInterpolated($"[grey]Repo:[/] {repo} [grey]following[/] {followed}");

        var entry = new RegistryEntry
        {
            Id = manifest.Id,
            Repo = repo,
            Latest = new RefPointer { Ref = followed },
        };

        return await RegistryFlow.RunAsync(settings, $"Submitting {manifest.Id}", p => p.PublishAsync(entry, cancellation));
    }
}

internal sealed class CertifySettings : RegistrySettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id to certify a version of.")]
    public string Asset { get; init; } = "";

    [CommandOption("--version <VERSION>")]
    [Description("Version label, e.g. 1.0.0. Required.")]
    public string? Version { get; init; }

    [CommandOption("--commit <SHA>")]
    [Description("The immutable commit being certified. Required.")]
    public string? Commit { get; init; }

    [CommandOption("--tag <TAG>")]
    [Description("Tag the commit was released as, when there is one.")]
    public string? Tag { get; init; }
}

/// <summary>
/// Certifies a version: pins a reviewed commit as immutable, the way the app's Certify form does.
/// </summary>
/// <remarks>
/// The commit is not derived from the tag on purpose. Certification is the store's one promise
/// that a specific tree was reviewed, and a tag can be moved afterwards — so the caller states the
/// commit, and the registry records that exact sha.
/// </remarks>
internal sealed class CertifyCommand : AsyncCommand<CertifySettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, CertifySettings settings, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(settings.Version) || string.IsNullOrWhiteSpace(settings.Commit))
        {
            AnsiConsole.MarkupLine("[red]--version and --commit are both required.[/]");
            return 1;
        }

        if (settings.Commit.Trim().Length != 40 || !settings.Commit.Trim().All(Uri.IsHexDigit))
        {
            AnsiConsole.MarkupLine("[red]--commit must be a full 40-character sha[/] — an abbreviated one can become ambiguous.");
            return 1;
        }

        var version = new CertifiedVersion
        {
            Version = settings.Version.Trim(),
            Tag = string.IsNullOrWhiteSpace(settings.Tag) ? null : settings.Tag.Trim(),
            Commit = settings.Commit.Trim().ToLowerInvariant(),
        };

        return await RegistryFlow.RunAsync(
            settings,
            $"Certifying {settings.Asset} {version.Version}",
            p => p.CertifyAsync(settings.Asset.Trim(), version, cancellation));
    }
}

internal sealed class DeprecateSettings : RegistrySettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id to mark deprecated.")]
    public string Asset { get; init; } = "";

    [CommandOption("--reason <TEXT>")]
    [Description("Why it is deprecated — shown on the asset page.")]
    public string? Reason { get; init; }

    [CommandOption("--successor <ID>")]
    [Description("Asset id to use instead, when there is one.")]
    public string? Successor { get; init; }
}

/// <summary>Marks an asset deprecated: it stays installable, and says it shouldn't be chosen.</summary>
internal sealed class DeprecateCommand : AsyncCommand<DeprecateSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, DeprecateSettings settings, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(settings.Reason))
        {
            // Not required by the registry, but a deprecation with no reason tells a reader nothing
            // and cannot be argued with.
            AnsiConsole.MarkupLine("[yellow]No --reason given[/] — the asset page will say it is deprecated without saying why.");
        }

        return await RegistryFlow.RunAsync(
            settings,
            $"Deprecating {settings.Asset}",
            p => p.DeprecateAsync(settings.Asset.Trim(), settings.Reason, settings.Successor, cancellation));
    }
}

internal sealed class UnpublishSettings : RegistrySettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id to take out of the registry.")]
    public string Asset { get; init; } = "";

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation (for scripts).")]
    public bool Yes { get; init; }
}

/// <summary>
/// Takes an asset out of the registry entirely — the entry file is deleted by the pull request.
/// </summary>
/// <remarks>
/// Confirmed rather than merely typed, and pointed at `deprecate` first: removal breaks every
/// project that installed the asset by id, while deprecation leaves them working and tells new
/// readers to look elsewhere. That is almost always the intended one.
/// </remarks>
internal sealed class UnpublishCommand : AsyncCommand<UnpublishSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, UnpublishSettings settings, CancellationToken cancellation)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[yellow]Removing {settings.Asset} from the registry[/] breaks `add` and `update` for everyone using it.");
        AnsiConsole.MarkupLine("[grey]`deprecate` keeps it installable and warns instead — consider that first.[/]");

        if (!CliOutput.Confirm($"Open a pull request removing {settings.Asset}?", settings.Yes))
        {
            AnsiConsole.MarkupLine("[grey]Nothing was submitted.[/]");
            return 1;
        }

        return await RegistryFlow.RunAsync(
            settings,
            $"Removing {settings.Asset}",
            p => p.RemoveAsync(settings.Asset.Trim(), cancellation));
    }
}

/// <summary>The three facts `publish` reads from a checkout instead of asking for them.</summary>
internal static class RepoFacts
{
    public static AssetManifest? Manifest(string root)
    {
        try
        {
            var path = System.IO.Path.Combine(root, "AssetData", "manifest.json");
            return File.Exists(path)
                ? Core.Serialization.StrideAssetStoreJson.Deserialize<AssetManifest>(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null; // unreadable or invalid — `check` explains it properly
        }
    }

    /// <summary>The origin remote as an https URL, with the .git suffix and any credentials dropped.</summary>
    public static string? OriginUrl(string root)
    {
        var result = ProcessRunner.RunAsync("git", ["-C", root, "remote", "get-url", "origin"], root)
            .GetAwaiter().GetResult();
        if (!result.Ok)
        {
            return null;
        }

        var url = result.StdOut.Trim();
        if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://github.com/" + url["git@github.com:".Length..];
        }

        return url.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? url[..^4] : url;
    }

    public static string? CurrentBranch(string root)
    {
        var result = ProcessRunner.RunAsync("git", ["-C", root, "rev-parse", "--abbrev-ref", "HEAD"], root)
            .GetAwaiter().GetResult();
        return result.Ok && result.StdOut.Trim() is { Length: > 0 } branch and not "HEAD" ? branch : null;
    }
}
