// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Commands;

internal sealed class InfoSettings : CatalogSettings
{
    [CommandArgument(0, "<ASSET>")]
    [Description("Asset id, or enough of it to be unambiguous.")]
    public string Asset { get; init; } = "";

    [CommandOption("--versions")]
    [Description("Print only the published versions, one per line (for scripts).")]
    public bool VersionsOnly { get; init; }
}

/// <summary>
/// Everything the catalog knows about one asset — what <c>search</c> has no room for.
/// </summary>
/// <remarks>
/// The gap this fills is <c>--version</c>: nothing told you what to pass. The published versions
/// were only ever visible by asking for one that doesn't exist and reading the error, or by opening
/// the website. They live in the index next to everything else here.
/// </remarks>
internal sealed class InfoCommand : AsyncCommand<InfoSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, InfoSettings settings, CancellationToken cancellation)
    {
        var (index, fromCache) = await CatalogAccess.LoadAsync(settings.IndexUrl, settings.Offline, cancellation);
        var asset = CatalogAccess.Resolve(index, settings.Asset);

        // The scripting mode prints versions and nothing else — no catalog notice, no header, so
        // `for v in $(strideassetstore info grass --versions)` gets versions and not prose.
        if (settings.VersionsOnly)
        {
            foreach (var version in Versions(asset))
            {
                AnsiConsole.WriteLine(version.Version);
            }

            return 0;
        }

        CliOutput.NoteCatalogSource(fromCache, index);

        AnsiConsole.MarkupLineInterpolated($"[bold]{asset.Manifest.Name}[/] [grey]{asset.Id}[/]");
        AnsiConsole.MarkupLineInterpolated($"{asset.Manifest.Description}");
        AnsiConsole.WriteLine();

        if (asset.Deprecated is { } deprecated)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]⚠ Deprecated.[/] {deprecated.Reason ?? "No reason given."}");
            if (deprecated.Successor is { } successor)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]Use instead:[/] {successor}");
            }

            AnsiConsole.WriteLine();
        }

        Fact("Repo", asset.Repo);
        Fact("Category", asset.Manifest.Category);
        Fact("License", asset.Manifest.License);
        if (asset.Manifest.Authors.Count > 0)
        {
            Fact("Authors", string.Join(", ", asset.Manifest.Authors.Select(a => a.Name)));
        }

        Fact("Stride", asset.Latest.DetectedStrideVersion ?? "not detected");
        Fact("Framework", asset.Latest.TargetFramework);
        Fact("Size", ByteSize.Format(asset.Latest.SizeBytes));
        Fact("Stars", asset.Stars is { } stars ? $"{stars}{(asset.Forks is { } forks ? $" · {forks} fork(s)" : "")}" : null);
        FactMarkup("Follows", $"{Markup.Escape(asset.Latest.Ref)} [grey]({Short(asset.Latest.Commit)}"
            + $"{(asset.Latest.CommittedAt is { } at ? $", {at[..Math.Min(10, at.Length)]}" : "")})[/]");

        // How `add` will install it by default, which decides whether --version even applies:
        // a NuGet install takes the package's version, not a git tag.
        FactMarkup("Default install", asset.Manifest.Nuget is { } nuget
                && string.Equals(asset.Manifest.DefaultImport, "nuget", StringComparison.OrdinalIgnoreCase)
            ? $"NuGet package [grey]{Markup.Escape(nuget.PackageId)}[/] (--source clones it instead)"
            : "clone of the source" + (asset.Manifest.Nuget is not null ? " (--nuget takes the package instead)" : ""));

        if (asset.Manifest.Dependencies.Count > 0)
        {
            Fact("Store dependencies", string.Join(", ", asset.Manifest.Dependencies));
        }

        if (asset.Manifest.Tags.Count > 0)
        {
            Fact("Tags", string.Join(", ", asset.Manifest.Tags));
        }

        if (!string.Equals(asset.ValidationStatus, "ok", StringComparison.OrdinalIgnoreCase))
        {
            FactMarkup("Validation",
                $"[yellow]{Markup.Escape(asset.ValidationStatus)}[/] {Markup.Escape(string.Join("; ", asset.ValidationMessages))}");
        }

        AnsiConsole.WriteLine();

        var versions = Versions(asset);
        if (versions.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[grey]No published version — `add {asset.Id}` installs its followed branch ({asset.Latest.Ref}).[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Version");
        table.AddColumn("Tag");
        table.AddColumn("Commit");
        table.AddColumn("Certified");

        foreach (var version in versions)
        {
            table.AddRow(
                Markup.Escape(version.Version),
                Markup.Escape(version.Tag ?? "-"),
                Markup.Escape(Short(version.Commit)),
                version.CertifiedAt is { } certifiedAt
                    ? $"[green]{Markup.Escape(certifiedAt[..Math.Min(10, certifiedAt.Length)])}[/]"
                    : version.Certified ? "[green]yes[/]" : "");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Install one with:[/] strideassetstore add {asset.Id} --version {versions[0].Version}");
        return 0;
    }

    /// <summary>
    /// The published versions, newest first. Tags and certifications are two separate lists in the
    /// index and neither contains the other — a version can be certified from a commit its author
    /// never tagged, and most tags are never certified — so they are merged on the version label.
    /// </summary>
    private static IReadOnlyList<PublishedVersion> Versions(IndexedAsset asset)
    {
        var merged = new Dictionary<string, PublishedVersion>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in asset.Versions)
        {
            merged[tag.Version] = new PublishedVersion(tag.Version, tag.Tag, tag.Commit, false, null);
        }

        foreach (var certified in asset.Certified)
        {
            merged[certified.Version] = new PublishedVersion(
                certified.Version,
                certified.Tag ?? merged.GetValueOrDefault(certified.Version)?.Tag,
                certified.Commit,
                true,
                certified.CertifiedAt);
        }

        // Newest first, by version number where they parse — a tag like "beta" sorts last rather
        // than being dropped: it is installable, so it belongs in the list.
        return merged.Values
            .OrderByDescending(v => Version.TryParse(v.Version.TrimStart('v', 'V'), out var parsed) ? parsed : null)
            .ThenByDescending(v => v.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>One "label: value" line, with the value escaped — it comes from the registry.</summary>
    private static void Fact(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]{label}:[/] {value}");
        }
    }

    /// <summary>Same, for the few values that carry markup of their own and escape it themselves.</summary>
    private static void FactMarkup(string label, string value) =>
        AnsiConsole.MarkupLine($"[grey]{label}:[/] {value}");

    private static string Short(string commit) => commit.Length >= 7 ? commit[..7] : commit;

    private sealed record PublishedVersion(
        string Version, string? Tag, string Commit, bool Certified, string? CertifiedAt);
}
