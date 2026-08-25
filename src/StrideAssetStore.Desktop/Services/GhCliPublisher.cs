// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.App.Services;
using StrideAssetStore.Core.Local.Registry;
using StrideAssetStore.Core.Local.Shell;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Desktop.Services;

/// <summary>
/// Desktop implementation of <see cref="ICliPublisher"/>: reports what command-line tools the
/// machine has, and hands every registry pull request to the shared <see cref="RegistryPublisher"/>
/// — the same flow the CLI's `publish` and `certify` run.
/// </summary>
public sealed class GhCliPublisher(RegistryOptions registry) : ICliPublisher
{
    private readonly RegistryPublisher _registry = new(registry.Owner, registry.Repo, registry.BaseBranch);

    public bool Supported => true;

    public async Task<CliStatus> CheckAsync(CancellationToken ct = default)
    {
        var git = await RunAsync("git", ["--version"], ct);
        var gh = await RunAsync("gh", ["--version"], ct);
        var authed = false;
        string? account = null;
        if (gh.Ok)
        {
            var status = await RunAsync("gh", ["auth", "status"], ct);
            authed = status.Ok;
            if (authed)
            {
                var login = await RunAsync("gh", ["api", "user", "-q", ".login"], ct);
                account = login.Ok ? login.StdOut.Trim() : null;
            }
        }

        // The store's own CLI: not needed to publish, but this is the page people open when
        // setting up, and it is how the app is installed and updated.
        var tool = await RunAsync("strideassetstore", ["--version"], ct);

        return new CliStatus(
            git.Ok, FirstLine(git.StdOut),
            gh.Ok, FirstLine(gh.StdOut),
            authed, account,
            tool.Ok, FirstLine(tool.StdOut));
    }

    public Task<PublishResult> PublishAsync(RegistryEntry entry, CancellationToken ct = default) =>
        _registry.PublishAsync(entry, ct);

    public Task<PublishResult> CertifyAsync(string id, CertifiedVersion version, CancellationToken ct = default) =>
        _registry.CertifyAsync(id, version, ct);

    public Task<PublishResult> RemoveAsync(string id, CancellationToken ct = default) =>
        _registry.RemoveAsync(id, ct);

    /// <summary>Opens a PR marking the asset deprecated (reason + optional successor id).</summary>
    public Task<PublishResult> DeprecateAsync(string id, string? reason, string? successor, CancellationToken ct = default) =>
        _registry.DeprecateAsync(id, reason, successor, ct);

    // ── Tool detection: the one part that is the app's own business ──────────

    private static async Task<ProcResult> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(exe, args, cancellation: ct);
        return new ProcResult(result.ExitCode, result.StdOut, result.StdErr);
    }

    private static string? FirstLine(string s)
    {
        var line = s.Split('\n', 2)[0].Trim();
        return string.IsNullOrEmpty(line) ? null : line;
    }

    private sealed record ProcResult(int ExitCode, string StdOut, string StdErr)
    {
        public bool Ok => ExitCode == 0;
    }
}
