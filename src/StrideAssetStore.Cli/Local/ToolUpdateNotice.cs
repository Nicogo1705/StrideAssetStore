// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using System.Text.Json;
using Spectre.Console;
using StrideAssetStore.Core.Local.Install;

namespace StrideAssetStore.Cli.Local;

/// <summary>
/// Tells the user when a newer <c>strideassetstore</c> exists on nuget.org.
/// </summary>
/// <remarks>
/// The desktop app checks its own version on every start; the tool never did, so a stale one kept
/// its bugs indefinitely with nobody the wiser. Checked at most once a day, in the background, and
/// after the command's own output — a version notice must never delay or bury what was asked for.
/// </remarks>
internal static class ToolUpdateNotice
{
    private const string PackageId = "StrideAssetStore";

    /// <summary>How long a check is trusted. A day is far more often than releases happen.</summary>
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    private static string StampFile => Path.Combine(AssetInstaller.AppRoot, "tool-update-check.json");

    private static Task<string?>? _pending;

    /// <summary>
    /// Starts the check if one is due, without waiting for it. Call before the command runs so the
    /// answer is usually ready by the time it finishes.
    /// </summary>
    public static void Begin()
    {
        // NO_COLOR and redirected output mean a script is reading us; a notice would be noise there.
        if (Console.IsOutputRedirected
            || Environment.GetEnvironmentVariable("NO_COLOR") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("STRIDEASSETSTORE_NO_UPDATE_CHECK") is { Length: > 0 }
            || !IsDue())
        {
            return;
        }

        _pending = Task.Run(FetchLatestAsync);
    }

    /// <summary>Prints the notice if a newer version was found. Never throws, never blocks for long.</summary>
    public static void End()
    {
        if (_pending is null)
        {
            return;
        }

        string? latest = null;
        try
        {
            // A slow or unreachable nuget.org must not hold the command open.
            latest = _pending.Wait(TimeSpan.FromSeconds(2)) ? _pending.Result : null;
        }
        catch
        {
            // Nothing about a version check is worth failing a command over.
        }

        if (latest is null || Current() is not { } current || !IsNewer(latest, current))
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated(
            $"[yellow]A newer strideassetstore is available:[/] [grey]{current}[/] → [green]{latest}[/]");
        AnsiConsole.MarkupLine("[grey]Update with[/] [bold]dotnet tool update -g StrideAssetStore[/]");
    }

    /// <summary>The running tool's version, or null for a local build with no version stamped.</summary>
    private static Version? Current() =>
        typeof(ToolUpdateNotice).Assembly.GetName().Version is { Major: not 99 } v ? v : null;

    private static bool IsNewer(string latest, Version current) =>
        Version.TryParse(latest.Split('-')[0], out var parsed) && parsed > current;

    /// <summary>Whether a check is due, and stamps the file so the next run isn't.</summary>
    private static bool IsDue()
    {
        // Read and write are handled separately on purpose: a stamp we can't parse means we don't
        // know when we last checked, which is a reason to check — folding it into one catch made a
        // truncated file disable the notice permanently, because nothing ever rewrote it.
        try
        {
            if (File.Exists(StampFile)
                && JsonSerializer.Deserialize<Stamp>(File.ReadAllText(StampFile)) is { } stamp
                && DateTimeOffset.UtcNow - stamp.CheckedAt < CheckEvery)
            {
                return false;
            }
        }
        catch
        {
            // Unreadable or corrupt: fall through and check, which also rewrites it.
        }

        try
        {
            // Stamped before the request, not after: offline, retrying on every single command would
            // cost a DNS timeout each time for an answer that isn't coming.
            Directory.CreateDirectory(AssetInstaller.AppRoot);
            File.WriteAllText(StampFile, JsonSerializer.Serialize(new Stamp(DateTimeOffset.UtcNow)));
            return true;
        }
        catch
        {
            // Unwritable app folder: skip rather than check on every command with nothing to remember it.
            return false;
        }
    }

    private static async Task<string?> FetchLatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId.ToLowerInvariant()}/index.json");

            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("versions").EnumerateArray()
                .Select(v => v.GetString())
                .LastOrDefault(v => v is not null && !v.Contains('-')); // released versions only
        }
        catch
        {
            return null;
        }
    }

    private sealed record Stamp(DateTimeOffset CheckedAt);
}
