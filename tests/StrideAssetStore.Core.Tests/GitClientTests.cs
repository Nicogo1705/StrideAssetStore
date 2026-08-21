// Copyright (c) 2026 Nicogo1705
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

    [Theory]
    // An SSH clone — what "Code → SSH" and `gh repo clone` give you — must match the registry's
    // https URL. Splitting on '/' alone read "git@github.com:owner" as the owner, so the official
    // asset was recorded as a fork of a repository nobody can clone.
    [InlineData("https://github.com/Nicogo1705/Grass", "git@github.com:Nicogo1705/Grass.git", true)]
    [InlineData("https://github.com/Nicogo1705/Grass", "ssh://git@github.com/Nicogo1705/Grass", true)]
    [InlineData("https://github.com/Nicogo1705/Grass", "https://github.com/Nicogo1705/Grass.git/", true)]
    [InlineData("https://github.com/Nicogo1705/Grass", "https://github.com/nicogo1705/grass", true)]
    [InlineData("https://github.com/Nicogo1705/Grass", "https://github.com/someone/Grass", false)]
    [InlineData("https://github.com/Nicogo1705/Grass", "https://gitlab.com/Nicogo1705/Other", false)]
    public void SameRepository_sees_through_url_forms(string a, string b, bool same) =>
        Assert.Equal(same, GitClient.SameRepository(a, b));

    [Fact]
    public void OwnerRepo_keeps_the_case_it_was_given()
    {
        // This value is written into project files as the fork to follow, so it must stay readable.
        Assert.Equal("Nicogo1705/Grass", GitClient.OwnerRepo("git@github.com:Nicogo1705/Grass.git"));
        Assert.Null(GitClient.OwnerRepo("not-a-url"));
    }
}
