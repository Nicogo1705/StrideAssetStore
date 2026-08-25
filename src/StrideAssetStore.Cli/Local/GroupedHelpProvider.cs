// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace StrideAssetStore.Cli.Local;

/// <summary>
/// Prints the root help with its commands grouped by what they are for.
/// </summary>
/// <remarks>
/// Fifteen commands in one alphabetical-ish list read as fifteen equally likely things to type,
/// when in practice a reader is in one of four situations: putting an asset into a game, writing
/// an asset, looking after the tool itself, or maintaining the registry. Only the root listing is
/// grouped — a single command's help has nothing to group.
/// </remarks>
internal sealed class GroupedHelpProvider(ICommandAppSettings settings) : HelpProvider(settings)
{
    /// <summary>
    /// The groups, in the order someone meets them. A command missing from every group still gets
    /// listed, under the last one: a help page that quietly omits a command is worse than a command
    /// filed under the wrong heading, and this list is edited by hand.
    /// </summary>
    private static readonly (string Title, string[] Commands)[] Groups =
    [
        ("USING ASSETS", ["search", "info", "add", "forks", "list", "update", "remove"]),
        ("AUTHORING AN ASSET", ["new", "check"]),
        ("THIS TOOL AND THE DESKTOP APP", ["app", "upgrade", "alias", "uninstall"]),
        ("REGISTRY MAINTENANCE", ["validate", "build-index", "generate-pages"]),
    ];

    public override IEnumerable<IRenderable> GetCommands(ICommandModel model, ICommandInfo? command)
    {
        // Only the root listing is grouped; `app --help` and the rest keep the standard rendering.
        if (command is not null)
        {
            return base.GetCommands(model, command);
        }

        var commands = model.Commands.Where(c => !c.IsHidden).ToList();
        if (commands.Count == 0)
        {
            return base.GetCommands(model, command);
        }

        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<IRenderable>();

        // One width for every group: each grid measures itself otherwise, and the descriptions
        // start at a different column in each block — which reads as four unrelated tables.
        var width = commands.Max(c => Label(c).Length);

        for (var index = 0; index < Groups.Length; index++)
        {
            var (title, names) = Groups[index];
            var members = commands.Where(c => names.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(c => Array.IndexOf(names, c.Name))
                .ToList();

            // The last group also collects whatever nobody filed.
            if (index == Groups.Length - 1)
            {
                members.AddRange(commands.Where(c => !placed.Contains(c.Name) && !members.Contains(c)));
            }

            if (members.Count == 0)
            {
                continue;
            }

            foreach (var member in members)
            {
                placed.Add(member.Name);
            }

            // The renderables are written one after another with nothing between them, so the
            // blank line and the line break are ours to add — without them the heading and the
            // first command share a line.
            result.Add(new Markup($"{Environment.NewLine}[yellow]{title}:[/]{Environment.NewLine}"));
            result.Add(Table(members, width));
        }

        return result;
    }

    /// <summary>Name and description in two columns, indented under the group heading.</summary>
    private static IRenderable Table(IEnumerable<ICommandInfo> commands, int width)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn { Padding = new Padding(4, 0, 4, 0), NoWrap = true, Width = width });
        grid.AddColumn(new GridColumn { Padding = new Padding(0, 0, 0, 0) });

        foreach (var command in commands)
        {
            var arguments = Arguments(command);
            grid.AddRow(
                new Markup($"[silver]{Markup.Escape(command.Name)}[/]"
                    + (arguments.Length > 0 ? $" [grey]{Markup.Escape(arguments)}[/]" : "")),
                new Markup(Markup.Escape(command.Description ?? "")));
        }

        return grid;
    }

    /// <summary>What the first column holds, unstyled — the width every group is measured against.</summary>
    private static string Label(ICommandInfo command) =>
        Arguments(command) is { Length: > 0 } arguments ? $"{command.Name} {arguments}" : command.Name;

    /// <summary>The command's required arguments, as the usage line spells them.</summary>
    private static string Arguments(ICommandInfo command) =>
        string.Join(' ', command.Parameters
            .OfType<ICommandArgument>()
            .Where(a => a.IsRequired)
            .Select(a => $"<{a.Value}>"));
}
