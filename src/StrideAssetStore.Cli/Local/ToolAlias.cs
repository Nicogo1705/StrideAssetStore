// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

namespace StrideAssetStore.Cli.Local;

/// <summary>
/// The short-name shims: where they live, what they contain, and how to tell ours apart from
/// somebody else's tool of the same name.
/// </summary>
/// <remarks>
/// Shared by <c>alias</c>, which writes them, and <c>uninstall</c>, which has to take them away
/// again — an alias left behind after the tool is gone is a command that fails to explain itself.
/// </remarks>
internal static class ToolAlias
{
    public const string DefaultName = "sas";

    /// <summary>The real command, and the file the shim points at inside its own folder.</summary>
    private const string ToolName = "strideassetstore";

    /// <summary>
    /// Stamped into every shim we write. It is what makes "don't overwrite, don't delete other
    /// people's tools" a fact we can check rather than a name we hope is free.
    /// </summary>
    private const string Marker = "strideassetstore-alias";

    /// <summary>
    /// The folder holding the installed tool — the global tools directory, which is on PATH.
    /// Taken from the running executable so a non-default DOTNET_TOOLS location still works, and
    /// null when this is a local build (its bin folder is not on anyone's PATH).
    /// </summary>
    public static string? Directory
    {
        get
        {
            var directory = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            return File.Exists(Path.Combine(directory, ToolFileName)) ? directory : null;
        }
    }

    private static string ToolFileName => OperatingSystem.IsWindows() ? $"{ToolName}.exe" : ToolName;

    /// <summary>A command name has to be a filename on PATH: keep it to what every shell accepts.</summary>
    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= 32
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && !string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase);

    public static string PathFor(string directory, string name) =>
        Path.Combine(directory, OperatingSystem.IsWindows() ? $"{name}.cmd" : name);

    /// <summary>Whether a folder is on PATH — i.e. whether a shim placed there is callable by name.</summary>
    public static bool OnPath(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(entry =>
            {
                try
                {
                    return string.Equals(
                        Path.GetFullPath(entry.Trim('"')).TrimEnd(Path.DirectorySeparatorChar),
                        full,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                }
                catch
                {
                    return false; // a malformed PATH entry is not the one we are looking for
                }
            });
    }

    /// <summary>Whether this file is a shim we wrote (and may therefore replace or delete).</summary>
    public static bool IsOurs(string path)
    {
        try
        {
            return File.ReadAllText(path).Contains(Marker, StringComparison.Ordinal);
        }
        catch
        {
            // Unreadable means "not provably ours", which is the safe answer for both callers.
            return false;
        }
    }

    /// <summary>
    /// Writes the shim. It calls the tool by its own folder (<c>%~dp0</c> / <c>dirname $0</c>)
    /// rather than by name: resolving through PATH again would find whichever came first, which
    /// on a machine with two installs is not necessarily the one the alias was made from.
    /// </summary>
    public static void Write(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, $"@rem {Marker}\r\n@\"%~dp0{ToolName}.exe\" %*\r\n");
            return;
        }

        File.WriteAllText(path, $"#!/bin/sh\n# {Marker}\nexec \"$(dirname \"$0\")/{ToolName}\" \"$@\"\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>Every shim we wrote, for the uninstall that has to clean up after itself.</summary>
    public static IEnumerable<string> All()
    {
        if (Directory is not { } directory)
        {
            yield break;
        }

        var candidates = OperatingSystem.IsWindows()
            ? System.IO.Directory.EnumerateFiles(directory, "*.cmd")
            : System.IO.Directory.EnumerateFiles(directory);

        foreach (var candidate in candidates)
        {
            if (IsOurs(candidate))
            {
                yield return candidate;
            }
        }
    }
}
