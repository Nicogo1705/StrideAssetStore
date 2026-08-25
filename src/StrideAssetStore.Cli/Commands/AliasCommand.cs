// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using StrideAssetStore.Cli.Local;

namespace StrideAssetStore.Cli.Commands;

internal sealed class AliasSettings : CommandSettings
{
    [CommandOption("-n|--name <NAME>")]
    [Description("The short name to create. Defaults to 'sas'.")]
    [DefaultValue("sas")]
    public string Name { get; init; } = ToolAlias.DefaultName;

    [CommandOption("--remove")]
    [Description("Delete the alias instead of creating it.")]
    public bool Remove { get; init; }
}

/// <summary>
/// Creates a short name for this tool — <c>sas add grass</c> instead of the full seventeen letters.
/// </summary>
/// <remarks>
/// A NuGet tool package can only declare one command: the .NET SDK refuses a package whose settings
/// file lists more than one, so a second name cannot ship inside it, and renaming the real one would
/// break every script and every README that already says `strideassetstore`. What can be done is to
/// drop a two-line shim next to the tool's own — that folder is already on PATH, which is how the
/// tool is reachable at all — so the alias works in cmd, PowerShell and any Unix shell without
/// touching a single shell profile.
/// </remarks>
internal sealed class AliasCommand : Command<AliasSettings>
{
    protected override int Execute(CommandContext context, AliasSettings settings, CancellationToken cancellation)
    {
        var name = settings.Name.Trim();
        if (!ToolAlias.IsValidName(name))
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[red]'{name}' can't be a command name.[/] Use 1-32 letters, digits, '-' or '_'.");
            return 1;
        }

        if (ToolAlias.Directory is not { } directory)
        {
            AnsiConsole.MarkupLine(
                "[red]Couldn't find the folder this tool is installed in[/] — an alias only helps when it lands on PATH.");
            AnsiConsole.MarkupLine("[grey]Running from a local build? Install the tool first:[/] [bold]dotnet tool install -g StrideAssetStore[/]");
            return 1;
        }

        var path = ToolAlias.PathFor(directory, name);
        return settings.Remove ? RemoveAlias(path, name) : CreateAlias(path, name);
    }

    private static int CreateAlias(string path, string name)
    {
        // Never write over something that isn't ours: this folder holds other people's tools, and
        // `alias --name dotnet-grpc` would otherwise replace one of them with a redirect to us.
        if (File.Exists(path) && !ToolAlias.IsOurs(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{path} already exists[/] and wasn't created by this tool. Pick another name.");
            return 1;
        }

        try
        {
            ToolAlias.Write(path);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Couldn't write {path}:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[green]✓ {name}[/] now runs strideassetstore. [grey]({path})[/]");

        // A global tool lands in a folder that is on PATH; `dotnet tool install --tool-path` does
        // not. Telling someone to "try it in a new terminal" when nothing can find it is the kind
        // of small lie that costs ten minutes.
        if (ToolAlias.OnPath(Path.GetDirectoryName(path)!))
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]Try it in a new terminal:[/] [bold]{name} search grass[/]");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]That folder isn't on your PATH[/] — this is a --tool-path install, so call it by its full path, or add the folder to PATH.");
        }

        return 0;
    }

    private static int RemoveAlias(string path, string name)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[grey]No '{name}' alias here — nothing to remove.[/]");
            return 0;
        }

        // Same rule in reverse: `alias --remove --name dotnet-grpc` must not delete another tool.
        if (!ToolAlias.IsOurs(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{path} wasn't created by this tool[/] — leaving it alone.");
            return 1;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Couldn't delete {path}:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[green]✓ Removed the '{name}' alias.[/]");
        return 0;
    }
}
