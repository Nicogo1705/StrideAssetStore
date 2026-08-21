// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StrideAssetStore.Core.Local.Git;

/// <summary>Thin wrapper over the installed <c>git</c> executable.</summary>
/// <remarks>
/// Decentralization-friendly: works against any git host. Only the operations needed by the
/// app are exposed (resolve a commit, shallow clone a ref).
/// </remarks>
public sealed class GitClient(string gitExecutable = "git")
{
    private static readonly Regex FolderNamePattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    /// <summary>Returns the full commit SHA that <paramref name="refName"/> resolves to in a repo, or null.</summary>
    public string? ResolveCommit(string repositoryPath, string refName = "HEAD")
    {
        RejectOptionLike(refName); // refs come from untrusted registry data — same guard as the other wrappers
        var (exitCode, output, _) = Run(repositoryPath, "rev-parse", refName);
        return exitCode == 0 ? output.Trim() : null;
    }

    /// <summary>Updates an existing checkout to the tip of <paramref name="refName"/> (shallow fetch + hard reset).</summary>
    public bool UpdateToRef(string repositoryPath, string refName)
    {
        RejectOptionLike(refName);
        if (Run(repositoryPath, [.. SafeProtocol, "fetch", "--depth", "1", "origin", refName]).ExitCode != 0)
        {
            return false;
        }

        return Run(repositoryPath, "reset", "--hard", "FETCH_HEAD").ExitCode == 0;
    }

    /// <summary>
    /// Shallow-clones <paramref name="repoUrl"/> at <paramref name="refName"/> into a directory,
    /// by default checking out <b>only the <c>AssetData/</c> folder</b> (sparse). The store consumes
    /// nothing else, so the rest of the repo — sample/.Windows projects, solutions, etc. — is never
    /// written.
    /// </summary>
    /// <remarks>
    /// Pass <c>assetDataOnly: false</c> for a fork. The registry has no say over someone's copy, so a
    /// fork restructured for their own game keeps its project outside <c>AssetData/</c> — checking out
    /// only that folder would hand the caller an empty tree and nothing to reference.
    /// </remarks>
    public void ShallowClone(string repoUrl, string refName, string destination, bool assetDataOnly = true)
    {
        RejectOptionLike(repoUrl, refName);

        // `--branch` only accepts a branch/tag name; for a raw commit SHA we do a blobless clone of the
        // default branch (no depth cap so any reachable commit can be checked out) and check out the SHA.
        var isCommit = IsCommitSha(refName);
        var cloneArgs = isCommit
            ? new[] { "clone", "--no-checkout", "--filter=blob:none", "--", repoUrl, destination }
            : ["clone", "--no-checkout", "--depth", "1", "--branch", refName, "--", repoUrl, destination];

        var clone = Run(null, [.. SafeProtocol, .. cloneArgs]);
        if (clone.ExitCode != 0)
        {
            throw new InvalidOperationException($"git clone failed for {repoUrl}@{refName}: {clone.StdErr}");
        }

        if (assetDataOnly)
        {
            // Restrict the working tree to AssetData/ before checking it out.
            Run(destination, "config", "core.sparseCheckout", "true");
            Run(destination, "config", "core.sparseCheckoutCone", "false");
            File.WriteAllText(Path.Combine(destination, ".git", "info", "sparse-checkout"), "/AssetData/\n");
        }

        var checkout = Run(destination, [.. SafeProtocol, "checkout", refName]);
        if (checkout.ExitCode != 0)
        {
            throw new InvalidOperationException($"git checkout failed for {repoUrl}@{refName}: {checkout.StdErr}");
        }
    }

    /// <summary>
    /// Derives a safe local folder name from a repo URL (last path segment, sans .git), rejecting
    /// anything that could escape a parent directory (e.g. "..", path separators, invalid chars).
    /// </summary>
    public static string SafeRepoFolderName(string repoUrl)
    {
        var name = repoUrl.TrimEnd('/').Split('/').Last();
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        // OS-independent allowlist (Path.GetInvalidFileNameChars differs per platform — e.g. ':' is
        // allowed on Linux). GitHub repo names are [A-Za-z0-9._-] anyway.
        if (name is "" or "." or ".." || !FolderNamePattern.IsMatch(name))
        {
            throw new InvalidOperationException($"Unsafe repository folder name derived from '{repoUrl}'.");
        }

        return name;
    }

