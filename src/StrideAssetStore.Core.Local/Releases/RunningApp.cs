// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Text.Json;

namespace StrideAssetStore.Core.Local.Releases;

/// <summary>The desktop app answering on the local port, if any.</summary>
/// <param name="Running">Whether anything answered.</param>
/// <param name="Version">Version it reported, when it is recent enough to report one.</param>
public sealed record AppPing(bool Running, string? Version);

/// <summary>
/// Talks to the desktop app running on this machine over its local HTTP endpoints. The app is a
/// local server, so this is more reliable than hunting for a process name — and it is the same
/// door the storefront knocks on.
/// </summary>
public static class RunningApp
{
    /// <summary>The port the desktop app serves on. Fixed, because the protocol handler and the
    /// storefront's detection both hard-code it.</summary>
    public const int Port = 5111;

    /// <summary>The <c>app</c> field /api/ping answers with — how a caller knows it reached us.</summary>
    public const string AppMarker = "stride-assetstore";

    private static string Base => $"http://localhost:{Port}";

    /// <summary>Whether the app is up, and which version it is. Never throws.</summary>
    public static async Task<AppPing> PingAsync(CancellationToken cancellation = default)
    {
        // Short timeout: "not running" is the common answer and must be instant.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            using var response = await http.GetAsync($"{Base}/api/ping", cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return new AppPing(false, null);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));

            // Check the marker: a 200 on this port proves something is listening, not that it is us.
            // Another dev server answering here would otherwise be taken for the app — and stopped.
            if (!document.RootElement.TryGetProperty("app", out var name)
                || name.GetString() != AppMarker)
            {
                return new AppPing(false, null);
            }

            return new AppPing(true,
                document.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null);
        }
        catch
        {
            // Nothing listening, or something else is on the port and isn't us.
            return new AppPing(false, null);
        }
    }

    /// <summary>
    /// Asks the app to quit and waits for the port to go quiet. Returns false only when it was
    /// still answering after <paramref name="timeout"/> — the caller must not overwrite its files
    /// then, because Windows keeps a running executable locked.
    /// </summary>
    public static async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellation = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            await http.PostAsync($"{Base}/app/quit", null, cancellation);
        }
        catch
        {
            // The app shuts down while answering, so a dropped connection is the normal outcome.
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!(await PingAsync(cancellation)).Running)
            {
                // Give the process a moment to release its file handles after the socket closes.
                await Task.Delay(500, cancellation);
                return true;
            }

            await Task.Delay(300, cancellation);
        }

        return false;
    }
}
