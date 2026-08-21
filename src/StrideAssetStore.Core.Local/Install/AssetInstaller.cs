// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using StrideAssetStore.Core.Local.Git;
using StrideAssetStore.Core.Local.Hashing;
using StrideAssetStore.Core.Models;
using StrideAssetStore.Core.Local.Projects;

namespace StrideAssetStore.Core.Local.Install;

/// <summary>A filesystem entry shown by the project picker.</summary>
public enum FsKind { Directory, Solution, Project }

public sealed record FsEntry(string Name, string Path, FsKind Kind);

/// <summary>Result of an install attempt.</summary>
public sealed record InstallResult(bool Success, IReadOnlyList<string> Messages);

/// <summary>A store asset referenced by a specific project (via ProjectReference or PackageReference).</summary>
public sealed record ProjectAsset(
    string Id,
    string Name,
    string Status,           // up-to-date | outdated | unknown | broken | missing
    string InstalledCommit,
    string? LatestCommit,
    string Kind,             // "local" | "nuget"
    string CloneRoot,        // local: the clone folder on disk (else "")
    string ReferencedCsproj, // local: absolute path of the referenced .csproj
    string? PackageId,       // nuget: the package id
    string RawInclude = "",  // local: the verbatim csproj Include (needed to remove global-cache refs)
    string Ref = "",         // local: the ref the clone follows, read from its cache path ("" = legacy)
    string? StrideVersion = null, // Stride version the asset targets (from its csproj / the index)
    string? Fork = null);    // owner/repo when installed from a fork instead of the asset's own repo

/// <summary>An asset clone sitting in the shared cache (the My assets page).</summary>
public sealed record CachedAsset(
    string Id,               // "" when the manifest is unreadable
    string Name,
    string Ref,              // followed ref from the path ("" = legacy flat layout)
    string CloneRoot,
    string InstalledCommit,
    string Status,           // up-to-date | outdated | unknown | broken
    long SizeBytes);

/// <summary>A non-store dependency of a project: a NuGet package (with version) or a plain
/// ProjectReference (version null).</summary>
public sealed record ProjectDep(string Name, string? Version);

/// <summary>A project within a solution and the store assets it references.</summary>
public sealed record ProjectNode(string Name, string CsprojPath, IReadOnlyList<ProjectAsset> Assets,
    string? StrideVersion = null,               // Stride version the project targets (null when none detected)
    IReadOnlyList<ProjectDep>? Dependencies = null); // everything else it references (packages + plain projects)

/// <summary>A store-asset project still listed in a solution whose files are gone (a "Remove" that
/// never cleaned the .sln). Kept visible so the user can download the asset again or drop the entry.</summary>
public sealed record StoreSolutionProject(string Name, string CsprojPath, string? AssetId);

/// <summary>A solution (or lone project) and its projects' store assets, plus any stale store entries.</summary>
public sealed record SolutionView(
    string Path, string Name,
    IReadOnlyList<ProjectNode> Projects,
    IReadOnlyList<StoreSolutionProject> Dangling);

/// <summary>
/// Installs and manages store assets on a real machine: browse the filesystem, read a solution's
/// projects, clone an asset (and its dependencies) and add the reference, then keep it up to date.
/// Shared by the desktop app and the CLI — needs a filesystem and git, so never the browser.
/// </summary>
public sealed class AssetInstaller(GitClient? git = null)
{
    private readonly GitClient _git = git ?? new GitClient();

