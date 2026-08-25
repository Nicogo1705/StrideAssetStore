// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Releases;
using StrideAssetStore.Core.Local.Shell;

namespace StrideAssetStore.Cli.Commands;

/// <summary>
/// Brings this machine's store up to date: the tool itself, then the desktop app.
/// </summary>
/// <remarks>
/// The two halves used to be two commands in two places — <c>dotnet tool update</c> for one,
/// <c>strideassetstore app update</c> for the other — and the first one is not even this tool's
/// own vocabulary. The app half runs here, with its progress bar. The tool half cannot: dotnet
/// would be replacing the files of the process asking for it, so it is handed to a terminal that
/// starts once this process is gone. Installed assets are a different thing entirely and are never
/// touched here — `update` is the command for those.
/// </remarks>
internal sealed class UpgradeCommand : AsyncCommand<AppSettings>
{
    internal const string ToolUpdateCommand = "dotnet tool update -g StrideAssetStore";

    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppSettings settings, CancellationToken cancellation)
    {
        // Both questions asked before anything is done, so the plan is visible up front rather than
        // arriving as a surprise window halfway through.
        var toolLatest = await ToolUpdateNotice.FetchLatestAsync();
        var toolCurrent = ToolUpdateNotice.Current();
        var toolBehind = toolLatest is not null && toolCurrent is not null && ToolUpdateNotice.IsNewer(toolLatest, toolCurrent);

        var installedApp = DesktopAppInstaller.InstalledVersion();
        DesktopRelease? release = null;
        try
        {
            release = await new DesktopAppInstaller().FetchLatestAsync(settings.Repo, cancellation);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Couldn't reach the release API:[/] {ex.Message}");
        }

        var appBehind = installedApp is not null && release is not null && installedApp != release.Version;

        // MarkupLine, not the interpolated overload: Describe returns markup on purpose, and the
        // interpolated one escapes its holes — which is what printed "[grey]" as text.
        AnsiConsole.MarkupLine($"[grey]Tool:[/] {Describe(toolCurrent?.ToString(3), toolLatest, toolBehind)}");
        AnsiConsole.MarkupLine($"[grey]App:[/] {(installedApp is null
            ? "not installed by this tool — run [bold]strideassetstore app install[/]"
            : Describe(installedApp, release?.Version, appBehind))}");
        AnsiConsole.WriteLine();

        if (!toolBehind && !appBehind)
        {
            // "Up to date" is a claim about both halves; a half that couldn't be checked has not
            // earned it. Offline, this used to answer a 404 with a green tick.
            var unchecked_ = toolLatest is null || (installedApp is not null && release is null);
            AnsiConsole.MarkupLine(unchecked_
                ? "[yellow]Nothing to update in what could be checked.[/] Try again when the network is back."
                : "[green]✓ Everything is up to date.[/]");
            return unchecked_ ? 1 : 0;
        }

        var failed = 0;
        if (appBehind)
        {
            // Before the tool, not after: this is the half that can run here, and running it now
            // means its output is on the screen the user is already looking at. Which version it
            // installs comes from the release API, not from this tool's code, so the newer tool
            // would have fetched exactly the same build.
            AnsiConsole.MarkupLine("[bold]Updating the app…[/]");
            failed += await AppInstallCommand.RunAsync(new AppInstallSettings { Repo = settings.Repo }, cancellation);
            AnsiConsole.WriteLine();
        }

        if (toolBehind)
        {
            failed += HandOverToolUpdate(toolLatest!) ? 0 : 1;
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>One version line as markup. Versions are escaped: they reach us from an API.</summary>
    private static string Describe(string? current, string? latest, bool behind) => (current, latest) switch
    {
        (null, _) => "[grey]a local build — nothing to compare against[/]",
        (_, null) => $"{Markup.Escape(current)} [grey](couldn't check for a newer one)[/]",
        _ when behind => $"{Markup.Escape(current)} [yellow]→ {Markup.Escape(latest)}[/]",
        _ => $"{Markup.Escape(current)} [green]up to date[/]",
    };

    /// <summary>
    /// Runs the tool update in a terminal of its own, after this process ends. A global tool cannot
    /// update itself while it runs — on Windows dotnet cannot even delete the locked executable —
    /// so the window waits a few seconds for this process to be gone, and stays open afterwards so
    /// its result is readable. When no terminal opens (a headless Linux box, a Linux desktop
    /// without one of the usual emulators), the command is printed instead of silently not running.
    /// </summary>
    private static bool HandOverToolUpdate(string latest)
    {
        var wait = OperatingSystem.IsWindows() ? "timeout /t 3 /nobreak >nul && " : "sleep 3; ";
        if (DesktopShell.OpenTerminal(wait + ToolUpdateCommand))
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[bold]Updating the tool to v{latest}[/] in a separate window — it starts once this command exits.");
            return true;
        }

        AnsiConsole.MarkupLineInterpolated($"[yellow]A newer tool is available (v{latest}), and no terminal could be opened.[/]");
        AnsiConsole.MarkupLineInterpolated($"[grey]Run it yourself:[/] [bold]{ToolUpdateCommand}[/]");
        return false;
    }
}
