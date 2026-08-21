// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.ComponentModel;
using Spectre.Console.Cli;

namespace StrideAssetStore.Cli.Commands;

/// <summary>Options shared by the commands that read the published catalog.</summary>
internal class CatalogSettings : CommandSettings
{
    [CommandOption("--index <URL>")]
    [Description("Catalog index to read. Defaults to the public registry's index.lock.json.")]
    public string? IndexUrl { get; init; }

    [CommandOption("--offline")]
    [Description("Use the catalog snapshot cached on this machine instead of fetching it.")]
    public bool Offline { get; init; }
}

/// <summary>Options for the commands that read a solution or project without changing it.</summary>
internal class TargetSettings : CatalogSettings
{
    [CommandOption("-t|--target <PATH>")]
    [Description("Solution or .csproj to act on. Defaults to the nearest one from the current directory.")]
    public string? Target { get; init; }
}

/// <summary>
/// Options for the commands that change a solution or project. Kept apart from <see cref="TargetSettings"/>
/// so a read-only command doesn't advertise a project filter it ignores, or a confirmation it never asks.
/// </summary>
internal class ProjectScopedSettings : TargetSettings
{
    [CommandOption("-p|--project <NAME>")]
    [Description("Which project inside the solution to act on, when there is more than one.")]
    public string? Project { get; init; }

    [CommandOption("--all-projects")]
    [Description("Act on every project of the solution.")]
    public bool AllProjects { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Answer yes to confirmations (for scripts and CI).")]
    public bool Yes { get; init; }
}
