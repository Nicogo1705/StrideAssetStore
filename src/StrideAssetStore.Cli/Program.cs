// Copyright (c) 2026 Nicogo1705
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Reflection;
using System.Text;
using StrideAssetStore.Cli.Commands;
using StrideAssetStore.Cli.Local;
using Spectre.Console;
using Spectre.Console.Cli;

// A Windows console still starts on a legacy code page (850, 437, 1252 depending on the install),
// which has no glyph for anything this tool prints outside ASCII: the star column of `search`, the
// green tick after an install, an em dash. They all arrived as "?". The switch is per-process — the
// user's own console keeps its setting — and only for a real console: redirected output is decoded
// by whatever captures it, and forcing the encoding there would prepend a BOM to it.
if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
{
    try
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
    catch (Exception)
    {
        // No console attached (a detached process, some CI agents), or policy in the way. Nothing
        // would have been read anyway, and failing to pick an encoding must not end the command.
    }
}

// When output is redirected (CI logs, a file, a pipe to tee), emit plain text instead of
// ANSI colour codes — otherwise escape sequences leak into captured logs and PR comments.
if (Console.IsOutputRedirected)
{
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.No,
        ColorSystem = ColorSystemSupport.NoColors,
    });
}

// Started before the command so the answer is usually ready when it ends, and printed after its
// output so a version notice never delays or buries what was asked for. Run with nothing at all —
// which prints the help — it asks regardless of today's check: that is someone looking the tool
// over, and "there is a newer one" belongs in that answer. `--version` deliberately does not, since
// that is what scripts and the desktop app call.
StrideAssetStore.Cli.Local.ToolUpdateNotice.Begin(force: args.Length == 0);

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("strideassetstore");

    // The tool is written StrideAssetStore everywhere it is talked about — the package id, the
    // repository, the announcement people copy from — so that is how they type it, and `Add Grass`
    // answering "Unknown command 'Add'" reads as a broken tool rather than a capital letter. The
    // executable name is already case-insensitive on Windows; this makes the rest of the line
    // behave the same way. Asset ids have always been matched case-insensitively.
    config.CaseSensitivity(CaseSensitivity.None);

    // Fifteen commands in one list read as fifteen equally likely things to type. Grouped, they
    // read as four situations, and a reader is only ever in one of them.
    config.SetHelpProvider(new StrideAssetStore.Cli.Local.GroupedHelpProvider(config.Settings));

    // Expected failures travel as exceptions here — an ambiguous asset id, no solution in sight, a
    // version nobody published. Without this they reach the user as a stack trace and exit -1 (255
    // in a shell), burying messages that were written to be read.
    config.SetExceptionHandler((exception, _) =>
    {
        AnsiConsole.MarkupLineInterpolated($"[red]{exception.Message}[/]");
        return 1;
    });

    // Without this, Spectre answers `--version` with "Unexpected option". Bug reports ask for it,
    // and so does CI when it checks the tool it just installed. The informational version carries
    // the build suffix (a local build says 99.0.0.0-local, so it can't be mistaken for a release).
    config.SetApplicationVersion(
        typeof(SearchCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(SearchCommand).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0");

    // ── Using assets: everything the desktop app's UI does, from a terminal ──
    config.AddCommand<SearchCommand>("search")
        .WithDescription("Find assets in the store catalog.")
        .WithExample("search", "grass");

    config.AddCommand<InfoCommand>("info")
        .WithDescription("Show one asset in full — its published versions, what add would install, its dependencies.")
        .WithExample("info", "grass")
        .WithExample("info", "grass", "--versions");

    config.AddCommand<AddCommand>("add")
        .WithDescription("Install an asset into the project you are in.")
        .WithExample("add", "com.nicogo.grass")
        .WithExample("add", "grass", "--version", "1.0.0");

    config.AddCommand<ForkListCommand>("forks")
        .WithDescription("List an asset's forks — the names `add --fork` accepts.")
        .WithExample("forks", "grass");

    config.AddCommand<ListCommand>("list")
        .WithDescription("Show the assets this project references, or --cached for everything downloaded.")
        .WithExample("list")
        .WithExample("list", "--cached");

    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Update installed assets, or move one onto another version.")
        .WithExample("update")
        .WithExample("update", "grass", "--version", "1.1.0");

    config.AddCommand<RemoveCommand>("remove")
        .WithDescription("Remove an asset from the project.")
        .WithExample("remove", "grass");

    // ── Authoring an asset: the desktop app's wizard and its pre-flight checks, as commands ──
    config.AddCommand<NewAssetCommand>("new")
        .WithDescription("Create a new asset repository from the store's template (needs the GitHub CLI).")
        .WithExample("new", "StrideCoolThing")
        .WithExample("new", "StrideCoolThing", "--category", "Shaders", "--id", "com.you.cool-thing");

    config.AddCommand<CheckCommand>("check")
        .WithDescription("Check an asset repository before publishing it: manifest, media, README, project layout.")
        .WithExample("check")
        .WithExample("check", "--strict");

    // ── Submitting to the registry: the app's Manage page, as commands ──
    config.AddCommand<PublishCommand>("publish")
        .WithDescription("Submit this asset repository to the store (opens a pull request on the registry).")
        .WithExample("publish")
        .WithExample("publish", "--ref", "main");

    config.AddCommand<CertifyCommand>("certify")
        .WithDescription("Certify a version: pin a reviewed commit as immutable.")
        .WithExample("certify", "com.you.cool-thing", "--version", "1.0.0", "--commit", "<sha>");

    config.AddCommand<DeprecateCommand>("deprecate")
        .WithDescription("Mark an asset deprecated — still installable, no longer recommended.")
        .WithExample("deprecate", "com.you.cool-thing", "--reason", "Superseded", "--successor", "com.you.better-thing");

    config.AddCommand<UnpublishCommand>("unpublish")
        .WithDescription("Take an asset out of the registry entirely (breaks `add` for everyone using it).")
        .WithExample("unpublish", "com.you.cool-thing");

    // ── The desktop app itself ──
    config.AddBranch<AppSettings>("app", app =>
    {
        app.SetDescription("Install, update and start the desktop app.");
        app.AddCommand<AppInstallCommand>("install").WithDescription("Install the desktop app for this machine.");
        app.AddCommand<AppInstallCommand>("update").WithDescription("Update the installed desktop app.");
        app.AddCommand<AppStatusCommand>("status").WithDescription("Show what is installed, what is running, and the latest release.");
        app.AddCommand<AppStartCommand>("start").WithDescription("Start the installed desktop app.");
        app.AddCommand<AppStopCommand>("stop").WithDescription("Stop the desktop app running on this machine.");
        app.AddCommand<AppOpenCommand>("open").WithDescription("Open the store in a browser — the local app if it is running, otherwise the online storefront.");
    });

    config.AddCommand<AliasCommand>("alias")
        .WithDescription("Create a short name for this tool (`sas`), or remove it.")
        .WithExample("alias")
        .WithExample("alias", "--name", "sast")
        .WithExample("alias", "--remove");

    config.AddCommand<UpgradeCommand>("upgrade")
        .WithDescription(
            "Update this tool and the desktop app. Installed assets are not touched — use `strideassetstore update` for those.")
        .WithExample("upgrade");

    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Remove what this tool installed on this machine: the app, the downloaded assets, the settings.")
        .WithExample("uninstall")
        .WithExample("uninstall", "--app");

    // ── Registry maintenance (needs a checkout of the AssetContainer repository) ──
    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate registry entries and manifests against schemas and catalog rules.");

    config.AddCommand<BuildIndexCommand>("build-index")
        .WithDescription("Generate index.lock.json from the registry (use --stars to refresh star counts).");

    config.AddCommand<GeneratePagesCommand>("generate-pages")
        .WithDescription("Generate static per-asset OG/SEO pages and a sitemap from an index.lock.json.");
});

var exitCode = app.Run(args);
ToolUpdateNotice.End();
return exitCode;
