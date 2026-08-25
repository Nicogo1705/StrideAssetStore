// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace StrideAssetStore.App.Services;

/// <summary>A solution or project the machine tracks, offered as a target for a built command.</summary>
/// <param name="Path">Absolute path to a .sln/.slnx/.csproj.</param>
/// <param name="Name">File name, for the picker.</param>
public sealed record CliProject(string Path, string Name);

/// <summary>
/// What came of asking the machine to run a command. <paramref name="ToolMissing"/> separates
/// "the tool isn't installed" from "no terminal opened" — the two need different advice.
/// </summary>
public sealed record CliRunResult(bool Opened, bool ToolMissing);

/// <summary>
/// Runs a <c>strideassetstore</c> command in a terminal window of the user's own machine.
/// </summary>
/// <remarks>
/// A terminal rather than captured output on purpose: these commands clone repositories, print
/// progress and sometimes ask something, and the window stays open afterwards so what happened
/// remains readable. The browser host has no machine to run on and reports itself unavailable.
/// </remarks>
public interface ICliRunner
{
    /// <summary>True on hosts that can start local processes (desktop); false in the browser.</summary>
    bool CanRun { get; }

    /// <summary>Solutions the user tracks, to fill the <c>--target</c> of a built command.</summary>
    IReadOnlyList<CliProject> KnownProjects { get; }

    /// <summary>Opens a terminal running <c>strideassetstore <paramref name="arguments"/></c>.</summary>
    /// <param name="arguments">Everything after the executable name, already quoted.</param>
    /// <param name="workingDirectory">Folder to start in, or null for the default.</param>
    /// <param name="ct">Cancels the check for the tool before the terminal opens.</param>
    Task<CliRunResult> RunAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default);
}

/// <summary>Builds the argument strings a shell will read back the way they were meant.</summary>
public static class ShellArg
{
    /// <summary>
    /// Quotes a value that a shell would otherwise mangle. Whitespace is the obvious case; the one
    /// that bites is a path like <c>C:\Dev\R&amp;D\Game.slnx</c>, where cmd reads the ampersand as a
    /// command separator and runs the tail as a second command. Double quotes suit both cmd and
    /// POSIX shells, which is what the built commands are pasted into.
    /// </summary>
    public static string Quote(string value) =>
        value.Length > 0 && !value.StartsWith('"') && value.IndexOfAny(NeedsQuoting) >= 0
            ? $"\"{value}\""
            : value;

    private static readonly char[] NeedsQuoting =
        [' ', '\t', '&', '|', '^', '<', '>', '(', ')', ';', ',', '%', '!', '\'', '`', '$', '*', '?'];
}

/// <summary>Browser fallback: nothing local to run on, so commands are only ever copied.</summary>
public sealed class NullCliRunner : ICliRunner
{
    public bool CanRun => false;

    public IReadOnlyList<CliProject> KnownProjects => [];

    public Task<CliRunResult> RunAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default) =>
        Task.FromResult(new CliRunResult(false, false));
}
