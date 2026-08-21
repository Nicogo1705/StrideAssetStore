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
}

/// <summary>Installs the desktop app, or updates the copy this tool installed.</summary>
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
            return StartIfAsked(settings.Start);
        }

        if (release.DownloadUrl is null)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]Release v{release.Version} has no build for this machine.[/] It may still be uploading — try again shortly.");
            return 1;
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
        AnsiConsole.MarkupLine("[grey]Start it with: strideassetstore app start[/]");
        return StartIfAsked(settings.Start);
    }

    private static int StartIfAsked(bool start) => start ? AppStartCommand.Start() : 0;
}

/// <summary>Reports what is installed against what has been released.</summary>
internal sealed class AppStatusCommand : AsyncCommand<AppSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, AppSettings settings, CancellationToken cancellation)
    {
        var installed = DesktopAppInstaller.InstalledVersion();
        if (installed is null)
        {
            AnsiConsole.MarkupLine("[grey]Not installed by this tool.[/] Install it with: strideassetstore app install");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Installed:[/] v{installed} ({DesktopAppInstaller.InstallRoot})");
        }

        try
        {
            var release = await new DesktopAppInstaller().FetchLatestAsync(settings.Repo, cancellation);
            AnsiConsole.MarkupLineInterpolated($"[grey]Latest:[/] v{release.Version}");
            if (installed is not null && installed != release.Version)
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
internal sealed class AppStartCommand : Command<AppSettings>
{
    protected override int Execute(CommandContext context, AppSettings settings, CancellationToken cancellation) => Start();

    public static int Start()
    {
        if (DesktopAppInstaller.ExecutablePath() is not { } exe)
        {
            AnsiConsole.MarkupLine("[red]The desktop app isn't installed.[/] Run: strideassetstore app install");
            return 1;
        }

        try
        {
            // Detached on purpose: the app serves until the user quits it, long after this command returns.
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Couldn't start it:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]Started.[/] It opens http://localhost:5111 in your browser.");
        return 0;
    }
}

/// <summary>Opens the storefront in a browser — the online one, no install required.</summary>
internal sealed class AppOpenCommand : Command<AppSettings>
{
    protected override int Execute(CommandContext context, AppSettings settings, CancellationToken cancellation)
    {
        const string site = "https://nicogo1705.github.io/StrideAssetStore/";
        if (!DesktopShell.OpenUrl(site))
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Couldn't open a browser.[/] The store is at {site}");
            return 1;
        }

        return 0;
    }
}
