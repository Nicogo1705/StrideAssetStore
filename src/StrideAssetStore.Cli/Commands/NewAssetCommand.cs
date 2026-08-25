// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Core.Local.Authoring;

namespace StrideAssetStore.Cli.Commands;

internal sealed class NewAssetSettings : CommandSettings
{
    [CommandArgument(0, "<REPO-NAME>")]
    [Description("Repository name to create on GitHub, and the name every template file is renamed to.")]
    public string RepoName { get; init; } = "";

    [CommandOption("--name <NAME>")]
    [Description("Display name shown in the store. Defaults to the repository name.")]
    public string? DisplayName { get; init; }

    [CommandOption("--id <ID>")]
    [Description("Store id, e.g. com.you.cool-thing. Defaults to com.<your-github-login>.<repo-name>.")]
    public string? Id { get; init; }

    [CommandOption("--category <NAME>")]
    [Description("Store category (Scripts, Shaders, Textures, …).")]
    [DefaultValue("Scripts")]
    public string Category { get; init; } = "Scripts";

    [CommandOption("--license <SPDX>")]
    [Description("License identifier.")]
    [DefaultValue("MIT")]
    public string License { get; init; } = "MIT";

    [CommandOption("--description <TEXT>")]
    [Description("One line describing what the asset does.")]
    public string? Description { get; init; }

    [CommandOption("--tags <LIST>")]
    [Description("Comma-separated tags.")]
    public string? Tags { get; init; }

    [CommandOption("-d|--dir <PATH>")]
    [Description("Where to clone it. Defaults to the current directory.")]
    public string? Directory { get; init; }

    [CommandOption("--private")]
    [Description("Create the repository private. The store can only list public ones.")]
    public bool Private { get; init; }

    [CommandOption("--template <OWNER/REPO>")]
    [Description("Template repository to instantiate. Defaults to the store's own.")]
    [DefaultValue(CatalogDefaults.TemplateRepo)]
    public string Template { get; init; } = CatalogDefaults.TemplateRepo;
}

/// <summary>
/// Creates a new asset repository from the store's template — the desktop app's "New asset" wizard,
/// as a command.
/// </summary>
/// <remarks>
/// The same <see cref="AssetScaffolder"/> the wizard uses, so both produce a repository that is
/// renamed, manifested and pushed identically. What the wizard collects in a form arrives here as
/// options, with the two that can be guessed — display name and id — derived from the repository
/// name and the signed-in GitHub login rather than demanded.
/// </remarks>
internal sealed class NewAssetCommand : AsyncCommand<NewAssetSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, NewAssetSettings settings, CancellationToken cancellation)
    {
        if (!CliOutputGuards.RequireGh())
        {
            return 1;
        }

        var repoName = settings.RepoName.Trim();
        var displayName = Blank(settings.DisplayName) ? Humanize(repoName) : settings.DisplayName!.Trim();
        var directory = Path.GetFullPath(Blank(settings.Directory) ? Environment.CurrentDirectory : settings.Directory!);

        var id = Blank(settings.Id) ? await DeriveIdAsync(repoName, cancellation) : settings.Id!.Trim();
        if (id is null)
        {
            AnsiConsole.MarkupLine("[red]Couldn't read your GitHub login[/] to build an id. Sign in with `gh auth login`, or pass --id.");
            return 1;
        }

        // The same shape the storefront enforces: reverse-DNS, lowercase, at least one dot. Checked
        // here rather than after the repository exists on GitHub — the wizard's form does the same.
        if (!Regex.IsMatch(id, "^[a-z0-9]+(\\.[a-z0-9-]+)+$"))
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]'{id}' is not a valid store id.[/] Use reverse-DNS, lowercase: com.you.cool-thing");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]Creating[/] {repoName} [grey]as[/] {id} [grey]in[/] {directory}");

        var result = await new AssetScaffolder(settings.Template).CreateAsync(
            new ScaffoldRequest(
                repoName, displayName, id, settings.Category, settings.License,
                settings.Description?.Trim() ?? $"{displayName} for Stride.",
                settings.Tags ?? "", directory, settings.Private),
            cancellation);

        foreach (var message in result.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        if (!result.Success)
        {
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[green]✓ {result.RepoUrl}[/]");
        AnsiConsole.MarkupLineInterpolated($"[grey]Cloned to[/] {result.CloneDir}");
        AnsiConsole.MarkupLine("[grey]Next: build it, replace the placeholder media, then[/] [bold]strideassetstore check[/]");
        return 0;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>com.&lt;login&gt;.&lt;repo-name&gt;, the id the wizard proposes from the same two facts.</summary>
    private static async Task<string?> DeriveIdAsync(string repoName, CancellationToken cancellation)
    {
        var login = await Gh.LoginAsync(cancellation);
        if (login is null)
        {
            return null;
        }

        var slug = Regex.Replace(repoName, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
        slug = Regex.Replace(slug, "[^a-z0-9-]+", "-").Trim('-');
        return $"com.{Regex.Replace(login.ToLowerInvariant(), "[^a-z0-9]+", "")}.{slug}";
    }

    /// <summary>"StrideGrassSystem" → "Stride Grass System": a display name worth defaulting to.</summary>
    private static string Humanize(string repoName) =>
        Regex.Replace(repoName.Replace('-', ' ').Replace('_', ' '), "([a-z0-9])([A-Z])", "$1 $2").Trim();
}
