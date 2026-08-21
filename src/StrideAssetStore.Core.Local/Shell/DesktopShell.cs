// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;

namespace StrideAssetStore.Core.Local.Shell;

/// <summary>
/// Hands a path or a URL to the desktop environment. Every OS spells this differently and getting
/// it wrong is silent — the call just does nothing on Linux and macOS — so it lives in one place.
/// </summary>
public static class DesktopShell
{
    /// <summary>Opens a folder in the system file manager.</summary>
    public static bool OpenFolder(string path) => Start(
        windows: ("explorer.exe", Quote(path)),
        macos: ("open", Quote(path)),
        linux: ("xdg-open", Quote(path)));

    /// <summary>
    /// Opens the file manager with <paramref name="path"/> selected. Only Windows and macOS can
    /// select an entry; elsewhere the containing folder is opened, which is the useful part anyway.
    /// </summary>
    public static bool RevealFile(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return Start(
            windows: ("explorer.exe", $"/select,{Quote(path)}"),
            macos: ("open", $"-R {Quote(path)}"),
            linux: ("xdg-open", Quote(parent ?? path)));
    }

    /// <summary>Opens a URL in the user's default browser.</summary>
    public static bool OpenUrl(string url) => Start(
        // cmd's `start` treats the first quoted argument as a window title, hence the empty one.
        windows: ("cmd", $"/c start \"\" {Quote(url)}"),
        macos: ("open", Quote(url)),
        linux: ("xdg-open", Quote(url)));

    private static bool Start((string File, string Args) windows, (string File, string Args) macos,
        (string File, string Args) linux)
    {
        var (file, args) = OperatingSystem.IsWindows() ? windows
            : OperatingSystem.IsMacOS() ? macos
            : linux;

        try
        {
            // UseShellExecute stays false: the launcher IS the shell here, and true would make the
            // child inherit this process's console on Windows.
            using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false });
            return process is not null;
        }
        catch
        {
            // No file manager, no xdg-utils, headless session — nothing actionable for the caller
            // beyond "it didn't open".
            return false;
        }
    }

    private static string Quote(string value) => $"\"{value}\"";
}
