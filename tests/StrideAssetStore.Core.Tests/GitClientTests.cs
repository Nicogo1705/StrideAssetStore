// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Local.Git;

namespace StrideAssetStore.Core.Tests;

public sealed class GitClientTests
{
    [Theory]
    [InlineData("https://github.com/owner/MyAsset", "MyAsset")]
    [InlineData("https://github.com/owner/MyAsset.git", "MyAsset")]
    [InlineData("https://github.com/owner/MyAsset/", "MyAsset")]
    public void SafeRepoFolderName_extracts_clean_name(string url, string expected) =>
        Assert.Equal(expected, GitClient.SafeRepoFolderName(url));

    [Theory]
    [InlineData("https://github.com/owner/..")]
    [InlineData("https://evil/x/.")]
    [InlineData("https://evil/a:b")]
    public void SafeRepoFolderName_rejects_traversal(string url) =>
        Assert.Throws<InvalidOperationException>(() => GitClient.SafeRepoFolderName(url));

    [Fact]
    public void A_fork_never_lands_on_the_asset_it_forked()
    {
        // Forking keeps the repository's name, so folder-by-name alone would put the fork on top of
        // the original — silently replacing it for every project on the machine.
        const string upstream = "https://github.com/Nicogo1705/StrideGrassSystem";
        const string fork = "Someone/StrideGrassSystem";

        Assert.Equal("StrideGrassSystem", GitClient.SafeRepoFolderName(upstream));
        Assert.Equal("StrideGrassSystem__Someone", GitClient.SafeForkFolderName(fork));
        Assert.NotEqual(GitClient.SafeRepoFolderName(upstream), GitClient.SafeForkFolderName(fork));
    }

    [Theory]
    [InlineData("StrideGrassSystem")]           // no owner
    [InlineData("../evil/StrideGrassSystem")]   // owner is traversal
    public void SafeForkFolderName_rejects_what_it_cannot_place(string fork) =>
        Assert.Throws<InvalidOperationException>(() => GitClient.SafeForkFolderName(fork));
}
