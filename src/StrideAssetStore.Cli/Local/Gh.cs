// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Spectre.Console;
using StrideAssetStore.Core.Local.Shell;

namespace StrideAssetStore.Cli.Commands;

/// <summary>The GitHub CLI, for the commands that create or read things on GitHub.</summary>
internal static class Gh
{
    /// <summary>The signed-in GitHub login, or null when gh is missing or signed out.</summary>
    public static async Task<string?> LoginAsync(CancellationToken cancellation)
    {
        var result = await ProcessRunner.RunAsync("gh", ["api", "user", "-q", ".login"], cancellation: cancellation);
        return result.Ok && result.StdOut.Trim() is { Length: > 0 } login ? login : null;
    }
}

/// <summary>Preconditions worth failing on early, with an answer rather than a stack trace.</summary>
internal static class CliOutputGuards
{
    /// <summary>
    /// Fails clearly when the GitHub CLI is missing. Creating a repository needs it, and gh's own
    /// "not found" arrives from deep inside a step that already half-ran.
    /// </summary>
    public static bool RequireGh()
    {
        if (DesktopShell.CommandExists("gh"))
        {
            return true;
        }

        AnsiConsole.MarkupLine("[red]The GitHub CLI (gh) was not found on PATH.[/] Install it from https://cli.github.com and run `gh auth login`.");
        return false;
    }
}
