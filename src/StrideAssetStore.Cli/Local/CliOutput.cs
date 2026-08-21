// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Spectre.Console;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Local;

/// <summary>Shared console output for the consumer-facing commands.</summary>
internal static class CliOutput
{
    /// <summary>
    /// Says when the catalog came from the on-disk snapshot rather than the network. Acting on a
    /// week-old view of the registry is fine; not knowing you are is not.
    /// </summary>
    public static void NoteCatalogSource(bool fromCache, IndexLock index)
    {
        if (fromCache)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]Offline:[/] using the cached catalog from {index.GeneratedAt}.");
        }
    }

    /// <summary>Prints an installer result and maps it to a process exit code.</summary>
    public static int Report(InstallResult result)
    {
        foreach (var message in result.Messages)
        {
            AnsiConsole.MarkupLineInterpolated($"{message}");
        }

        return result.Success ? 0 : 1;
    }

    /// <summary>Colours a status coming from the installer's analysis.</summary>
    public static string StatusMarkup(string status) => status switch
    {
        "up-to-date" => "[green]up-to-date[/]",
        "outdated" => "[yellow]outdated[/]",
        "missing" => "[yellow]missing[/]",
        "broken" => "[red]broken[/]",
        _ => $"[grey]{Markup.Escape(status)}[/]",
    };

    /// <summary>Asks for confirmation unless the caller already answered (scripts, CI).</summary>
    public static bool Confirm(string question, bool yes)
    {
        if (yes)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            AnsiConsole.MarkupLine("[red]Refusing to guess:[/] this needs a confirmation and stdin isn't a terminal. Pass --yes.");
            return false;
        }

        return AnsiConsole.Confirm(question);
    }

    /// <summary>
    /// Fails early and clearly when git is missing. Everything that clones needs it, and the error
    /// from deep inside an install is much harder to act on.
    /// </summary>
    public static bool RequireGit()
    {
        if (new Core.Local.Git.GitClient().IsAvailable())
        {
            return true;
        }

        AnsiConsole.MarkupLine("[red]git was not found on PATH.[/] Install it from https://git-scm.com and try again.");
        return false;
    }
}
