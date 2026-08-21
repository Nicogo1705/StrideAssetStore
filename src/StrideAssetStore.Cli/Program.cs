// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.Cli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

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

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("strideassetstore");

    // ── Using assets: everything the desktop app's UI does, from a terminal ──
    config.AddCommand<SearchCommand>("search")
        .WithDescription("Find assets in the store catalog.")
        .WithExample("search", "grass");

    config.AddCommand<AddCommand>("add")
        .WithDescription("Install an asset into the project you are in.")
        .WithExample("add", "com.nicogo.grass")
        .WithExample("add", "grass", "--version", "1.0.0");

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

    // ── The desktop app itself ──
    config.AddBranch<AppSettings>("app", app =>
    {
        app.SetDescription("Install, update and start the desktop app.");
        app.AddCommand<AppInstallCommand>("install").WithDescription("Install the desktop app for this machine.");
        app.AddCommand<AppInstallCommand>("update").WithDescription("Update the installed desktop app.");
        app.AddCommand<AppStatusCommand>("status").WithDescription("Show what is installed, what is running, and the latest release.");
        app.AddCommand<AppStartCommand>("start").WithDescription("Start the installed desktop app.");
        app.AddCommand<AppStopCommand>("stop").WithDescription("Stop the desktop app running on this machine.");
        app.AddCommand<AppOpenCommand>("open").WithDescription("Open the online storefront in a browser.");
    });

    // ── Registry maintenance (needs a checkout of the AssetContainer repository) ──
    config.AddCommand<ValidateCommand>("validate")
        .WithDescription("Validate registry entries and manifests against schemas and catalog rules.");

    config.AddCommand<BuildIndexCommand>("build-index")
        .WithDescription("Generate index.lock.json from the registry (use --stars to refresh star counts).");

    config.AddCommand<GeneratePagesCommand>("generate-pages")
        .WithDescription("Generate static per-asset OG/SEO pages and a sitemap from an index.lock.json.");
});

return app.Run(args);