    /// <summary>
    /// Cache folder for a FORK, as <c>&lt;repo&gt;__&lt;owner&gt;</c>. Forking keeps the repository's name, so
    /// deriving the folder from the name alone would make a fork land on top of the asset it forked and
    /// silently replace it for every project on the machine. The owner is what makes it a different thing.
    /// </summary>
    public static string SafeForkFolderName(string forkOwnerRepo)
    {
        var spec = forkOwnerRepo.TrimEnd('/');
        var isUrl = spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var parts = spec.Split('/');

        // A bare spec is exactly owner/repo. Accepting anything longer would quietly keep only the
        // last two segments, turning "../evil/Repo" into a clone URL nobody wrote.
        if (parts.Length < 2 || (!isUrl && parts.Length != 2))
        {
            throw new InvalidOperationException($"A fork must be owner/repo (or a full URL), got '{forkOwnerRepo}'.");
        }

        var repo = SafeRepoFolderName(parts[^1]);
        var owner = parts[^2];
        if (!FolderNamePattern.IsMatch(owner))
        {
            throw new InvalidOperationException($"Unsafe fork owner '{owner}'.");
        }

        return $"{repo}__{owner}";
    }

    /// <summary>Resolves the commit a branch/tag points to on the remote, without cloning. For an annotated
    /// tag the peeled commit (<c>^{}</c> line) is preferred over the tag-object SHA, so it matches a checkout.</summary>
    public string? ResolveRemoteCommit(string repoUrlOrPath, string refName)
    {
        RejectOptionLike(repoUrlOrPath, refName);
        var (exitCode, output, _) = Run(null, [.. SafeProtocol, "ls-remote", "--", repoUrlOrPath, refName]);
        if (exitCode != 0)
        {
            return null;
        }

        string? first = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            var sha = line[..tab].Trim();
            var name = line[(tab + 1)..].Trim();
            if (name.EndsWith("^{}", StringComparison.Ordinal))
            {
                return sha; // peeled commit of an annotated tag — the real commit a checkout lands on
            }

            first ??= sha;
        }

