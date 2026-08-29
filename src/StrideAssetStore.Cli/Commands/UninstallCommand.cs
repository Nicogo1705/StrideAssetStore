// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Local.Releases;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Cli.Commands;

internal sealed class UninstallSettings : CommandSettings
{
    [CommandOption("--app")]
    [Description("Remove the desktop app installed by this tool (stops it first).")]
    public bool App { get; init; }

    [CommandOption("--cache")]
    [Description("Remove the downloaded assets and the offline catalog snapshot.")]
    public bool Cache { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Answer yes to the confirmation (for scripts and CI).")]
    public bool Yes { get; init; }
}

/// <summary>
/// Removes what this tool put on the machine: the desktop app, the downloaded assets, the settings.
/// </summary>
/// <remarks>
/// Everything lives under one folder, but "delete that folder" is not a safe instruction to give:
/// the app may be running and holding its own files, and the asset cache is what installed projects
/// reference — so this stops the app first, and says what will break before it deletes anything.
/// The tool itself is the one thing it cannot remove: dotnet would be deleting the files of the
/// process doing the asking, so that command is printed for the user to run afterwards.
/// </remarks>
internal sealed class UninstallCommand : AsyncCommand<UninstallSettings>
{
    private const string ToolCommand = "dotnet tool uninstall -g StrideAssetStore";

    protected override async Task<int> ExecuteAsync(
        CommandContext context, UninstallSettings settings, CancellationToken cancellation)
    {
        // No flag means all of it — the question someone asks by typing `uninstall` with nothing
        // else. The flags are there to remove one part and keep the rest.
        var everything = !settings.App && !settings.Cache;
        var targets = Targets(settings, everything).Where(t => Directory.Exists(t.Path) || File.Exists(t.Path)).ToList();

        if (targets.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing to remove — this machine has no store folder.[/]");
            PrintToolStep();
            return 0;
        }

        AnsiConsole.MarkupLine("[bold]This will delete:[/]");
        foreach (var target in targets)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"  [grey]{target.Path}[/] — {target.Label} ({ByteSize.Format(SizeOf(target.Path))})");
        }

        // Said before the confirmation, not after: an installed project points at the clone in the
        // cache, so deleting it breaks the build until `add` or `update` downloads it again.
        if (everything || settings.Cache)
        {
            AnsiConsole.MarkupLine(
                "[yellow]Projects that reference downloaded assets will stop building[/] until you install them again.");
        }

        if (!CliOutput.Confirm("Remove them?", settings.Yes))
        {
            AnsiConsole.MarkupLine("[grey]Nothing was deleted.[/]");
            return 1;
        }

        // The app keeps its own executable open on Windows: deleting around a running process
        // leaves a folder that can neither start nor be removed.
        if ((everything || settings.App) && !await StopAppAsync(cancellation))
        {
            return 1;
        }

        var failed = 0;
        foreach (var target in targets)
        {
            failed += Delete(target.Path) ? 0 : 1;
        }

        PrintToolStep();
        return failed == 0 ? 0 : 1;
    }

    /// <summary>What each flag maps to on disk. Order matters: the app before the folder holding it.</summary>
    private static IEnumerable<(string Path, string Label)> Targets(UninstallSettings settings, bool everything)
    {
        if (everything)
        {
            // The whole folder, settings included (tracked projects, author repos, console state):
            // "uninstall" with no qualifier means nothing of it is expected to survive.
            yield return (AssetInstaller.AppRoot, "the desktop app, the downloaded assets and the settings");

            // The short-name shims live with the tool, not under that folder. Left behind, they
            // stay on PATH and fail with "the term is not recognized" long after everything else
            // is gone — with nothing to say where they came from.
            foreach (var alias in ToolAlias.All())
            {
                yield return (alias, $"the '{Path.GetFileNameWithoutExtension(alias)}' alias");
            }

            yield break;
        }

        if (settings.App)
        {
            yield return (DesktopAppInstaller.InstallRoot, "the desktop app");
        }

        if (settings.Cache)
        {
            yield return (AssetInstaller.GlobalCacheRoot, "the downloaded assets");
            yield return (Path.Combine(AssetInstaller.AppRoot, "catalog.lock.json"), "the offline catalog snapshot");
        }
    }

    private static async Task<bool> StopAppAsync(CancellationToken cancellation)
    {
        if (!(await RunningApp.PingAsync(cancellation)).Running)
        {
            return true;
        }

        AnsiConsole.MarkupLine("[grey]The app is running — stopping it first.[/]");
        if (await RunningApp.StopAsync(TimeSpan.FromSeconds(20), cancellation))
        {
            return true;
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[red]It didn't stop[/] — it is still answering on port {RunningApp.Port}. Quit it from its own window (⏻) and try again.");
        return false;
    }

    private static bool Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                // Not Directory.Delete: the cache holds git clones, and git marks its pack files
                // read-only — a plain recursive delete stops at the first one with "access denied"
                // and leaves the rest of the folder behind.
                AssetInstaller.ForceDeleteDirectory(path);
            }
            else
            {
                ForceDeleteFile(path);
            }

            AnsiConsole.MarkupLineInterpolated($"[green]✓ Removed[/] {path}");
            return true;
        }
        catch (Exception ex)
        {
            // A file open in an editor, a locked clone, a permission — say which one, since the
            // rest of the uninstall did happen and re-running only has this left to do.
            AnsiConsole.MarkupLineInterpolated($"[red]✗ Couldn't remove {path}:[/] {ex.Message}");
            return false;
        }
    }

    /// <summary>Deletes a file that may carry the read-only attribute, for the same reason as above.</summary>
    private static void ForceDeleteFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(path);
    }

    /// <summary>
    /// The last step, always printed: a global tool cannot uninstall itself — dotnet would be
    /// deleting the executable of the process running the command.
    /// </summary>
    private static void PrintToolStep()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]The tool itself can't remove itself while it runs. Finish with:[/]");
        AnsiConsole.MarkupLineInterpolated($"  [bold]{ToolCommand}[/]");
    }

    private static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            // A size is a courtesy; an unreadable corner of the cache must not stop the uninstall.
            return 0;
        }
    }
}
