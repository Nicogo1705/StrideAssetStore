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
    private static bool _forced;

    /// <summary>
    /// Starts the check if one is due, without waiting for it. Call before the command runs so the
    /// answer is usually ready by the time it finishes.
    /// </summary>
    /// <param name="force">
    /// Ask nuget.org even if today's check already happened. True when the tool is run with no
    /// arguments at all: that is somebody looking at the tool rather than using it, the one moment
    /// where a network round-trip costs nothing and "you are one version behind" is the answer they
    /// came for. Every other invocation keeps the once-a-day cadence — a version check must not be
    /// something every `add` pays for.
    /// </param>
    public static void Begin(bool force = false)
    {
        // NO_COLOR and redirected output mean a script is reading us; a notice would be noise there.
        // These still apply when forced: `strideassetstore > help.txt` is not a person reading.
        if (Console.IsOutputRedirected
            || Environment.GetEnvironmentVariable("NO_COLOR") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("STRIDEASSETSTORE_NO_UPDATE_CHECK") is { Length: > 0 })
        {
            return;
        }

        if (!force && !IsDue())
        {
            return;
        }

        // A forced check is still a check: stamping it means the next `add` doesn't repeat it.
        if (force)
        {
            Stamp();
        }

        _forced = force;
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
            // A slow or unreachable nuget.org must not hold the command open. The forced check gets
            // longer: nothing else was asked for, so there is nothing it delays — and giving up at
            // two seconds is how a deliberate "am I up to date?" answers with silence.
            latest = _pending.Wait(TimeSpan.FromSeconds(_forced ? 6 : 2)) ? _pending.Result : null;
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

    /// <summary>
    /// The running tool's version, or null for a build that carries none: 99.0.0.0 is a local
    /// Release build and 0.0.0.0 a Debug one. Both are older than every published version, so
    /// treating them as a version turns "you are behind" into a permanent, wrong answer — and
    /// `upgrade` would offer to replace a build made on purpose with the last release.
    /// </summary>
    internal static Version? Current() =>
        typeof(ToolUpdateNotice).Assembly.GetName().Version is { Major: not 99 and not 0 } v ? v : null;

    internal static bool IsNewer(string latest, Version current) =>
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
                && JsonSerializer.Deserialize<StampFileContent>(File.ReadAllText(StampFile)) is { } stamp
                && DateTimeOffset.UtcNow - stamp.CheckedAt < CheckEvery)
            {
                return false;
            }
        }
        catch
        {
            // Unreadable or corrupt: fall through and check, which also rewrites it.
        }

        // Stamped before the request, not after: offline, retrying on every single command would
        // cost a DNS timeout each time for an answer that isn't coming.
        return Stamp();
    }

    /// <summary>Records that a check happened now. False when the folder can't be written.</summary>
    private static bool Stamp()
    {
        try
        {
            Directory.CreateDirectory(AssetInstaller.AppRoot);
            File.WriteAllText(StampFile, JsonSerializer.Serialize(new StampFileContent(DateTimeOffset.UtcNow)));
            return true;
        }
        catch
        {
            // Unwritable app folder: skip rather than check on every command with nothing to remember it.
            return false;
        }
    }

    /// <summary>The newest released version on nuget.org, or null when it couldn't be read.</summary>
    internal static async Task<string?> FetchLatestAsync()
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

    private sealed record StampFileContent(DateTimeOffset CheckedAt);
}
