// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.App.Services;
using StrideAssetStore.Core.Local.Shell;

namespace StrideAssetStore.Desktop.Services;

/// <summary>
/// Desktop implementation of <see cref="ICliRunner"/>: hands a built command to a terminal window
/// of this machine, and lists the tracked solutions so a command can be aimed at one.
/// </summary>
/// <remarks>
/// The output is deliberately not captured back into the page. These commands clone repositories
/// and print progress for minutes; a spinner with the result at the end hides exactly the part
/// worth watching, and the terminal is also where the user can retry the command by hand.
/// </remarks>
public sealed class CliTerminalRunner(ProjectStore projects) : ICliRunner
{
    private const string Tool = "strideassetstore";

    public bool CanRun => true;

    public IReadOnlyList<CliProject> KnownProjects =>
        projects.List().Where(p => p.Exists).Select(p => new CliProject(p.Path, p.Name)).ToList();

    public async Task<CliRunResult> RunAsync(
        string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        // Opening a terminal on a command that isn't installed prints "not recognized" and looks
        // like a broken button. Say what is missing instead — same rule as the update banner.
        var installed = await Task.Run(() => DesktopShell.CommandExists(Tool), ct);
        if (!installed)
        {
            return new CliRunResult(false, ToolMissing: true);
        }

        var command = $"{Tool} {arguments}".TrimEnd();
        var opened = await Task.Run(() => DesktopShell.OpenTerminal(command, workingDirectory), ct);
        return new CliRunResult(opened, ToolMissing: false);
    }
}
