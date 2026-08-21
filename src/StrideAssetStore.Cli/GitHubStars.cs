// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Text.Json;

namespace StrideAssetStore.Cli;

/// <summary>Best-effort GitHub popularity lookup (stars and forks) for index enrichment.</summary>
public sealed class GitHubStars
{
    private readonly HttpClient _http = new();

    public GitHubStars(string? token)
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("StrideAssetStore-cli");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        }
    }

    /// <summary>Stars and forks for a GitHub repo URL, both null when it isn't GitHub or the call
    /// fails. One request answers both — they live in the same payload.</summary>
    public (int? Stars, int? Forks) Get(string repoUrl)
    {
        if (!TryParseGitHub(repoUrl, out var owner, out var name))
        {
            return (null, null);
        }

        try
        {
            var json = _http.GetStringAsync($"https://api.github.com/repos/{owner}/{name}").GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            return (
                doc.RootElement.TryGetProperty("stargazers_count", out var s) ? s.GetInt32() : null,
                doc.RootElement.TryGetProperty("forks_count", out var f) ? f.GetInt32() : null);
        }
        catch
        {
            return (null, null);
        }
    }

    private static bool TryParseGitHub(string repoUrl, out string owner, out string name)
    {
        owner = name = "";
        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
        {
            return false;
        }

        owner = parts[0];
        name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return true;
    }
}