        return first;
    }

    /// <summary>The URL a clone was made from, or null. The only reliable answer to "where does this
    /// folder come from" — a folder name is a sanitized, lossy rendering of it.</summary>
    public string? GetRemoteUrl(string repositoryPath, string remote = "origin")
    {
        RejectOptionLike(remote);
        var (exitCode, output, _) = Run(repositoryPath, "remote", "get-url", remote);
        return exitCode == 0 && output.Trim() is { Length: > 0 } url ? url : null;
    }

    /// <summary>Whether two repository URLs name the same repository (scheme, case and .git suffix aside).</summary>
    public static bool SameRepository(string? a, string? b)
    {
        static string Normalize(string url)
        {
            var value = url.Trim().TrimEnd('/');
            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^4];
            }

            // Compare owner/repo: the same repository is reachable over https, ssh and with or
            // without a credential in the URL.
            var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}".ToLowerInvariant() : value.ToLowerInvariant();
        }

        return a is not null && b is not null && Normalize(a) == Normalize(b);
    }

    /// <summary>ISO-8601 committer date of a commit in a local checkout, or null.</summary>
    public string? GetCommitDate(string repositoryPath, string commit = "HEAD")
    {
        RejectOptionLike(commit);
        var (exitCode, output, _) = Run(repositoryPath, "show", "-s", "--format=%cI", commit);
        return exitCode == 0 ? output.Trim() : null;
    }

    /// <summary>ISO-8601 committer date of the commit that first added a file, or null (also null
    /// on a shallow clone that doesn't reach back to the creation commit).</summary>
    public string? GetFileAddedDate(string repositoryPath, string relativePath)
    {
        RejectOptionLike(relativePath);
        var (exitCode, output, _) = Run(repositoryPath, "log", "--diff-filter=A", "--follow", "--format=%cI", "--", relativePath);
        if (exitCode != 0)
        {
            return null;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1].Trim() : null;
    }

    private static bool IsCommitSha(string value) =>
        value.Length == 40 && value.All(char.IsAsciiHexDigit);

    /// <summary>Lists a repository's tags as (tag, commit) without cloning, via <c>ls-remote --tags</c>.</summary>
    public IReadOnlyList<(string Tag, string Commit)> ListRemoteTags(string repoUrlOrPath)
    {
        RejectOptionLike(repoUrlOrPath);
        var (exitCode, output, _) = Run(null, [.. SafeProtocol, "ls-remote", "--tags", "--", repoUrlOrPath]);
        return exitCode == 0 ? ParseLsRemoteTags(output) : [];
    }

    /// <summary>Parses <c>git ls-remote --tags</c> output, preferring the peeled commit of annotated tags.</summary>
    public static IReadOnlyList<(string Tag, string Commit)> ParseLsRemoteTags(string output)
    {
        const string prefix = "refs/tags/";
        var commits = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var sha = line[..tab].Trim();
            var refName = line[(tab + 1)..].Trim();
            if (!refName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var tag = refName[prefix.Length..];
            var peeled = tag.EndsWith("^{}", StringComparison.Ordinal);
            if (peeled)
            {
                tag = tag[..^3];
            }

            if (!commits.ContainsKey(tag))
            {
                order.Add(tag);
            }

            // Peeled line (annotated tag) carries the actual commit and overrides the tag-object sha.
            if (peeled || !commits.ContainsKey(tag))
            {
                commits[tag] = sha;
            }
        }

        return order.Select(t => (t, commits[t])).ToList();
    }

    /// <summary>True if <c>git</c> is available on the PATH.</summary>
    public bool IsAvailable()
    {
        try
        {
            return Run(null, "--version").ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Upper bound for a single git invocation; a git hung on the network (credential
    /// prompt, dead remote) would otherwise freeze the caller forever. Generous on purpose:
    /// clones here are shallow/sparse, so anything slower than this is stuck, not slow.</summary>
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    private (int ExitCode, string StdOut, string StdErr) Run(string? workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(gitExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        // The app is a WinExe with no console and no cancel button, so a prompt nobody can answer is
        // a freeze: git asking for credentials on a renamed or private repository, or the Git
        // Credential Manager opening a window behind the browser. Fail instead. (ProcessRunner does
        // the same for gh — every child this project starts must be non-interactive.)
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = "never";
        info.Environment["GIT_ASKPASS"] = "";
        info.Environment["SSH_ASKPASS"] = "";

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // A missing git is an answer, not a crash: Process.Start throws Win32Exception when the
        // executable isn't on PATH, and callers like AnalyzeProject are not wrapped — one machine
        // without git took down the whole project analysis instead of reporting it.
        Process? started;
        try
        {
            started = Process.Start(info);
        }
        catch (Exception ex)
        {
            return (-1, "", $"Unable to run '{gitExecutable}': {ex.Message}");
        }

        if (started is null)
        {
            return (-1, "", $"Unable to start '{gitExecutable}'.");
        }

        using var process = started;

        // Read both pipes concurrently to avoid a deadlock when one fills its buffer (git writes
        // progress to stderr while output goes to stdout).
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // process exited between the wait and the kill
            }

            process.WaitForExit(); // flush the pipe readers
            return (-1, stdout.GetAwaiter().GetResult(),
                $"git timed out after {ProcessTimeout.TotalMinutes:0} minutes and was killed. {stderr.GetAwaiter().GetResult()}".TrimEnd());
        }

        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    // Block non-https transports (notably git's `ext::` = arbitrary command execution) and any
    // argument that looks like an option, since repo URLs / refs come from untrusted registry data.
    private static readonly string[] SafeProtocol =
        ["-c", "protocol.ext.allow=never", "-c", "protocol.file.allow=never"];

    private static void RejectOptionLike(params string[] values)
    {
        foreach (var v in values)
        {
            if (v.StartsWith('-'))
            {
                throw new InvalidOperationException($"Refusing git argument that looks like an option: '{v}'.");
            }
        }
    }
}
