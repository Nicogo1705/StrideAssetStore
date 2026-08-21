// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Core.Local.Install;
using StrideAssetStore.Core.Local.Projects;

namespace StrideAssetStore.Cli.Local;

/// <summary>
/// Works out what the user meant by "here". The desktop app has a file browser and a list of tracked
/// solutions; on a command line the answer has to come from the working directory, the way every
/// other project-scoped tool behaves.
/// </summary>
internal static class ProjectTarget
{
    /// <summary>
    /// The solution or project to act on: an explicit path, otherwise the nearest one found walking
    /// up from the working directory. Solutions win over lone projects at the same level, since a
    /// solution is the wider context.
    /// </summary>
    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            if (File.Exists(full))
            {
                return full;
            }

            if (Directory.Exists(full) && FindIn(full) is { } inDirectory)
            {
                return inDirectory;
            }

            throw new FileNotFoundException($"No solution or project found at {full}.");
        }

        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            if (FindIn(dir.FullName) is { } found)
            {
                return found;
            }
        }

        throw new FileNotFoundException(
            "No .sln, .slnx or .csproj found here or in any parent directory. Run this from your game's folder, or pass --project.");
    }

    private static string? FindIn(string directory)
    {
        string[] patterns = ["*.slnx", "*.sln", "*.csproj"];
        foreach (var pattern in patterns)
        {
            // Ordered so the choice is stable across machines and listings.
            var match = Directory.EnumerateFiles(directory, pattern).OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// The projects an asset can be installed into, narrowed by <paramref name="filter"/> (a project
    /// name or a path fragment). Throws with the available names when the choice is ambiguous, rather
    /// than picking one: installing into the wrong project is annoying to undo.
    /// </summary>
    public static IReadOnlyList<string> SelectProjects(
        AssetInstaller installer, string target, string? filter, bool all)
    {
        var candidates = installer.ReadTargets(target);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(target)} contains no project that can take an asset.");
        }

        if (all)
        {
            return [.. candidates.Select(c => c.Path)];
        }

        if (filter is not null)
        {
            var matched = candidates.Where(c =>
                c.Name.Equals(filter, StringComparison.OrdinalIgnoreCase)
                || c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || c.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            return matched.Count switch
            {
                1 => [matched[0].Path],
                0 => throw new InvalidOperationException(
                    $"No project matches '{filter}'. Available: {Names(candidates)}"),
                _ => throw new InvalidOperationException(
                    $"'{filter}' matches several projects: {Names(matched)}. Be more specific, or pass --all-projects."),
            };
        }

        return candidates.Count == 1
            ? [candidates[0].Path]
            : throw new InvalidOperationException(
                $"{Path.GetFileName(target)} has several projects: {Names(candidates)}. Pick one with --project, or use --all-projects.");
    }

    /// <summary>The solution path to register asset projects in, or null for a lone .csproj target.</summary>
    public static string? SolutionOf(string target) =>
        Path.GetExtension(target).ToLowerInvariant() is ".sln" or ".slnx" ? target : null;

    private static string Names(IEnumerable<SolutionProject> projects) =>
        string.Join(", ", projects.Select(p => p.Name));
}
