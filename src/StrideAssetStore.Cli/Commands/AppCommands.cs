// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Core.Local.Releases;
using StrideAssetStore.Core.Local.Shell;

namespace StrideAssetStore.Cli.Commands;

internal class AppSettings : CommandSettings
{
    [CommandOption("--repo <URL>")]
    [Description("Repository to take releases from. Defaults to the official one.")]
    [DefaultValue("https://github.com/Nicogo1705/StrideAssetStore")]
    public string Repo { get; init; } = "https://github.com/Nicogo1705/StrideAssetStore";
}

internal sealed class AppInstallSettings : AppSettings
{
    [CommandOption("--force")]
    [Description("Reinstall even when the newest version is already installed.")]
    public bool Force { get; init; }

    [CommandOption("--start")]
    [Description("Start the app once it is installed.")]
    public bool Start { get; init; }

    [CommandOption("--no-stop")]
    [Description("Don't stop a running app first. The install will fail if its files are locked.")]
    public bool NoStop { get; init; }
}

/// <summary>
/// Installs the desktop app, or updates the copy this tool installed. `install` and `update` are
/// the same operation — what matters is what is already on disk, not which word you typed.
/// </summary>
internal sealed class AppInstallCommand : AsyncCommand<AppInstallSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppInstallSettings settings, CancellationToken cancellation)
    {
        var installer = new DesktopAppInstaller();
        var installed = DesktopAppInstaller.InstalledVersion();

        DesktopRelease release;
        try
        {
            release = await installer.FetchLatestAsync(settings.Repo, cancellation);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Couldn't reach the release API:[/] {ex.Message}");
            return 1;
        }

        if (!settings.Force && installed == release.Version)
        {
            AnsiConsole.MarkupLineInterpolated($"[green]Already on v{release.Version}.[/] Use --force to reinstall.");
            return settings.Start ? await AppStartCommand.StartAsync(cancellation) : 0;
        }

        if (release.DownloadUrl is null)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]Release v{release.Version} has no build for this machine.[/] It may still be uploading — try again shortly.");
            return 1;
        }

        // A running app keeps its own executable locked on Windows, so extracting over it fails
        // halfway and leaves a broken install. Stop it first, and put it back afterwards.
        var wasRunning = (await RunningApp.PingAsync(cancellation)).Running;
        if (wasRunning && !settings.NoStop)
        {
            AnsiConsole.MarkupLine("[grey]The app is running — stopping it first.[/]");
            if (!await RunningApp.StopAsync(TimeSpan.FromSeconds(20), cancellation))
            {
                AnsiConsole.MarkupLine("[red]It didn't stop.[/] Close it and run this again, or pass --no-stop to try anyway.");
                return 1;
            }
        }

        if (installed is null)
        {
            AnsiConsole.MarkupLineInterpolated($"Installing [bold]v{release.Version}[/]…");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"Updating [bold]v{installed}[/] → [bold]v{release.Version}[/]…");
        }

        try
        {
            await AnsiConsole.Progress()
                .AutoClear(true)
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Downloading", maxValue: 100);
                    await installer.InstallAsync(release, new Progress<double>(p => task.Value = p), cancellation);
                    task.Value = 100;
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Install failed:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[green]✓ v{release.Version} installed[/] in {DesktopAppInstaller.InstallRoot}");

        // Restarting is the polite thing when we were the ones who stopped it.
        if (settings.Start || wasRunning)
        {
            return await AppStartCommand.StartAsync(cancellation);
        }

        AnsiConsole.MarkupLine("[grey]Start it with: strideassetstore app start[/]");
        return 0;
    }
}

