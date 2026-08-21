// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Net.Http.Headers;
using System.Text.Json;

namespace StrideAssetStore.Core.Local.Git;

/// <summary>A fork of an asset's repository, as GitHub reports it.</summary>
/// <param name="FullName"><c>owner/repo</c> — what an install takes.</param>
/// <param name="Owner">Who owns the fork.</param>
/// <param name="Stars">Stargazers, so the list can lead with the ones people actually use.</param>
/// <param name="PushedAt">Last push, so a fork abandoned years ago is visibly that.</param>
public sealed record AssetFork(string FullName, string Owner, int Stars, DateTimeOffset? PushedAt);

/// <summary>
/// Lists the forks of an asset's repository. Typing <c>owner/repo</c> from memory is a poor way to
/// pick one, and the answer is a single public API call away.
/// </summary>
public sealed class ForkLister(HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Forks of <paramref name="repoUrl"/>, most-starred first then most recently pushed. Returns an
    /// empty list rather than throwing when GitHub can't answer: not knowing the forks must never
    /// stop someone from typing one in.
    /// </summary>
    public async Task<IReadOnlyList<AssetFork>> ListAsync(string repoUrl, CancellationToken cancellation = default) =>
        (await TryListAsync(repoUrl, cancellation)).Forks;

    /// <summary>
    /// Same, but says whether GitHub actually answered. A caller that prints a conclusion needs the
    /// difference: "this asset has no forks" and "we couldn't ask" are not the same sentence, and
    /// rate limiting (60 requests an hour per IP, anonymously) makes the second one common.
    /// </summary>
    public async Task<(bool Reached, IReadOnlyList<AssetFork> Forks)> TryListAsync(
        string repoUrl, CancellationToken cancellation = default)
    {
        var parts = repoUrl.TrimEnd('/').Split('/');
        if (parts.Length < 2)
        {
            return (false, []); // not a repository URL we can ask about
        }

        var (owner, repo) = (parts[^2], parts[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? parts[^1][..^4]
            : parts[^1]);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/forks?per_page=100&sort=stargazers");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("StrideAssetStore", "1.0"));
        if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
        {
            // 60 requests an hour per IP anonymously; a signed-in user opening a few asset pages
            // would hit that on its own.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return (false, []);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var forks = new List<AssetFork>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var fullName = element.TryGetProperty("full_name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    continue;
                }

                forks.Add(new AssetFork(
                    fullName,
                    element.TryGetProperty("owner", out var o) && o.TryGetProperty("login", out var l)
                        ? l.GetString() ?? "" : "",
                    element.TryGetProperty("stargazers_count", out var s) ? s.GetInt32() : 0,
                    element.TryGetProperty("pushed_at", out var p) && p.TryGetDateTimeOffset(out var pushed)
                        ? pushed : null));
            }

            return (true, [.. forks.OrderByDescending(f => f.Stars).ThenByDescending(f => f.PushedAt)]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, []);
        }
    }
}