    /// <summary>Lists directories and .sln/.slnx/.csproj files at a path (drives when path is null).</summary>
    public IReadOnlyList<FsEntry> Browse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new FsEntry(d.Name, d.RootDirectory.FullName, FsKind.Directory))
                .ToList();
        }

        var full = Path.GetFullPath(path);
        var entries = new List<FsEntry>();

        var parent = Directory.GetParent(full);
        if (parent is not null)
        {
            entries.Add(new FsEntry("..", parent.FullName, FsKind.Directory));
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(full).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(new FsEntry(Path.GetFileName(dir), dir, FsKind.Directory));
            }

            foreach (var file in Directory.GetFiles(full).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".sln" or ".slnx")
                {
                    entries.Add(new FsEntry(Path.GetFileName(file), file, FsKind.Solution));
                }
                else if (ext == ".csproj")
                {
                    entries.Add(new FsEntry(Path.GetFileName(file), file, FsKind.Project));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Folder not readable (permissions, special system folders) — show just the parent entry.
        }

        return entries;
    }

    /// <summary>
    /// Returns the candidate target projects for a picked .sln/.slnx/.csproj — excluding store-asset
    /// projects, which live in the solution only so Visual Studio can load the ProjectReferences (you
    /// don't install an asset into another asset's project).
    /// </summary>
    public IReadOnlyList<SolutionProject> ReadTargets(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var projects = ext == ".csproj"
            ? new List<SolutionProject> { new(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path)) }
            : SolutionInspector.ReadProjects(path).ToList();

        return projects.Where(p => !IsStoreAssetProject(Path.GetFullPath(p.Path))).ToList();
    }

    /// <summary>
    /// True when a .csproj is an installed store asset (not a user project): either it sits under the
    /// global asset cache (matches even after its files were deleted, leaving a dangling .sln entry),
    /// or an ancestor has an <c>AssetData/manifest.json</c> (a forked asset checked out elsewhere).
    /// </summary>
    private static bool IsStoreAssetProject(string csprojAbs) =>
        csprojAbs.StartsWith(GlobalCacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || FindStoreClone(csprojAbs) is not null;

    /// <summary>
    /// Lists store-asset projects that a solution still references but whose <c>.csproj</c> is gone from
    /// disk — the stale entries left when an asset's clone was deleted (a "Remove" that never cleaned the
    /// .sln). They aren't removed automatically: a missing file may just be a shared-source dependency the
    /// user hasn't downloaded yet, so we surface them for the user to download again or drop. No-op for a
    /// lone .csproj target.
    /// </summary>
    public IReadOnlyList<StoreSolutionProject> ListDanglingStoreProjects(
        string solutionOrCsproj, IReadOnlyDictionary<string, IndexedAsset> catalog)
    {
        var ext = Path.GetExtension(solutionOrCsproj).ToLowerInvariant();
        if (ext is not (".sln" or ".slnx"))
        {
            return [];
        }

        List<SolutionProject> projects;
        try
        {
            projects = SolutionInspector.ReadProjects(solutionOrCsproj).ToList();
        }
        catch
        {
            return [];
        }

        var result = new List<StoreSolutionProject>();
        foreach (var p in projects)
        {
            var abs = Path.GetFullPath(p.Path);
            if (!IsStoreAssetProject(abs) || File.Exists(abs))
            {
                continue;
            }

            // Try to map the (global-cache) clone folder back to a catalog asset so the UI can offer
            // to re-download it rather than only remove the stale entry.
            string? assetId = null;
            if (abs.StartsWith(GlobalCacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var (_, folder) = GlobalCachePartsOf(abs);
                assetId = catalog.Values.FirstOrDefault(a =>
                    string.Equals(GitClient.SafeRepoFolderName(a.Repo), folder, StringComparison.OrdinalIgnoreCase))?.Id;
            }

            result.Add(new StoreSolutionProject(p.Name, abs, assetId));
        }

        return result;
    }

    /// <summary>Removes a single project entry from a .sln/.slnx by path (works even if the file is gone).
    /// Returns true on success.</summary>
    public bool RemoveFromSolution(string solutionOrCsproj, string csprojPath)
    {
        var ext = Path.GetExtension(solutionOrCsproj).ToLowerInvariant();
        if (ext is not (".sln" or ".slnx"))
        {
            return false;
        }

        var (exitCode, _, _) = RunDotnet(["sln", solutionOrCsproj, "remove", csprojPath],
            Path.GetDirectoryName(Path.GetFullPath(solutionOrCsproj)));
        return exitCode == 0;
    }

    /// <summary>
    /// The per-machine global asset cache. Assets installed in "global" mode are cloned here, and the
    /// project reference is written with an MSBuild property-function path that resolves to this folder on
    /// <em>any</em> machine — so the source can be shared and teammates just download the assets.
    /// Layout: <c>Assets\&lt;ref&gt;\&lt;repo-folder&gt;</c> — the followed ref (branch or tag) is part of
    /// the path, so different versions coexist and "up-to-date" is checked against THAT ref, not blindly
    /// against the index's latest. (Clones from before this layout sit directly under Assets\ and are
    /// still recognized.)
    /// </summary>
    public static string GlobalCacheRoot => Path.Combine(AppRoot, "Assets");

    /// <summary>Per-machine root for everything the store keeps locally: the asset cache, the
    /// catalog snapshot the CLI reads when offline, and any installed copy of the desktop app.</summary>
    public static string AppRoot => Path.Combine(AppDataRoot, "StrideAssetStore");

    // Test seam: on Windows GetFolderPath uses the shell API, not the APPDATA variable, so tests
    // can't redirect the cache via the environment. Product code never sets this.
    internal static string? AppDataOverride;

    private static string AppDataRoot =>
        AppDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>A ref name as a filesystem-safe single folder name (e.g. "feature/x" → "feature-x").</summary>
    public static string SafeRefFolderName(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return "unknown";
        }

        var safe = new string(reference.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray());
        return safe.Trim('-', '.') is { Length: > 0 } trimmed ? trimmed : "unknown";
    }

    // MSBuild resolves this to GlobalCacheRoot at evaluation time, on every machine/OS.
    private const string GlobalCacheInclude =
        @"$([System.Environment]::GetFolderPath(SpecialFolder.ApplicationData))\StrideAssetStore\Assets";

    private const string GlobalCacheMarker = "$([System.Environment]::GetFolderPath(SpecialFolder.ApplicationData))";

    /// <summary>
    /// Installs <paramref name="asset"/> at <paramref name="reference"/> into each target project by cloning
    /// the asset (and resolved dependencies) into the shared per-machine cache and adding a ProjectReference
    /// resolved through an MSBuild property function, so the reference stays valid on any machine.
    /// </summary>
    /// <remarks>
    /// There is deliberately no "clone into my project" mode. It produced a relative reference that only
    /// worked on one machine, and putting an asset package inside the consuming project made the asset
    /// compiler load it twice and the SDK sweep its build output into the game's own compile items. To own
    /// and modify an asset, fork its repository — that is a different thing, and it is what forking is for.
    /// </remarks>
    /// <param name="asset">The catalog entry being installed.</param>
    /// <param name="reference">Git ref to check out (branch, tag or commit).</param>
    /// <param name="targetCsprojPaths">Projects that should reference the asset.</param>
    /// <param name="catalog">The catalog, used to resolve the asset's dependencies.</param>
    /// <param name="solutionPath">Solution to register the cloned projects in, when there is one.</param>
    /// <param name="fork">
    /// <c>owner/repo</c> to install from instead of the asset's own repository. A fork has its own
    /// tags and its own history, so neither the registry's content hash nor its certified commits
    /// apply — both checks are skipped, and the install says so.
    /// </param>
    public InstallResult Install(
        IndexedAsset asset,
        string reference,
        IReadOnlyList<string> targetCsprojPaths,
        IReadOnlyDictionary<string, IndexedAsset> catalog,
        string? solutionPath = null,
        string? fork = null)
    {
        var messages = new List<string>();

        if (!_git.IsAvailable())
        {
            return new InstallResult(false, ["git was not found on PATH."]);
        }

        if (targetCsprojPaths.Count == 0)
        {
            return new InstallResult(false, ["Select at least one target project."]);
        }

        try
        {
            var storeRoot = GlobalCacheRoot;

            // Versioned layout: <root>/<ref>/<repo-folder> — the followed ref is part of the path.
            var refRoot = Path.Combine(storeRoot, SafeRefFolderName(reference));
            Directory.CreateDirectory(refRoot);

            // Clone the asset plus its resolved dependencies (so inter-asset references resolve), verifying
            // each against the content hash the index recorded (integrity for the whole set, not just the root).
            // A fork replaces the root asset's repository; its dependencies still come from the registry.
            var rootRepo = fork is null ? asset.Repo : ForkRepoUrl(fork);
            // A fork gets its own folder: same repo name, different owner, and it must never land on
            // top of the asset it forked — that clone is shared by every project on this machine.
            var assetFolder = Clone(rootRepo, reference, refRoot, messages,
                fork is null ? null : GitClient.SafeForkFolderName(fork),
                assetDataOnly: fork is null);
            if (fork is null)
            {
                VerifyHash(refRoot, assetFolder, string.Equals(reference, asset.Latest.Ref, StringComparison.Ordinal) ? asset.Latest.ContentHash : null, asset.Manifest.Name, messages);
                VerifyCertifiedCommit(refRoot, assetFolder, asset, reference, messages);
            }
            else
            {
                // Not a warning about the fork being bad — a statement of what stops applying.
                messages.Add($"⚠ Fork install: {fork} at '{reference}'. Its content is not the registry's, "
                    + "so the content hash isn't verified and no certification applies. You trust the fork's owner.");
            }

            var missingDeps = false;
            var clonedCsprojs = new List<string>(); // asset + dep .csprojs, to register in the solution
            foreach (var depId in asset.Latest.ResolvedDependencies)
            {
                if (catalog.TryGetValue(depId, out var dep))
                {
                    // Deps live in the SAME ref folder as the dependent asset: inter-asset
                    // ProjectReferences are relative ("../<depFolder>/AssetData/…"), so the dep
                    // must sit next to the asset for them to resolve.
                    var depFolder = Clone(dep.Repo, dep.Latest.Ref, refRoot, messages);
                    VerifyHash(refRoot, depFolder, dep.Latest.ContentHash, dep.Manifest.Name, messages);
                    var depCsproj = CsprojInspector.FindProjects(Path.Combine(refRoot, depFolder, "AssetData")).FirstOrDefault();
                    if (depCsproj is not null)
                    {
                        clonedCsprojs.Add(depCsproj);
                    }
                }
                else
                {
                    missingDeps = true;
                    messages.Add($"⚠ Dependency '{depId}' is not in the catalog — the project won't compile until it's available.");
                }
            }

            var assetData = Path.Combine(refRoot, assetFolder, "AssetData");
            var clonedRoot = Path.Combine(refRoot, assetFolder);
            var assetCsproj = CsprojInspector.FindProjects(assetData).FirstOrDefault();

            // AssetData/ is the registry's convention, and the registry has no say over a fork:
            // someone who restructured their copy for their own game still has a project worth
            // referencing. Look through the whole clone before giving up on it — which is why a
            // fork is cloned in full (ShallowClone assetDataOnly: false) and a registry asset isn't.
            if (assetCsproj is null && fork is not null)
            {
                var elsewhere = CsprojInspector.FindProjects(clonedRoot);
                if (elsewhere.Count == 1)
                {
                    assetCsproj = elsewhere[0];
                    messages.Add($"• {fork} has no AssetData/ layout; referencing "
                        + $"{Path.GetRelativePath(clonedRoot, assetCsproj)} instead.");
                }
                else if (elsewhere.Count > 1)
                {
                    // One fact per message: the UI prints them as a list, and a single run-on
                    // sentence carrying a cache path was unreadable.
                    messages.Add($"✗ {fork} has no AssetData/ folder and {elsewhere.Count} projects — nothing says which one to reference.");
                    messages.Add($"• The clone is kept so you can point at a project yourself: {clonedRoot}");
                    return new InstallResult(false, messages);
                }
            }

            if (assetCsproj is null)
            {
                // Kept, not deleted: it is the user's clone of a repository they named, and they may
                // want to look at it. `list --cached` shows it and `remove --delete-clone` drops it.
                messages.Add(fork is null
                    ? $"✗ No .csproj anywhere in the clone — {asset.Repo} doesn't look like a store asset."
                    : $"✗ No .csproj anywhere in {fork} — nothing to reference.");
                messages.Add($"• The clone is kept at {clonedRoot}; nothing was written to your project.");
                return new InstallResult(false, messages);
            }

            clonedCsprojs.Insert(0, assetCsproj);

            // Portable: MSBuild resolves this to the cache root on whatever machine opens the project.
            var globalInclude = $"{GlobalCacheInclude}\\{Path.GetRelativePath(storeRoot, assetCsproj).Replace('/', '\\')}";

            // Each target is edited independently so one locked/malformed .csproj can't leave the batch half-done.
            var anyTargetError = false;
            foreach (var target in targetCsprojPaths)
            {
                try
                {
                    var added = CsprojEditor.AddRawProjectReference(target, globalInclude, fork);
                    messages.Add(added
                        ? $"✓ Added reference to {Path.GetFileName(target)}"
                        : $"• {Path.GetFileName(target)} already references the asset");
                }
                catch (Exception ex)
                {
                    anyTargetError = true;
                    messages.Add($"✗ {Path.GetFileName(target)}: {ex.Message}");
                }
            }

            // Register the asset (and its deps) in the solution so Visual Studio can load the referenced
            // projects — a ProjectReference to a project that isn't in the .sln shows as "project not found".
            AddToSolution(solutionPath, clonedCsprojs, messages);

            messages.Add(fork is null
                ? "✓ Reference is portable — commit your source and teammates just download the asset."
                : $"✓ Reference records the fork — commit it and teammates install from {fork} too.");

            return new InstallResult(!missingDeps && !anyTargetError, messages);
        }
        catch (Exception ex)
        {
            messages.Add($"✗ {ex.Message}");
            return new InstallResult(false, messages);
        }
    }

    /// <summary>
    /// NuGet install: add a <c>&lt;PackageReference&gt;</c> for the asset's published package to each
    /// target project. No source is cloned. Requires the asset to declare a NuGet package.
    /// </summary>
    public InstallResult InstallNuget(IndexedAsset asset, IReadOnlyList<string> targetCsprojPaths)
    {
        var nuget = asset.Manifest.Nuget;
        if (nuget is null)
        {
            return new InstallResult(false, ["This asset is not published on NuGet."]);
        }

        if (targetCsprojPaths.Count == 0)
        {
            return new InstallResult(false, ["Select at least one target project."]);
        }

        var messages = new List<string>();
        try
        {
            foreach (var target in targetCsprojPaths)
            {
                var added = CsprojEditor.AddPackageReference(target, nuget.PackageId, nuget.PackageVersion);
                messages.Add(added
                    ? $"✓ Added package {nuget.PackageId} to {Path.GetFileName(target)}"
                    : $"• {Path.GetFileName(target)} already references {nuget.PackageId}");
            }

            return new InstallResult(true, messages);
        }
        catch (Exception ex)
        {
            messages.Add($"✗ {ex.Message}");
            return new InstallResult(false, messages);
        }
    }

    /// <summary>Updates an installed asset to the tip of a ref. Returns the new commit, or null on failure.</summary>
    public string? UpdateInstalled(string assetDir, string reference) =>
        _git.UpdateToRef(assetDir, reference) ? _git.ResolveCommit(assetDir, "HEAD") : null;

    /// <summary>
    /// Analyses a solution (or lone .csproj): lists its projects and, for each, the store assets it
    /// references — local (a ProjectReference into a cloned <c>AssetData/</c>) or NuGet (a PackageReference
    /// matching a catalog asset's published package) — with an up-to-date/outdated/broken status.
    /// </summary>
    public SolutionView Analyze(string solutionOrCsproj, IReadOnlyDictionary<string, IndexedAsset> catalog)
    {
        var full = Path.GetFullPath(solutionOrCsproj);
        var nodes = new List<ProjectNode>();

        IReadOnlyList<SolutionProject> projects;
        try
        {
            projects = ReadTargets(full);
        }
        catch
        {
            projects = [];
        }

        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            // (ReadTargets already excludes store-asset projects — they're in the .sln only so VS loads the refs.)
            var assets = AnalyzeProject(project.Path, catalog);
            nodes.Add(new ProjectNode(project.Name, project.Path, assets,
                SafeDetectStrideVersion(project.Path), ListNonAssetDependencies(project.Path, assets)));
        }

        return new SolutionView(full, Path.GetFileName(full), nodes, ListDanglingStoreProjects(full, catalog));
    }

    private IReadOnlyList<ProjectAsset> AnalyzeProject(
        string csprojPath, IReadOnlyDictionary<string, IndexedAsset> catalog)
    {
        var assets = new List<ProjectAsset>();
        if (!File.Exists(csprojPath))
        {
            return assets;
        }

        // Local installs: ProjectReferences that point into a cloned store asset.
        foreach (var (include, fork) in SafeProjectReferences(csprojPath))
        {
            var referenced = ResolveInclude(csprojPath, include);
            var clone = FindStoreClone(referenced);
            if (clone is null)
            {
                // A portable reference into the global cache whose asset isn't downloaded on this machine yet:
                // surface it as "missing" so the user can fetch it with one click (the shared-source workflow).
                if (referenced.StartsWith(GlobalCacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var (refName, folder) = GlobalCachePartsOf(referenced);

                    // A fork's folder is <repo>__<owner>, which matches no catalog repository — so
                    // match on the fork's own repository name instead. Without this, a teammate who
                    // clones the project sees a nameless "missing" row and no clue what to fetch.
                    var lookup = fork is null ? folder : GitClient.SafeRepoFolderName(ForkRepoUrl(fork));
                    var known = catalog.Values.FirstOrDefault(a =>
                        string.Equals(GitClient.SafeRepoFolderName(a.Repo), lookup, StringComparison.OrdinalIgnoreCase));
                    assets.Add(new ProjectAsset(
                        known?.Id ?? "", known?.Manifest.Name ?? folder, "missing", "", known?.Latest.Commit,
                        "local", Path.Combine(GlobalCacheRoot, refName ?? "", folder), referenced, null, include,
                        refName ?? "", null, fork));
                }

                continue; // otherwise an ordinary ProjectReference, not a store asset
            }

            var (cloneRoot, hasManifest) = clone.Value;
            if (!hasManifest)
            {
                assets.Add(new ProjectAsset(
                    "", Path.GetFileName(cloneRoot), "broken", "", null, "local", cloneRoot, referenced, null, include,
                    "", null, fork));
                continue;
            }

            var manifest = TryReadManifest(Path.Combine(cloneRoot, "AssetData", "manifest.json"));
            if (manifest is null)
            {
                assets.Add(new ProjectAsset(
                    "", Path.GetFileName(cloneRoot), "broken", "", null, "local", cloneRoot, referenced, null, include,
                    "", null, fork));
                continue;
            }

            var installed = _git.ResolveCommit(cloneRoot, "HEAD") ?? "";
            catalog.TryGetValue(manifest.Id, out var entry);
            // Compare against the ref the clone's path says it follows: a v1.0.0 clone is judged
            // against the v1.0.0 tag commit, not against the moving latest.
            var followedRef = RefOfClone(cloneRoot);
            // A fork has its own history: the registry's commit for this asset says nothing about it,
            // so ask the fork's remote what its ref points at instead.
            var expected = fork is null
                ? ExpectedCommitFor(entry, followedRef)
                : _git.ResolveRemoteCommit(ForkRepoUrl(fork), followedRef ?? "HEAD");
            assets.Add(new ProjectAsset(
                manifest.Id, manifest.Name, StatusOf(installed, expected),
                installed, expected, "local", cloneRoot, referenced, null, include, followedRef ?? "",
                // What the INSTALLED clone actually targets (its csproj), not the index's opinion.
                SafeDetectStrideVersion(referenced) ?? entry?.Latest.DetectedStrideVersion, fork));
        }

        // NuGet installs: PackageReferences matching a catalog asset's published package.
        foreach (var (name, version) in SafePackageReferences(csprojPath))
        {
            var match = catalog.Values.FirstOrDefault(a =>
                a.Manifest.Nuget is { } n && string.Equals(n.PackageId, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                assets.Add(new ProjectAsset(
                    match.Id, match.Manifest.Name, "unknown", version ?? "", null, "nuget", "", csprojPath, name,
                    StrideVersion: match.Latest.DetectedStrideVersion));
            }
        }

        return assets;
    }

    /// <summary>
    /// Everything the project references that is NOT a store asset: NuGet packages (with their
    /// version) and plain ProjectReferences (short name, no version). Store assets are excluded —
    /// they have their own rows.
    /// </summary>
    private IReadOnlyList<ProjectDep> ListNonAssetDependencies(string csprojPath, IReadOnlyList<ProjectAsset> assets)
    {
        var deps = new List<ProjectDep>();
        if (!File.Exists(csprojPath))
        {
            return deps;
        }

        var assetPackages = assets.Where(a => a.Kind == "nuget" && a.PackageId is not null)
            .Select(a => a.PackageId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, version) in SafePackageReferences(csprojPath))
        {
            if (!assetPackages.Contains(name))
            {
                deps.Add(new ProjectDep(name, version));
            }
        }

        var assetProjects = assets.Where(a => a.Kind == "local")
            .Select(a => a.ReferencedCsproj).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (include, fork) in SafeProjectReferences(csprojPath))
        {
            var referenced = ResolveInclude(csprojPath, include);
            if (!assetProjects.Contains(referenced))
            {
                deps.Add(new ProjectDep(Path.GetFileNameWithoutExtension(referenced), null));
            }
        }

        return deps;
    }

    /// <summary><see cref="CsprojInspector.DetectStrideVersion"/> that returns null instead of throwing
    /// (missing file, malformed xml).</summary>
    private static string? SafeDetectStrideVersion(string csprojPath)
    {
        try
        {
            return File.Exists(csprojPath) ? CsprojInspector.DetectStrideVersion(csprojPath) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Switches a project's reference on a store asset to another ref: ensures the target ref's
    /// clone exists in the versioned global cache (downloading it if needed), then swaps the
    /// ProjectReference and registers the new asset csproj in the solution. The old clone stays
    /// (other projects may still follow it).
    /// </summary>
    /// <summary>
    /// Moves a project between the asset's own repository and a fork of it (either direction), at
    /// <paramref name="newRef"/>. The new source is installed first: if cloning or editing fails,
    /// the project is still building against what it had.
    /// </summary>
    /// <param name="asset">The catalog entry, used for dependencies and for the official repository.</param>
    /// <param name="current">The reference being replaced.</param>
    /// <param name="csprojPath">The project to edit.</param>
    /// <param name="newRef">Branch, tag or commit to follow in the new source.</param>
    /// <param name="fork"><c>owner/repo</c> to switch to, or null to go back to the official asset.</param>
    /// <param name="catalog">The catalog, for resolving dependencies.</param>
    /// <param name="solutionPath">Solution to register the cloned projects in, when there is one.</param>
    public InstallResult SwitchSource(
        IndexedAsset asset, ProjectAsset current, string csprojPath, string newRef, string? fork,
        IReadOnlyDictionary<string, IndexedAsset> catalog, string? solutionPath)
    {
        // Same source and same ref: removing the old reference after adding the identical new one
        // would leave the project with none.
        if (string.Equals(current.Fork, fork, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Ref, newRef, StringComparison.Ordinal))
        {
            return new InstallResult(true, [$"• {asset.Manifest.Name} already follows {Describe(fork)} at '{newRef}'."]);
        }

        var messages = new List<string>();
        var installed = Install(asset, newRef, [csprojPath], catalog, solutionPath, fork);
        messages.AddRange(installed.Messages);
        if (!installed.Success)
        {
            messages.Add("✗ Left the project on its previous source.");
            return new InstallResult(false, messages);
        }

        // Only drop the old reference when the install actually wrote a different one. Two refs can
        // land in the same cache folder — SafeRefFolderName turns every non-alphanumeric character
        // into '-', so "feature/x" and "feature-x" collapse — and removing it then would leave the
        // project with no reference at all, reported as a successful switch.
        var newCloneFolder = fork is null
            ? GitClient.SafeRepoFolderName(asset.Repo)
            : GitClient.SafeForkFolderName(fork);
        if (ReferenceStillPointsElsewhere(csprojPath, current.RawInclude, newRef, newCloneFolder))
        {
            CsprojEditor.RemoveRawProjectReference(csprojPath, current.RawInclude);
        }

        messages.Add($"✓ Switched {asset.Manifest.Name} to {Describe(fork)} at '{newRef}'.");
        return new InstallResult(true, messages);
    }

    private static string Describe(string? fork) => fork is null ? "the official asset" : $"the fork {fork}";

    /// <summary>
    /// Whether the reference a project held before a switch points somewhere other than the clone the
    /// switch just installed — that is, whether removing it is safe.
    /// </summary>
    private static bool ReferenceStillPointsElsewhere(string csprojPath, string rawInclude, string newRef, string newCloneFolder)
    {
        try
        {
            var expanded = rawInclude.Replace(GlobalCacheInclude, GlobalCacheRoot, StringComparison.Ordinal);
            var oldFull = Path.GetFullPath(Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(Path.GetDirectoryName(csprojPath) ?? ".", expanded));

            var newClone = Path.GetFullPath(Path.Combine(GlobalCacheRoot, SafeRefFolderName(newRef), newCloneFolder));

            return !oldFull.StartsWith(newClone + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // An include we can't resolve is not one we should delete on a guess.
            return false;
        }
    }

    /// <summary>
    /// Moves a project onto another ref of the SAME source it already follows. A project installed
    /// from a fork stays on that fork: re-resolving against the registry's repository here would
    /// have quietly walked the user back to the official asset while they asked for a version.
    /// </summary>
    public InstallResult SwitchRef(
        IndexedAsset asset, ProjectAsset current, string csprojPath, string newRef,
        IReadOnlyDictionary<string, IndexedAsset> catalog, string? solutionPath) =>
        SwitchSource(asset, current, csprojPath, newRef, current.Fork, catalog, solutionPath);

    /// <summary>Every csproj under a clone's AssetData — the surface a shared retarget touches.</summary>
    public IReadOnlyList<string> CloneCsprojs(string cloneRoot) =>
        CsprojInspector.FindProjects(Path.Combine(cloneRoot, "AssetData")).ToList();

    /// <summary>Retargets the Stride.* packages of the given project files; returns how many changed.</summary>
    public int RetargetStride(IEnumerable<string> csprojPaths, string strideVersion)
    {
        var changed = 0;
        foreach (var path in csprojPaths.Where(File.Exists))
        {
            if (CsprojEditor.RetargetStridePackages(path, strideVersion))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>Removes a local asset's ProjectReference, matched by its verbatim Include (works for both
    /// relative and global-cache references). Returns true if modified.</summary>
    public bool UninstallLocal(string csprojPath, string rawInclude) =>
        CsprojEditor.RemoveRawProjectReference(csprojPath, rawInclude);

    /// <summary>Removes a NuGet asset's PackageReference from a project. Returns true if modified.</summary>
    public bool UninstallNuget(string csprojPath, string packageId) =>
        CsprojEditor.RemovePackageReference(csprojPath, packageId);

    /// <summary>Deletes a cloned asset folder from disk (used when no project references it any more).</summary>
    public bool DeleteClone(string cloneRoot)
    {
        if (string.IsNullOrWhiteSpace(cloneRoot) || !Directory.Exists(cloneRoot))
        {
            return false;
        }

        ForceDeleteDirectory(cloneRoot);
        return true;
    }

    /// <summary>
    /// Recursively deletes a directory, first clearing read-only attributes. Git marks files under
    /// <c>.git</c> (pack/object files) read-only, which makes a plain <see cref="Directory.Delete(string, bool)"/>
    /// throw <see cref="UnauthorizedAccessException"/> on Windows.
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
                // best-effort; Directory.Delete will surface any real problem
            }
        }

        Directory.Delete(path, recursive: true);
    }

    private static string StatusOf(string installedCommit, string? latestCommit) =>
        latestCommit is null ? "unknown"
        : string.Equals(latestCommit, installedCommit, StringComparison.OrdinalIgnoreCase) ? "up-to-date"
        : "outdated";

    private static string ResolveInclude(string csprojPath, string include)
    {
        // Expand the global-cache MSBuild property function to the real folder, mirroring what MSBuild does.
        var expanded = include.Contains(GlobalCacheMarker, StringComparison.OrdinalIgnoreCase)
            ? include.Replace(GlobalCacheMarker, AppDataRoot, StringComparison.OrdinalIgnoreCase)
            : include;
        expanded = expanded.Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
        return Path.GetFullPath(Path.Combine(dir, expanded));
    }

    /// <summary>
    /// Splits a path under the global cache into (ref, repo folder). Handles both layouts:
    /// versioned <c>Assets\&lt;ref&gt;\&lt;folder&gt;\AssetData\…</c> and legacy
    /// <c>Assets\&lt;folder&gt;\AssetData\…</c> (ref null).
    /// </summary>
    private static (string? Ref, string Folder) GlobalCachePartsOf(string resolvedPath)
    {
        var rel = Path.GetRelativePath(GlobalCacheRoot, resolvedPath);
        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var versioned = segments.Length >= 2
            && !segments[1].Equals("AssetData", StringComparison.OrdinalIgnoreCase);
        return versioned ? (segments[0], segments[1]) : (null, segments[0]);
    }

    /// <summary>The ref a clone follows, read from its cache path (null for legacy/unversioned clones).</summary>
    private static string? RefOfClone(string cloneRoot)
    {
        if (cloneRoot.StartsWith(GlobalCacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return GlobalCachePartsOf(Path.Combine(cloneRoot, "AssetData")).Ref;
        }

        // Local layout <chosen-dir>/<ref>/<folder> — take the parent folder as the candidate ref.
        return Path.GetFileName(Path.GetDirectoryName(cloneRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }

    /// <summary>
    /// The commit the installed clone SHOULD be at, given the ref its path says it follows:
    /// a certified/tagged ref pins its own commit; the tracked branch compares to the index's
    /// latest; anything unknown falls back to latest.
    /// </summary>
    private static string? ExpectedCommitFor(IndexedAsset? entry, string? refName)
    {
        if (entry is null)
        {
            return null;
        }

        if (refName is null || string.Equals(refName, entry.Latest.Ref, StringComparison.OrdinalIgnoreCase))
        {
            return entry.Latest.Commit;
        }

        var tagged = entry.Versions.FirstOrDefault(v =>
                string.Equals(v.Tag, refName, StringComparison.OrdinalIgnoreCase))?.Commit
            ?? entry.Certified.FirstOrDefault(c =>
                string.Equals(c.Tag, refName, StringComparison.OrdinalIgnoreCase))?.Commit;
        return tagged ?? entry.Latest.Commit;
    }

    /// <summary>
    /// Clones an asset (and its resolved deps) into the global cache — used to fetch a "missing" reference.
    /// When <paramref name="solutionPath"/> is given, the fetched projects are also registered in that solution.
    /// </summary>
    public InstallResult DownloadToCache(IndexedAsset asset, IReadOnlyDictionary<string, IndexedAsset> catalog, string? solutionPath = null, string? refFolder = null, string? fork = null)
    {
        var messages = new List<string>();
        if (!_git.IsAvailable())
        {
            return new InstallResult(false, ["git was not found on PATH."]);
        }

        try
        {
            // Fetch WHERE the broken reference points: a legacy include goes to the flat layout,
            // a versioned one to Assets/<ref>/ — and the checkout follows that ref, not blindly latest.
            var checkoutRef = string.IsNullOrEmpty(refFolder) ? asset.Latest.Ref : refFolder;
            var targetRoot = string.IsNullOrEmpty(refFolder)
                ? GlobalCacheRoot
                : Path.Combine(GlobalCacheRoot, SafeRefFolderName(refFolder));
            Directory.CreateDirectory(targetRoot);
            // Same rule as an install: a fork replaces the root repository and gets its own cache
            // folder, and neither the content hash nor the certified commits describe it.
            var folder = Clone(fork is null ? asset.Repo : ForkRepoUrl(fork), checkoutRef, targetRoot, messages,
                fork is null ? null : GitClient.SafeForkFolderName(fork),
                assetDataOnly: fork is null);

            if (fork is null)
            {
                VerifyHash(targetRoot, folder,
                    string.Equals(checkoutRef, asset.Latest.Ref, StringComparison.Ordinal) ? asset.Latest.ContentHash : null,
                    asset.Manifest.Name, messages);
                VerifyCertifiedCommit(targetRoot, folder, asset, checkoutRef, messages);
            }
            else
            {
                messages.Add($"⚠ Fork: {fork} at '{checkoutRef}'. Not the registry's content, so the hash "
                    + "isn't verified and no certification applies.");
            }
            var clonedCsprojs = CsprojInspector.FindProjects(Path.Combine(targetRoot, folder, "AssetData")).Take(1).ToList();

            var missing = false;
            foreach (var depId in asset.Latest.ResolvedDependencies)
            {
                if (catalog.TryGetValue(depId, out var dep))
                {
                    var depFolder = Clone(dep.Repo, dep.Latest.Ref, targetRoot, messages);
                    VerifyHash(targetRoot, depFolder, dep.Latest.ContentHash, dep.Manifest.Name, messages);
                    clonedCsprojs.AddRange(CsprojInspector.FindProjects(Path.Combine(targetRoot, depFolder, "AssetData")).Take(1));
                }
                else
                {
                    missing = true;
                    messages.Add($"⚠ Dependency '{depId}' is not in the catalog.");
                }
            }

            AddToSolution(solutionPath, clonedCsprojs, messages);
            return new InstallResult(!missing, messages);
        }
        catch (Exception ex)
        {
            messages.Add($"✗ {ex.Message}");
            return new InstallResult(false, messages);
        }
    }

    /// <summary>
    /// Everything sitting in the shared cache, both layouts (versioned <c>Assets\&lt;ref&gt;\&lt;name&gt;</c>
    /// and legacy flat), with the status computed against the ref each clone follows.
    /// </summary>
    public IReadOnlyList<CachedAsset> ListCachedAssets(IReadOnlyDictionary<string, IndexedAsset> catalog)
    {
        var result = new List<CachedAsset>();
        if (!Directory.Exists(GlobalCacheRoot))
        {
            return result;
        }

        void Scan(string cloneRoot, string refName)
        {
            var manifestPath = Path.Combine(cloneRoot, "AssetData", "manifest.json");
            if (!Directory.Exists(Path.Combine(cloneRoot, "AssetData")))
            {
                return; // not a clone (e.g. an empty ref folder)
            }

            var manifest = File.Exists(manifestPath) ? TryReadManifest(manifestPath) : null;
            var installed = _git.ResolveCommit(cloneRoot, "HEAD") ?? "";
            IndexedAsset? entry = null;
            if (manifest is not null)
            {
                catalog.TryGetValue(manifest.Id, out entry);
            }

            var status = manifest is null ? "broken"
                : StatusOf(installed, ExpectedCommitFor(entry, string.IsNullOrEmpty(refName) ? null : refName));
            result.Add(new CachedAsset(
                manifest?.Id ?? "", manifest?.Name ?? Path.GetFileName(cloneRoot), refName, cloneRoot,
                installed, status, DirectorySize(cloneRoot)));
        }

        foreach (var top in Directory.EnumerateDirectories(GlobalCacheRoot))
        {
            if (Directory.Exists(Path.Combine(top, "AssetData")))
            {
                Scan(top, ""); // legacy flat clone
                continue;
            }

            foreach (var nested in Directory.EnumerateDirectories(top))
            {
                Scan(nested, Path.GetFileName(top)); // versioned: <ref>/<name>
            }
        }

        return result
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Ref, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch
        {
            return 0L;
        }
    }

    /// <summary>
    /// Wires an ALREADY-downloaded cache clone into target projects: ProjectReference (portable when
    /// the clone lives in the global cache) + solution registration. No network, no checkout change.
    /// Cached dependencies sitting next to the clone are registered in the solution too.
    /// </summary>
    public InstallResult AttachCached(
        string cloneRoot,
        IReadOnlyList<string> targetCsprojPaths,
        IReadOnlyDictionary<string, IndexedAsset> catalog,
        string? solutionPath = null)
    {
        var messages = new List<string>();
        if (targetCsprojPaths.Count == 0)
        {
            return new InstallResult(false, ["Select at least one target project."]);
        }

        var assetCsproj = CsprojInspector.FindProjects(Path.Combine(cloneRoot, "AssetData")).FirstOrDefault();
        if (assetCsproj is null)
        {
            return new InstallResult(false, ["No .csproj found in the cached asset's AssetData folder."]);
        }

        var inGlobalCache = cloneRoot.StartsWith(GlobalCacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var globalInclude = inGlobalCache
            ? $"{GlobalCacheInclude}\\{Path.GetRelativePath(GlobalCacheRoot, assetCsproj).Replace('/', '\\')}"
            : null;

        // A cached fork lives in a "<repo>__<owner>" folder. Attaching it without recording the fork
        // would leave a reference the registry claims to own: `update` would then resolve it against
        // the official repository and quietly walk the project back off the fork.
        var fork = ForkFromCloneFolder(cloneRoot);

        var clonedCsprojs = new List<string> { assetCsproj };
        var manifest = TryReadManifest(Path.Combine(cloneRoot, "AssetData", "manifest.json"));
        if (manifest is not null && catalog.TryGetValue(manifest.Id, out var entry))
        {
            var refParent = Path.GetDirectoryName(cloneRoot)!;
            foreach (var depId in entry.Latest.ResolvedDependencies)
            {
                if (!catalog.TryGetValue(depId, out var dep))
                {
                    continue;
                }

                var depRoot = Path.Combine(refParent, GitClient.SafeRepoFolderName(dep.Repo));
                var depCsproj = Directory.Exists(depRoot)
                    ? CsprojInspector.FindProjects(Path.Combine(depRoot, "AssetData")).FirstOrDefault()
                    : null;
                if (depCsproj is not null)
                {
                    clonedCsprojs.Add(depCsproj);
                }
                else
                {
                    messages.Add($"⚠ Dependency '{depId}' isn't downloaded next to this clone — install it or the project won't compile.");
                }
            }
        }

        var anyTargetError = false;
        foreach (var target in targetCsprojPaths)
        {
            try
            {
                var added = globalInclude is not null
                    ? CsprojEditor.AddRawProjectReference(target, globalInclude, fork)
                    : CsprojEditor.AddProjectReference(target, assetCsproj, fork);
                messages.Add(added
                    ? $"✓ Added reference to {Path.GetFileName(target)}"
                    : $"• {Path.GetFileName(target)} already references the asset");
            }
            catch (Exception ex)
            {
                anyTargetError = true;
                messages.Add($"✗ {Path.GetFileName(target)}: {ex.Message}");
            }
        }

        AddToSolution(solutionPath, clonedCsprojs, messages);
        return new InstallResult(!anyTargetError, messages);
    }

    /// <summary>
    /// The <c>owner/repo</c> a cached clone came from when its folder is a fork folder
    /// (<c>&lt;repo&gt;__&lt;owner&gt;</c>, written by <see cref="GitClient.SafeForkFolderName"/>), else null.
    /// </summary>
    private static string? ForkFromCloneFolder(string cloneRoot)
    {
        var folder = Path.GetFileName(cloneRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var separator = folder.LastIndexOf("__", StringComparison.Ordinal);
        return separator > 0 && separator + 2 < folder.Length
            ? $"{folder[(separator + 2)..]}/{folder[..separator]}"
            : null;
    }

    // Walks up from a referenced .csproj to the store clone root: the first ancestor with an AssetData/
    // folder. HasManifest distinguishes a healthy asset from a broken/partial clone.
    private static (string Root, bool HasManifest)? FindStoreClone(string referencedCsprojAbs)
    {
        var dir = Path.GetDirectoryName(referencedCsprojAbs);
        while (dir is not null)
        {
            var assetData = Path.Combine(dir, "AssetData");
            if (File.Exists(Path.Combine(assetData, "manifest.json")))
            {
                return (dir, true);
            }

            if (Directory.Exists(assetData))
            {
                return (dir, false);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static AssetManifest? TryReadManifest(string manifestPath)
    {
        try
        {
            return StrideAssetStore.Core.Serialization.StrideAssetStoreJson.Deserialize<AssetManifest>(File.ReadAllText(manifestPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A fork given as <c>owner/repo</c> (or already a URL) as a clonable https URL.</summary>
    public static string ForkRepoUrl(string fork) =>
        fork.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? fork : $"https://github.com/{fork.Trim('/')}";

    private static IReadOnlyList<CsprojInspector.ReferencedProject> SafeProjectReferences(string csprojPath)
    {
        try
        {
            return CsprojInspector.GetProjectReferencesWithMetadata(csprojPath);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<(string Name, string? Version)> SafePackageReferences(string csprojPath)
    {
        try
        {
            return CsprojInspector.GetPackageReferences(csprojPath);
        }
        catch
        {
            return [];
        }
    }

    private string Clone(string repo, string reference, string storeRoot, List<string> messages, string? folderName = null, bool assetDataOnly = true)
    {
        var folder = folderName ?? GitClient.SafeRepoFolderName(repo);
        var dest = Path.Combine(storeRoot, folder);
        if (Directory.Exists(Path.Combine(dest, ".git")))
        {
            // Warn when updating an existing clone actually changes the checked-out commit: in the shared
            // global cache that same folder is referenced by every project, so its version changes for all.
            var before = _git.ResolveCommit(dest, "HEAD");
            if (!_git.UpdateToRef(dest, reference))
            {
                // A failed fetch is only fatal when the checkout isn't already on the ref that was
                // asked for. Offline, an existing clone of a tag is exactly what should still work;
                // what must not happen is reporting "Updated" for an install left on another version.
                if (_git.ResolveCommit(dest, reference) is not { } local || local != before)
                {
                    throw new InvalidOperationException(
                        $"Couldn't update the cached '{folder}' to {reference}. It is still at {Short(before)} "
                        + "— check your network, or that the version still exists in the repository.");
                }

                messages.Add($"• {folder} was already at {reference} ({Short(before)}); couldn't reach the remote to confirm.");
                return folder;
            }

            var after = _git.ResolveCommit(dest, "HEAD");
            // The store root is the cache root itself for legacy clones, a subfolder otherwise.
            var rootFull = Path.GetFullPath(storeRoot);
            var shared = rootFull.Equals(GlobalCacheRoot, StringComparison.OrdinalIgnoreCase)
                || rootFull.StartsWith(GlobalCacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            messages.Add(before != after && shared
                ? $"⚠ Updated shared cache '{folder}' ({Short(before)}→{Short(after)}) — every project referencing it now builds this version."
                : $"• Updated {folder} ({reference})");
        }
        else
        {
            if (Directory.Exists(dest))
            {
                ForceDeleteDirectory(dest);
            }

            _git.ShallowClone(repo, reference, dest, assetDataOnly);
            messages.Add($"✓ Cloned {folder} ({reference})");
        }

        return folder;
    }

    private static string Short(string? commit) =>
        commit is { Length: >= 7 } ? commit[..7] : commit ?? "?";

    /// <summary>
    /// Adds the given projects to a .sln/.slnx under a "Store" solution folder via <c>dotnet sln add</c>,
    /// so Visual Studio loads them (a ProjectReference to a project not in the solution shows as missing).
    /// Idempotent (dotnet reports already-added projects); no-op for a lone .csproj target.
    /// </summary>
    private static void AddToSolution(string? solutionPath, IReadOnlyList<string> csprojPaths, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(solutionPath) || csprojPaths.Count == 0)
        {
            return;
        }

        var ext = Path.GetExtension(solutionPath).ToLowerInvariant();
        if (ext is not (".sln" or ".slnx"))
        {
            return; // a lone .csproj: the ProjectReference alone is enough for a CLI build
        }

        var args = new List<string> { "sln", solutionPath, "add", "--solution-folder", "Store" };
        args.AddRange(csprojPaths);

        try
        {
            var (exitCode, _, stderr) = RunDotnet(args, Path.GetDirectoryName(solutionPath));
            messages.Add(exitCode == 0
                ? $"✓ Registered {csprojPaths.Count} project(s) in {Path.GetFileName(solutionPath)} (Store folder)."
                : $"⚠ Couldn't add the projects to the solution ({Path.GetFileName(solutionPath)}) — add them manually. {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            messages.Add($"⚠ Couldn't run 'dotnet sln add': {ex.Message}");
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunDotnet(IReadOnlyList<string> args, string? workingDirectory)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Unable to start 'dotnet'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // A dotnet hung on the network (NuGet restore against a dead feed) must not freeze the app.
        if (!process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
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
                $"dotnet {args.FirstOrDefault()} timed out after 10 minutes and was killed. {stderr.GetAwaiter().GetResult()}".TrimEnd());
        }

        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    /// <summary>Verifies a cloned asset's AssetData/ against the content hash recorded in the index (best-effort).</summary>
    /// <summary>
    /// For a tag install matching a certified version, proves integrity by commit identity:
    /// git commits are content-addressed, so HEAD == the pinned certified commit means the
    /// content is byte-for-byte what the certifier reviewed — stronger than a content hash,
    /// and it catches a tag that was moved after certification.
    /// </summary>
    private void VerifyCertifiedCommit(string storeRoot, string folder, IndexedAsset asset, string reference, List<string> messages)
    {
        var certified = asset.Certified.FirstOrDefault(c =>
            string.Equals(c.Tag, reference, StringComparison.Ordinal)
            || string.Equals(c.Version, reference, StringComparison.Ordinal));
        if (certified is null || certified.Commit.Length < 7)
        {
            return;
        }

        var head = _git.ResolveCommit(Path.Combine(storeRoot, folder), "HEAD");
        messages.Add(string.Equals(head, certified.Commit, StringComparison.OrdinalIgnoreCase)
            ? $"✓ {asset.Manifest.Name}: certified commit verified ({certified.Commit[..7]})."
            : $"⚠ {asset.Manifest.Name}: '{reference}' no longer points at the certified commit {certified.Commit[..7]} — the tag may have been moved since certification.");
    }

    private static void VerifyHash(string storeRoot, string folder, string? expectedHash, string name, List<string> messages)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            return;
        }

        var assetData = Path.Combine(storeRoot, folder, "AssetData");
        if (!Directory.Exists(assetData))
        {
            return;
        }

        var actual = ContentHasher.HashDirectory(assetData).Hash;
        messages.Add(string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)
            ? $"✓ {name}: content hash verified."
            : $"⚠ {name}: content hash mismatch — the source may have changed since it was indexed.");
    }
}
