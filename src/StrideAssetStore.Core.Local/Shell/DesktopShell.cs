// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;

namespace StrideAssetStore.Core.Local.Shell;

/// <summary>
/// Hands a path or a URL to the desktop environment. Every OS spells this differently and getting
/// it wrong is silent — the call just does nothing on Linux and macOS — so it lives in one place.
/// </summary>
public static class DesktopShell
{
    /// <summary>Opens a folder in the system file manager. False when there is nothing to open.</summary>
    /// <remarks>
    /// The existence check is the point: explorer.exe always starts and always reports success, so a
    /// path that has been deleted or renamed produced a button that did nothing and said nothing.
    /// Windows then goes through ShellExecute on the folder itself rather than explorer.exe with an
    /// argument — a path ending in a separator gets quoted as <c>"C:\dir\"</c>, where the trailing
    /// backslash escapes the closing quote and the argument arrives mangled.
    /// </remarks>
    public static bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        var folder = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                return true; // ShellExecute on a directory hands it to the file manager, which may already be running
            }
            catch
            {
                return false;
            }
        }

        return Start(default, ("open", [folder]), ("xdg-open", [folder]));
    }

    /// <summary>
    /// Opens the file manager with <paramref name="path"/> selected. Only Windows and macOS can
    /// select an entry; elsewhere the containing folder is opened, which is the useful part anyway.
    /// </summary>
    public static bool RevealFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return OpenFolder(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "");
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return Start(
            windows: ("explorer.exe", [$"/select,{path}"]),
            macos: ("open", ["-R", path]),
            linux: ("xdg-open", [parent ?? path]));
    }

    /// <summary>Opens a URL in the user's default browser.</summary>
    /// <remarks>
    /// Windows goes through ShellExecute rather than <c>cmd /c start</c>: URLs come from the
    /// published index, which anyone can propose an entry to, and a quote inside one escaped the
    /// quoting and handed the rest to the command interpreter.
    /// </remarks>
    public static bool OpenUrl(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return process is not null;
            }
            catch
            {
                return false;
            }
        }

        return Start(default, ("open", [url]), ("xdg-open", [url]));
    }

    /// <summary>
    /// Whether <paramref name="executable"/> can be launched from this process's environment — i.e.
    /// whether opening a terminal on it would do anything but print "not recognized".
    /// </summary>
    public static bool CommandExists(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process?.WaitForExit(5000);
            return process is not null;
        }
        catch
        {
            // Win32Exception when it isn't on PATH — the only answer that matters here.
            return false;
        }
    }

    /// <summary>
    /// Opens a terminal window running <paramref name="command"/>, left open afterwards so its output
    /// stays readable. False when no terminal could be launched — Linux has no single one, and the
    /// caller still has the command to show.
    /// </summary>
    public static bool OpenTerminal(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            // /k keeps the window after the command finishes; the user sees what happened.
            return Start(("cmd.exe", ["/k", command]), default, default, useShellExecute: true);
        }

        if (OperatingSystem.IsMacOS())
        {
            var script = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return Start(default, ("osascript",
                ["-e", $"tell application \"Terminal\" to do script \"{script}\"",
                 "-e", "tell application \"Terminal\" to activate"]), default);
        }

        // No standard terminal on Linux: try the usual suspects, and let the caller fall back.
        foreach (var terminal in (string[])["x-terminal-emulator", "gnome-terminal", "konsole", "xterm"])
        {
            // ArgumentList, not one string: .NET splits Arguments with Windows rules even on Unix,
            // where a single quote is not a quoting character — the shell command came out shredded
            // into separate argv entries and never ran, while the terminal opened and reported
            // success.
            if (Start(default, default, (terminal, ["-e", $"bash -c \"{command}; exec bash\""])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Start((string File, string[] Args) windows, (string File, string[] Args) macos,
        (string File, string[] Args) linux, bool useShellExecute = false)
    {
        var (file, args) = OperatingSystem.IsWindows() ? windows
            : OperatingSystem.IsMacOS() ? macos
            : linux;

        if (string.IsNullOrEmpty(file))
        {
            return false; // nothing defined for this platform
        }

        try
        {
            // Normally false: the launcher IS the shell, and true would make the child inherit this
            // process's console on Windows. Opening a terminal is the exception — it needs its own
            // window, which is precisely what ShellExecute gives it.
            // ArgumentList quotes each argument for the platform; a single Arguments string would
            // leave that to whatever the caller pasted in.
            var info = new ProcessStartInfo(file) { UseShellExecute = useShellExecute };
            foreach (var argument in args)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            return process is not null;
        }
        catch
        {
            // No file manager, no xdg-utils, headless session — nothing actionable for the caller
            // beyond "it didn't open".
            return false;
        }
    }
}
