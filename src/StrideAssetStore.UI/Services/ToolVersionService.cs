// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Text.Json;

namespace StrideAssetStore.App.Services;

/// <summary>
/// Looks up the newest released <c>StrideAssetStore</c> on nuget.org, so the status page can say
/// whether the installed command-line tool is behind.
/// </summary>
/// <remarks>
/// The tool tells you itself, once a day, at the end of a command — which nobody sees when the tool
/// is the thing they are not running. The same flat-container endpoint answers here, unauthenticated
/// and CORS-open, so it works from the storefront as well as the desktop app.
/// </remarks>
public sealed class ToolVersionService
{
    private const string PackageId = "strideassetstore";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Whether a lookup has been attempted this session.</summary>
    public bool Checked { get; private set; }

    /// <summary>The newest released (non-prerelease) version, or null when nuget.org didn't answer.</summary>
    public string? Latest { get; private set; }

    /// <summary>Queries nuget.org once. <paramref name="force"/> re-asks after a failed attempt.</summary>
    public async Task CheckAsync(bool force = false, CancellationToken ct = default)
    {
        if (Checked && !force)
        {
            return;
        }

        Checked = true;
        try
        {
            var json = await _http.GetStringAsync(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/index.json", ct);

            using var document = JsonDocument.Parse(json);
            Latest = document.RootElement.GetProperty("versions").EnumerateArray()
                .Select(v => v.GetString())
                .LastOrDefault(v => v is not null && !v.Contains('-')); // released versions only
        }
        catch
        {
            // Offline, blocked, or the package was renamed. The page says "unknown" and offers the
            // update command anyway — it is correct whether or not we could read a version.
            Latest = null;
        }
    }

    /// <summary>
    /// Whether <paramref name="installed"/> is behind <see cref="Latest"/>. Anything unparseable —
    /// a local build's 99.0.0.0, a version with a build suffix — counts as "can't tell", never as
    /// out of date: telling someone to update a build they made themselves is noise.
    /// </summary>
    public bool IsOutdated(string? installed) =>
        Latest is { } latest
        && Version.TryParse(latest, out var newest)
        && Version.TryParse(Clean(installed), out var current)
        && current.Major != 99
        && current < newest;

    /// <summary>Strips what `--version` adds around the number: a name, a +buildmetadata suffix.</summary>
    public static string? Clean(string? version) =>
        version?.Split(' ').LastOrDefault()?.Split('+')[0].Split('-')[0].TrimStart('v', 'V');
}