/// <summary>Reports what is installed, what is running, and what has been released.</summary>
internal sealed class AppStatusCommand : AsyncCommand<AppSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppSettings settings, CancellationToken cancellation)
    {
        var installed = DesktopAppInstaller.InstalledVersion();
        if (installed is null)
        {
            AnsiConsole.MarkupLine("[grey]Installed:[/] not by this tool — [grey]strideassetstore app install[/]");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Installed:[/] v{installed} ({DesktopAppInstaller.InstallRoot})");
        }

        var ping = await RunningApp.PingAsync(cancellation);
        if (ping.Running)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[green]Running:[/] yes{(ping.Version is { } v ? $" (v{v})" : "")} — http://localhost:{RunningApp.Port}");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Running:[/] no");
        }

        try
        {
            var release = await new DesktopAppInstaller().FetchLatestAsync(settings.Repo, cancellation);
            AnsiConsole.MarkupLineInterpolated($"[grey]Latest:[/] v{release.Version}");

            // The running copy may be one the user unzipped themselves, so judge on either version.
            // Compare as versions, not strings: a local build ahead of the feed is not "outdated",
            // and an unparseable version is not a reason to nag.
            var current = installed ?? ping.Version;
            if (Version.TryParse(current, out var mine)
                && Version.TryParse(release.Version, out var latest)
                && latest > mine)
            {
                AnsiConsole.MarkupLine("[yellow]An update is available.[/] Run: strideassetstore app update");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Couldn't check for updates:[/] {ex.Message}");
        }

        return 0;
    }
}

/// <summary>Starts the installed desktop app.</summary>
internal sealed class AppStartCommand : AsyncCommand<AppSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppSettings settings, CancellationToken cancellation) =>
        await StartAsync(cancellation);

    public static async Task<int> StartAsync(CancellationToken cancellation)
    {
        // Starting a second copy would just fail to bind the port; say so instead.
        if ((await RunningApp.PingAsync(cancellation)).Running)
        {
            AnsiConsole.MarkupLineInterpolated($"[green]Already running[/] — http://localhost:{RunningApp.Port}");
            return 0;
        }

        if (DesktopAppInstaller.ExecutablePath() is not { } exe)
        {
            AnsiConsole.MarkupLine("[red]The desktop app isn't installed.[/] Run: strideassetstore app install");
            return 1;
        }

        try
        {
            // Must be genuinely detached: the app serves until the user quits it, and a child that
            // inherits this process's stdout keeps the pipe open, so the shell that ran this command
            // never gets its prompt back (and a script hangs forever).
            var directory = Path.GetDirectoryName(exe)!;
            var start = OperatingSystem.IsWindows()
                // ShellExecute hands the launch to the OS: no inherited handles, no console.
                ? new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = directory }
                : new ProcessStartInfo("/bin/sh", $"-c \"nohup '{exe}' >/dev/null 2>&1 &\"")
                {
                    UseShellExecute = false,
                    WorkingDirectory = directory,
                };

            Process.Start(start);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Couldn't start it:[/] {ex.Message}");
            return 1;
        }

        // Report the port only once it answers, so "started" means started.
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500, cancellation);
            if ((await RunningApp.PingAsync(cancellation)).Running)
            {
                AnsiConsole.MarkupLineInterpolated($"[green]✓ Started[/] — http://localhost:{RunningApp.Port} (it opens your browser)");
                return 0;
            }
        }

        AnsiConsole.MarkupLine("[yellow]Started, but it hasn't answered yet.[/] Check its console window.");
        return 0;
    }
}

/// <summary>Stops the desktop app running on this machine.</summary>
internal sealed class AppStopCommand : AsyncCommand<AppSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppSettings settings, CancellationToken cancellation)
    {
        if (!(await RunningApp.PingAsync(cancellation)).Running)
        {
            AnsiConsole.MarkupLine("[grey]Not running.[/]");
            return 0;
        }

        if (await RunningApp.StopAsync(TimeSpan.FromSeconds(20), cancellation))
        {
            AnsiConsole.MarkupLine("[green]✓ Stopped.[/]");
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[red]It is still answering on port {RunningApp.Port}.[/] Quit it from its own window (⏻), or end the process.");
        return 1;
    }
}

/// <summary>Opens the storefront in a browser — the local app when it is up, the online one otherwise.</summary>
internal sealed class AppOpenCommand : AsyncCommand<AppSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppSettings settings, CancellationToken cancellation)
    {
        var running = (await RunningApp.PingAsync(cancellation)).Running;
        var url = running
            ? $"http://localhost:{RunningApp.Port}"
            : "https://nicogo1705.github.io/StrideAssetStore/";

        if (!DesktopShell.OpenUrl(url))
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Couldn't open a browser.[/] The store is at {url}");
            return 1;
        }

        return 0;
    }
}
