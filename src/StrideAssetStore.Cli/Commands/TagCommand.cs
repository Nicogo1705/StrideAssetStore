// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Authoring;

namespace StrideAssetStore.Cli.Commands;

internal sealed class TagSettings : CommandSettings
{
    [CommandArgument(0, "[VERSION]")]
    [Description("Version to release, e.g. 1.2.0. Defaults to the next patch after the latest tag.")]
    public string? Version { get; init; }

    [CommandOption("-C|--repo-path <PATH>")]
    [Description("The asset repository. Defaults to the current directory.")]
    public string? Path { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation (for scripts).")]
    public bool Yes { get; init; }
}

/// <summary>
/// Tags a release of your asset and pushes it — the "My assets" tag button, as a command.
/// </summary>
/// <remarks>
/// `git tag` would do the mechanical part. What this adds is the two things that make a tag mean
/// something to the store: it refuses to tag a commit the world cannot fetch, and it names the
/// version the way the catalog reads it. A tag pushed onto an unpushed commit publishes a version
/// that resolves to nothing for everybody except its author, and nothing complains until someone
/// tries to install it.
/// </remarks>
internal sealed class TagCommand : Command<TagSettings>
{
    protected override int Execute(CommandContext context, TagSettings settings, CancellationToken cancellation)
    {
        var root = System.IO.Path.GetFullPath(
            string.IsNullOrWhiteSpace(settings.Path) ? Environment.CurrentDirectory : settings.Path);

        var repo = new AuthorRepoService().Inspect(root);
        if (repo.Branch == "?")
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{root} is not a git repository.[/]");
            return 1;
        }

        if (repo.Id is null)
        {
            // Not fatal: a tag is a git thing. But the store reads tags of registered assets, so a
            // repository with no manifest is almost certainly not the one you meant to release.
            AnsiConsole.MarkupLine("[yellow]No AssetData/manifest.json here[/] — is this the asset repository?");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Asset:[/] {repo.Name} ({repo.Id})");
        }

        // Built as markup rather than interpolated: the line mixes styling with values from git,
        // and a branch name may contain a bracket.
        AnsiConsole.MarkupLine(
            $"[grey]Branch:[/] {Markup.Escape(repo.Branch)} [grey]at[/] {Short(repo.HeadCommit)}"
            + (repo.LatestTag is { } latest ? $" [grey]· latest tag[/] {Markup.Escape(latest)}" : " [grey]· never tagged[/]"));

        if (repo.Dirty > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{repo.Dirty} uncommitted change(s).[/] Commit them first — the tag would point at a tree that isn't yours.");
            return 1;
        }

        if (repo.Ahead > 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]{repo.Ahead} commit(s) not pushed.[/] Push them first: a tag on a commit nobody can fetch installs as nothing.");
            return 1;
        }

        if (!repo.HasUpstream)
        {
            AnsiConsole.MarkupLine("[red]This branch has no upstream[/] — push it once (git push -u origin HEAD) so the tag has somewhere to go.");
            return 1;
        }

        var tag = Normalize(settings.Version) ?? AuthorRepoService.SuggestNextTag(repo.LatestTag);
        if (repo.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{tag} already exists.[/] Pick another version — a published tag is never moved.");
            return 1;
        }

        AnsiConsole.WriteLine();
        if (!CliOutput.Confirm($"Tag {Short(repo.HeadCommit)} as {tag} and push it?", settings.Yes))
        {
            AnsiConsole.MarkupLine("[grey]Nothing was tagged.[/]");
            return 1;
        }

        var result = new AuthorRepoService().PushTag(root, tag);
        foreach (var message in result.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        if (!result.Success)
        {
            return 1;
        }

        if (repo.Id is { } id)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]Install it with:[/] strideassetstore add {id} --version {tag.TrimStart('v')}");
        }

        return 0;
    }

    /// <summary>Accepts 1.2.0 or v1.2.0 — the catalog strips the v, so both mean the same release.</summary>
    private static string? Normalize(string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? null
            : version.Trim().StartsWith('v') || version.Trim().StartsWith('V')
                ? "v" + version.Trim()[1..]
                : "v" + version.Trim();

    private static string Short(string commit) => commit.Length >= 7 ? commit[..7] : commit;
}
