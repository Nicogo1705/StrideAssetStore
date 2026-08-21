// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Local.Git;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Models;

namespace StrideAssetStore.Core.Local.Indexing;

/// <summary>
/// Materializes assets by cloning their git repositories into a cache directory. Works against any
/// git host (decentralized). Used by CI to clone the public asset repositories.
/// </summary>
public sealed class GitAssetSource(string cacheDirectory, GitClient? git = null) : IAssetSource
{
    private readonly GitClient _git = git ?? new GitClient();

    public AssetCheckout Fetch(RegistryEntry entry)
    {
        if (!_git.IsAvailable())
        {
            throw new InvalidOperationException("git is required for the git asset source but was not found on PATH.");
        }

        Directory.CreateDirectory(cacheDirectory);
        var root = Path.Combine(cacheDirectory, GitClient.SafeRepoFolderName(entry.Repo));
        var assetData = Path.Combine(root, "AssetData");

        // Update an existing checkout to the ref tip; otherwise shallow-clone it.
        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            if (!_git.UpdateToRef(root, entry.Latest.Ref))
            {
                AssetInstaller.ForceDeleteDirectory(root);
                _git.ShallowClone(entry.Repo, entry.Latest.Ref, root);
            }
        }
        else
        {
            if (Directory.Exists(root))
            {
                AssetInstaller.ForceDeleteDirectory(root);
            }

            _git.ShallowClone(entry.Repo, entry.Latest.Ref, root);
        }

        var commit = _git.ResolveCommit(root, "HEAD");
        return new AssetCheckout(root, assetData, commit);
    }
}
