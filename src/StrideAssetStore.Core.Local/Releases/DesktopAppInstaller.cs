// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Releases;

namespace StrideAssetStore.Core.Local.Releases;

/// <summary>What the release API says the newest desktop build is.</summary>
/// <param name="Version">Release tag without its leading 'v'.</param>
/// <param name="DownloadUrl">Archive built for this machine, or null when this OS has no build.</param>
/// <param name="SizeBytes">Size of that archive, when the API reported it.</param>
public sealed record DesktopRelease(string Version, string? DownloadUrl, long? SizeBytes);

/// <summary>
/// Installs and updates the desktop app from its GitHub releases, so someone who found the store
/// through the CLI never has to visit a download page. This is the only supported update path: the
/// app doesn't replace itself, so updating never depends on its UI being alive.
/// </summary>
public sealed class DesktopAppInstaller(HttpClient? http = null)
{
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Where a CLI-installed app lives. Kept apart from any copy the user unzipped themselves.</summary>
    public static string InstallRoot => Path.Combine(AssetInstaller.AppRoot, "app");

    private static string VersionMarker => Path.Combine(InstallRoot, ".version");

    /// <summary>The executable of a CLI-installed app, or null when it isn't installed.</summary>
    public static string? ExecutablePath()
    {
        if (!Directory.Exists(InstallRoot))
        {
            return null;
        }

        var name = OperatingSystem.IsWindows() ? "StrideAssetStore.Desktop.exe" : "StrideAssetStore.Desktop";
        return Directory.EnumerateFiles(InstallRoot, name, SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>The version installed by this tool, or null when nothing is installed.</summary>
    public static string? InstalledVersion() =>
        File.Exists(VersionMarker) && ExecutablePath() is not null
            ? File.ReadAllText(VersionMarker).Trim()
            : null;

    /// <summary>Asks GitHub for the latest release and picks the archive built for this machine.</summary>
    public async Task<DesktopRelease> FetchLatestAsync(string repoUrl, CancellationToken cancellation = default)
    {
        var (owner, repo) = ParseRepo(repoUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        // api.github.com rejects requests without one.
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("StrideAssetStore-cli", "1.0"));
        if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token)
        {
            // Anonymous callers get 60 requests an hour per IP, which CI blows through quickly.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var version = (document.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null)
            ?.TrimStart('v', 'V') ?? throw new InvalidOperationException("The latest release has no tag.");

        var wanted = DesktopBuilds.Current()?.AssetName;
        if (wanted is null || !document.RootElement.TryGetProperty("assets", out var assets))
        {
            return new DesktopRelease(version, null, null);
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return new DesktopRelease(
                    version,
                    asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() : null,
                    asset.TryGetProperty("size", out var size) ? size.GetInt64() : null);
            }
        }

        // The release exists but its archives are still uploading, or this OS has no build.
        return new DesktopRelease(version, null, null);
    }

    /// <summary>
    /// Downloads and extracts <paramref name="release"/> over any previous install. The version marker
    /// is written last, so an interrupted install reports the old version rather than claiming success.
    /// </summary>
    public async Task InstallAsync(DesktopRelease release, IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        if (release.DownloadUrl is not { } url)
        {
            throw new InvalidOperationException(
                $"Release v{release.Version} has no build for this machine ({DesktopBuilds.Current()?.Rid ?? "unknown platform"}).");
        }

        Directory.CreateDirectory(InstallRoot);
        var archive = Path.Combine(Path.GetTempPath(), $"StrideAssetStore-{release.Version}{Extension(url)}");

        try
        {
            await DownloadAsync(url, archive, release.SizeBytes, progress, cancellation);

            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, InstallRoot, overwriteFiles: true);
            }
            else
            {
                await using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, InstallRoot, overwriteFiles: true, cancellation);
            }

            var exe = ExecutablePath()
                ?? throw new InvalidOperationException("The archive contained no StrideAssetStore.Desktop executable.");

            // Zip carries no permission bits, and tar only does when it was built on Unix — either
            // way the file has to be runnable or the install is decorative.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            await File.WriteAllTextAsync(VersionMarker, release.Version, cancellation);
        }
        finally
        {
            try
            {
                File.Delete(archive);
            }
            catch
            {
                // A leftover temp archive is not worth failing an otherwise successful install.
            }
        }
    }

    private async Task DownloadAsync(string url, string destination, long? expectedSize,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? expectedSize;
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
            written += read;
            if (total is > 0)
            {
                progress?.Report(Math.Min(100, written * 100.0 / total.Value));
            }
        }
    }

    private static string Extension(string url) =>
        url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";

    private static (string Owner, string Repo) ParseRepo(string repoUrl)
    {
        var parts = repoUrl.TrimEnd('/').Split('/');
        return parts.Length >= 2 ? (parts[^2], parts[^1]) : ("Nicogo1705", "StrideAssetStore");
    }
}
