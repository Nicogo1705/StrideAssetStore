// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using StrideAssetStore.App;
using StrideAssetStore.Core.Catalog;
using StrideAssetStore.Desktop.Components;

const string Url = "http://localhost:5111";

// stride-assetstore:// launch (from the web storefront's Install/Start buttons): if an instance is
// already serving, just open the requested page in it and exit instead of failing to bind. This
// applies to ANY protocol launch (including plain stride-assetstore://open with no mapped path).
var launchPath = StrideAssetStore.Desktop.Services.ProtocolLauncher.ParseLaunchPath(args);
var protocolLaunch = args.Any(a => a.StartsWith(
    StrideAssetStore.Desktop.Services.ProtocolLauncher.Scheme + "://", StringComparison.OrdinalIgnoreCase));
if (protocolLaunch && StrideAssetStore.Desktop.Services.ProtocolLauncher.IsAlreadyRunning(new Uri(Url).Port))
{
    OpenBrowser(Url + (launchPath ?? ""));
    return;
}

// Plain double-launch while an instance is already serving: don't crash on the port bind -
// behave like the protocol path, focus the existing instance (new tab) and leave.
if (!protocolLaunch && StrideAssetStore.Desktop.Services.ProtocolLauncher.IsAlreadyRunning(new Uri(Url).Port))
{
    OpenBrowser(Url);
    return;
}

// Register the protocol for the current user (Windows, HKCU, best-effort) — but never from
// a dev (0.0.0.0) or locally-built Release (99.0.0.0): they would hijack the website's
// "Open app" button away from the real install.
//
// Matched exactly, not by major: this used to reject anything with major 0, which silently
// disqualified the v0.9.0 release itself — the app never registered the scheme, and the site's
// "Open app" button fell through to the download page for everyone.
var entryVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
var isLocalBuild = entryVersion is null
    || entryVersion is { Major: 0, Minor: 0, Build: 0 }
    || entryVersion.Major == 99;
if (!isLocalBuild)
{
    StrideAssetStore.Desktop.Services.ProtocolLauncher.TryRegisterWindowsScheme();
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environments.Production, // desktop app: no dev-time static asset patching
    // The content root defaults to the working directory, which for a desktop app is wherever it was
    // started from — a shortcut, a terminal in another folder, or (debugging from Visual Studio) the
    // project directory, which has no wwwroot at all. wwwroot then resolves to nothing, so
    // _framework/blazor.web.js 404s and no circuit ever starts: the app renders and does nothing.
    ContentRootPath = AppContext.BaseDirectory,
});
builder.WebHost.UseUrls(Url);
builder.WebHost.UseStaticWebAssets(); // serve _framework + RCL assets in Production/dotnet run
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Desktop app: nothing long-running to drain on shutdown. The default 30s grace makes the
// console window visibly linger on X/Alt+F4 while open Blazor circuits are drained — one
// second is plenty (open sockets are aborted after it, harmless here).
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));

// Live catalog from the public registry (offline cache falls back via CatalogLoader).
// A self-pointing HttpClient also serves the publish form's bundled catalog metadata.
var indexUrl = builder.Configuration["Catalog:IndexUrl"] ?? CatalogDefaults.IndexUrl;
var appRepo = builder.Configuration["App:Repo"] ?? "https://github.com/Nicogo1705/StrideAssetStore";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(Url + "/") });
// Short timeout on purpose: a degraded GitHub can hold the connection open instead of failing
// fast, and the default 100s would keep the first interactive render pending that whole time —
// the app would sit on its prerendered HTML, with none of its buttons alive.
builder.Services.AddScoped<ICatalogSource>(_ => new HttpCatalogSource(
    new HttpClient { Timeout = TimeSpan.FromSeconds(8) }, new Uri(indexUrl)));
builder.Services.AddStrideAssetStoreUi(
    builder.Configuration.GetSection("Registry").Get<StrideAssetStore.App.Services.RegistryOptions>(),
    builder.Configuration.GetSection("App").Get<StrideAssetStore.App.Services.AppInfo>(),
    knownLocal: true); // this IS the local app — never wait for the browser to say so
builder.Services.AddScoped<StrideAssetStore.Core.Local.Install.AssetInstaller>();
// One instance: it holds an HttpClient and its answers are cached per asset by the page.
builder.Services.AddSingleton<StrideAssetStore.Core.Local.Git.ForkLister>();
builder.Services.AddSingleton<StrideAssetStore.Desktop.Services.ProjectStore>();
builder.Services.AddSingleton<StrideAssetStore.Desktop.Services.AuthorRepoService>();
builder.Services.AddScoped<StrideAssetStore.Desktop.Services.AssetScaffolder>();

// Desktop can open registry PRs with the local git + GitHub CLI (no pasted token). Overrides the
// browser's no-op ICliPublisher registered by AddStrideAssetStoreUi.
builder.Services.AddScoped<StrideAssetStore.Desktop.Services.GhCliPublisher>();
builder.Services.AddScoped<StrideAssetStore.App.Services.ICliPublisher>(sp =>
    sp.GetRequiredService<StrideAssetStore.Desktop.Services.GhCliPublisher>());

var app = builder.Build();

// A page that fails to render (bad server response, unexpected data) must never leave the user
// with a dead browser tab and no way out: serve the Blazor-free rescue controls instead.
app.UseExceptionHandler(sub => sub.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(RescuePage("This page failed to render — the app itself is still running."));
}));

app.UseStaticFiles();
app.UseAntiforgery();

// Presence/version beacon for the online storefront: lets nicogo1705.github.io swap its
// "Download app" button for "Open app". Read-only, non-sensitive, hence the open CORS headers.
// Chrome's Private Network Access sends an OPTIONS preflight for public→localhost requests and
// requires Access-Control-Allow-Private-Network — without it the probe silently fails.
var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";
app.MapMethods("/api/ping", ["GET", "OPTIONS"], (HttpContext ctx) =>
{
    ctx.Response.Headers.AccessControlAllowOrigin = "*";
    ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    ctx.Response.Headers.AccessControlAllowMethods = "GET";
    return HttpMethods.IsOptions(ctx.Request.Method)
        ? Results.NoContent()
        : Results.Json(new { app = "stride-assetstore", version = appVersion });
});
app.MapRazorComponents<StrideAssetStore.Desktop.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ServiceCollectionExtensions).Assembly); // routable pages live in the RCL

// Local-only window controls for the UI's top-bar buttons (the console is the app's only window).
// GET is supported on purpose: when the Blazor circuit is dead these must stay reachable from the
// address bar alone (http://localhost:5111/console/toggle), with no scripting involved.
// They are also callable from the online storefront (which pings this app and knows it runs):
// when the desktop UI itself is unusable, that page is the only remaining place to drive it.
var storefrontOrigin = new Uri(SiteUrlFromRepo(appRepo)).GetLeftPart(UriPartial.Authority);

app.MapMethods("/console/toggle", ["GET", "POST", "OPTIONS"], (HttpContext ctx) =>
{
    if (!AllowControlOrigin(ctx, storefrontOrigin))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (HttpMethods.IsOptions(ctx.Request.Method))
    {
        return Results.NoContent();
    }

    if (!OperatingSystem.IsWindows())
    {
        // Nothing to toggle: the console window is an AllocConsole feature. Say so rather than
        // reporting a state change that never happened.
        return HttpMethods.IsGet(ctx.Request.Method)
            ? Results.Content(RescuePage("The console window is a Windows-only feature."), "text/html; charset=utf-8")
            : Results.Json(new { visible = false, supported = false });
    }

    var visible = ConsoleWindow.Toggle();
    if (!HttpMethods.IsGet(ctx.Request.Method))
    {
        return Results.Json(new { visible });
    }

    // Plain-link fallback (no scripting): come back to the page the click came from, so toggling
    // the console from a broken UI doesn't also throw the user out of it.
    return SameOriginReferer(ctx) is { } back
        ? Results.Redirect(back)
        : Results.Content(RescuePage($"Console window {(visible ? "opened" : "closed")}."), "text/html; charset=utf-8");
});

// Blazor-free rescue page — the one URL that works no matter what the UI is doing.
app.MapGet("/app/controls", () => Results.Content(RescuePage(null), "text/html; charset=utf-8"));
// Nav attention dots: things that deserve the user's eye (outdated assets, broken refs).
// Computed on demand — the layout asks once per session, in the background.
app.MapGet("/api/attention", async (
    StrideAssetStore.Desktop.Services.ProjectStore store,
    StrideAssetStore.Core.Local.Install.AssetInstaller installer,
    ICatalogSource source) =>
{
    try
    {
        var index = await source.LoadAsync();
        var catalog = index.Assets.ToDictionary(a => a.Id, StringComparer.Ordinal);
        var cache = installer.ListCachedAssets(catalog);
        var assetsAttention = cache.Count(c => c.Status is "outdated" or "broken");

        var projectsAttention = 0;
        foreach (var project in store.List().Where(p => p.Exists))
        {
            var view = installer.Analyze(project.Path, catalog);
            projectsAttention += view.Projects.SelectMany(n => n.Assets)
                .Count(a => a.Status is "outdated" or "broken" or "missing");
            projectsAttention += view.Dangling.Count;
        }

        return Results.Json(new { projects = projectsAttention, assets = assetsAttention });
    }
    catch
    {
        return Results.Json(new { projects = 0, assets = 0 });
    }
});
// Opens a terminal running the update. The app deliberately cannot update itself — but it can hand
// the job to the tool that can, which saves the user copying a command by hand. Reports whether a
// terminal actually opened, so the UI can fall back to showing the command rather than lying.
app.MapPost("/app/update", (HttpContext ctx) =>
{
    // Same guard as the other controls: this one stops the app and reinstalls its binary, so a
    // website must not be able to trigger it either.
    if (!AllowControlOrigin(ctx, storefrontOrigin))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    const string tool = "strideassetstore";
    const string command = $"{tool} app update";

    // Opening a terminal on a command that isn't installed would just print "not recognized" and
    // look like the button is broken. Say what's missing instead.
    if (!StrideAssetStore.Core.Local.Shell.DesktopShell.CommandExists(tool))
    {
        return Results.Json(new { opened = false, command, toolMissing = true });
    }

    return Results.Json(new
    {
        opened = StrideAssetStore.Core.Local.Shell.DesktopShell.OpenTerminal(command),
        command,
        toolMissing = false,
    });
});

app.MapMethods("/app/quit", ["GET", "POST", "OPTIONS"], (HttpContext ctx, IHostApplicationLifetime lifetime) =>
{
    if (!AllowControlOrigin(ctx, storefrontOrigin))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (HttpMethods.IsOptions(ctx.Request.Method))
    {
        return Results.NoContent();
    }

    RequestQuit(lifetime);
    return HttpMethods.IsGet(ctx.Request.Method)
        ? Results.Content(RescuePage("Stopping the app — you can close this tab."), "text/html; charset=utf-8")
        : Results.Json(new { stopping = true });
});

// Console window closed by the user (X / Alt+F4) → same clean shutdown as ⏻.
ConsoleWindow.OnConsoleClosing = () =>
{
    app.Lifetime.StopApplication();
    app.Lifetime.ApplicationStopped.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
};

// System-wide escape hatch: works even when the HTTP server itself is unresponsive, which is
// the one case the rescue page can't cover.
StrideAssetStore.Desktop.Services.GlobalHotkeys.Start(
    toggleConsole: () => ConsoleWindow.Toggle(),
    quit: () => RequestQuit(app.Lifetime));

app.Lifetime.ApplicationStarted.Register(() =>
{
    // Friendly banner — buffered, and echoed into the on-demand console window.
    ConsoleWindow.Log("");
    ConsoleWindow.Log($"  Community Stride Asset Store — desktop app v{appVersion}");
    ConsoleWindow.Log($"  Executable:     {Environment.ProcessPath ?? "(unknown)"}");
    ConsoleWindow.Log($"  Local UI:       {Url}  (opening in your browser…)");
    ConsoleWindow.Log($"  Online store:   {SiteUrlFromRepo(appRepo)}");
    ConsoleWindow.Log($"  Catalog index:  {indexUrl}");

    // Where the app keeps its files — the folder to look at (or wipe) when debugging.
    var dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StrideAssetStore");
    ConsoleWindow.Log($"  App data:       {dataDir}  (tracked projects, settings)");
    ConsoleWindow.Log($"  Asset cache:    {StrideAssetStore.Core.Local.Install.AssetInstaller.GlobalCacheRoot}  (shared clones, one subfolder per ref)");
    ConsoleWindow.Log($"  Git:            {(new StrideAssetStore.Core.Local.Git.GitClient().IsAvailable() ? "found on PATH" : "NOT FOUND — installs will fail")}");
    if (launchPath is not null)
    {
        ConsoleWindow.Log($"  Install link:   opening {launchPath}");
    }

    var startupMs = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds;
    ConsoleWindow.Log($"  Started in:     {startupMs} ms");
    ConsoleWindow.Log("");
    ConsoleWindow.Log("  Toggle this console with the 🖥 button in the app's top bar; quit with ⏻.");
    ConsoleWindow.Log("  Closing this window (X / Alt+F4) quits the whole app.");
    ConsoleWindow.Log($"  If the UI ever breaks: {Url}/app/controls  (works without the app's UI)");
    if (StrideAssetStore.Desktop.Services.GlobalHotkeys.Description is { } hotkeys)
    {
        ConsoleWindow.Log($"  Anywhere in Windows: {hotkeys}");
    }

    ConsoleWindow.Log("");
    OpenBrowser(Url + (launchPath ?? ""));

    // Console open by default (the banner tells the user the app runs and where);
    // stays closed only when the user closed it last session.
    ConsoleWindow.ApplySavedState();

    // Catalog stats + update check in the background — the banner never waits on the network.
    _ = Task.Run(async () =>
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("stride-assetstore-desktop");

        try
        {
            var clock = Stopwatch.StartNew();
            var index = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(indexUrl);
            clock.Stop();
            var count = index.TryGetProperty("assets", out var assets) ? assets.GetArrayLength() : 0;
            var generated = index.TryGetProperty("generatedAt", out var g) ? g.GetString() : null;
            ConsoleWindow.Log($"  Catalog:        {count} asset(s), generated {generated ?? "?"} — fetched in {clock.ElapsedMilliseconds} ms");
        }
        catch
        {
            ConsoleWindow.Log("  Catalog:        offline — the app will use its cached copy.");
        }

        try
        {
            var parts = appRepo.TrimEnd('/').Split('/');
            var json = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"https://api.github.com/repos/{parts[^2]}/{parts[^1]}/releases/latest");
            var latestTag = json.GetProperty("tag_name").GetString() ?? "";
            var latest = latestTag.TrimStart('v', 'V');
            if (Version.TryParse(latest, out var l) && Version.TryParse(appVersion, out var current) && l > current)
            {
                ConsoleWindow.Log($"  ⬆ Update available: v{appVersion} → {latestTag} — {appRepo.TrimEnd('/')}/releases/latest");
            }
        }
        catch
        {
            // Offline or rate-limited — the banner simply stays without the update line.
        }
    });
});
app.Run();

// The online storefront lives on GitHub Pages of the app repository (config-only override).
static string SiteUrlFromRepo(string repoUrl)
{
    var parts = repoUrl.TrimEnd('/').Split('/');
    return parts.Length >= 2
        ? $"https://{parts[^2].ToLowerInvariant()}.github.io/{parts[^1]}/"
        : repoUrl;
}

// If the browser can't be launched, the user still has the URL printed in the banner.
static void OpenBrowser(string url) => StrideAssetStore.Core.Local.Shell.DesktopShell.OpenUrl(url);

/// <summary>The page the request came from, when it belongs to this app — null otherwise, so a
/// foreign Referer can never turn these endpoints into an open redirect.</summary>
static string? SameOriginReferer(HttpContext ctx)
{
    var referer = ctx.Request.Headers.Referer.ToString();
    if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri) || !uri.IsLoopback
        || uri.Port != ctx.Request.Host.Port)
    {
        return null;
    }

    return uri.PathAndQuery;
}

/// <summary>
/// Authorizes a call to the window controls and stamps the CORS response headers. These endpoints
/// close the app, so only the app's own pages, the official storefront, the address bar and the CLI
/// may reach them.
/// </summary>
/// <remarks>
/// A missing <c>Origin</c> header does NOT mean "typed in the address bar": browsers omit it on every
/// GET subresource, so trusting it let any website on the internet stop the app with
/// <c>&lt;img src="http://localhost:5111/app/quit"&gt;</c>. <c>Sec-Fetch-Site</c> is what actually
/// distinguishes those cases — every current browser sends it and it cannot be set by script, while a
/// non-browser client (the CLI's HttpClient, which POSTs /app/quit) sends none at all.
/// </remarks>
static bool AllowControlOrigin(HttpContext ctx, string storefrontOrigin)
{
    var origin = ctx.Request.Headers.Origin.ToString();
    var site = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
    var fromStorefront = string.Equals(origin, storefrontOrigin, StringComparison.OrdinalIgnoreCase);

    if (site is "cross-site" or "same-site")
    {
        // Only the storefront may drive the app from another origin, and never as a sub-resource:
        // a page may fetch() these, it may not fire them from an <img>, <script> or <iframe>.
        if (!fromStorefront || ctx.Request.Headers["Sec-Fetch-Dest"].ToString() is not ("empty" or "document"))
        {
            return false;
        }
    }
    else if (site is not ("" or "none" or "same-origin"))
    {
        return false; // unknown value from a future browser — refuse rather than guess
    }
    else if (!string.IsNullOrEmpty(origin)
        && !fromStorefront
        && !(Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.IsLoopback
            && uri.Port == StrideAssetStore.Core.Local.Releases.RunningApp.Port))
    {
        // Loopback is not a trust boundary: any dev server or local tool on another port would
        // otherwise be able to stop the app.
        return false;
    }

    if (!string.IsNullOrEmpty(origin))
    {
        ctx.Response.Headers.AccessControlAllowOrigin = origin;
        ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        ctx.Response.Headers.AccessControlAllowMethods = "GET, POST";
        ctx.Response.Headers.AccessControlAllowHeaders = "Content-Type";
        ctx.Response.Headers.Vary = "Origin";
    }

    return true;
}

/// <summary>
/// Stops the app for good. The graceful stop is tried first; a hard exit follows if the host is
/// still up seconds later, because a wedged request or a stuck shutdown would otherwise leave a
/// windowless process that only the Task Manager can end.
/// </summary>
static void RequestQuit(IHostApplicationLifetime lifetime)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(200);
        try
        {
            lifetime.StopApplication();
        }
        catch
        {
            // Already stopping — the backstop below still applies.
        }
    });

    new Thread(() =>
    {
        Thread.Sleep(6000);
        try
        {
            Environment.Exit(0);
        }
        catch
        {
            // fall through to the kill
        }

        Thread.Sleep(2000);
        Process.GetCurrentProcess().Kill();
    })
    { IsBackground = true, Name = "StrideAssetStore quit backstop" }.Start();
}

/// <summary>
/// Self-contained HTML for the rescue controls: no Blazor, no SignalR circuit, no scripts needed
/// for the buttons (plain links). This is what the user gets when the app's UI is unusable.
/// </summary>
static string RescuePage(string? notice)
{
    // The console window is a Windows-only feature (AllocConsole). Elsewhere there is no state to
    // report and no button that would do anything, so neither is shown.
    var console = OperatingSystem.IsWindows()
        ? ($"<p>The console window is currently <strong>{(ConsoleWindow.IsOpen ? "open" : "closed")}</strong>.</p>",
           "<a class=\"btn\" href=\"/console/toggle\">🖥 Toggle console</a>")
        : ("", "");
    var note = notice is null ? "" : $"<p class=\"notice\">{System.Net.WebUtility.HtmlEncode(notice)}</p>";
    var hotkeys = StrideAssetStore.Desktop.Services.GlobalHotkeys.Description is { } h
        ? $"<p class=\"hint\">Anywhere in Windows: {System.Net.WebUtility.HtmlEncode(h)}</p>"
        : "";
    return $$"""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>Asset Store — app controls</title>
        <style>
          body { font-family: system-ui, sans-serif; background: #11141a; color: #e6e9ef;
                 display: grid; place-items: center; min-height: 100vh; margin: 0; }
          .card { background: #171b23; border: 1px solid #2a3140; border-radius: 14px;
                  padding: 1.8rem 2rem; max-width: 34rem; }
          h1 { font-size: 1.1rem; margin: 0 0 .4rem; }
          p { color: #98a2b3; font-size: .88rem; line-height: 1.5; }
          .notice { color: #e6e9ef; }
          .row { display: flex; gap: .6rem; flex-wrap: wrap; margin-top: 1.2rem; }
          a.btn { display: inline-block; padding: .55rem 1rem; border-radius: 9px; text-decoration: none;
                  border: 1px solid #2a3140; color: #e6e9ef; background: #1e2430; }
          a.btn:hover { border-color: #4c8dff; }
          a.btn.danger:hover { border-color: #ff6b6b; }
          .hint { font-size: .78rem; }
        </style></head><body>
        <div class="card">
          <h1>Community Stride Asset Store — app controls</h1>
          {{note}}
          {{console.Item1}}
          <div class="row">
            {{console.Item2}}
            <a class="btn danger" href="/app/quit">⏻ Quit the app</a>
            <a class="btn" href="/">↩ Back to the app</a>
          </div>
          {{hotkeys}}
          <p class="hint">Bookmark this page: <code>http://localhost:5111/app/controls</code></p>
        </div></body></html>
        """;
}

/// <summary>
/// The app's on-demand console window (Windows). The process is a WinExe — no console
/// exists at startup; the UI's 🖥 button allocates a real one (AllocConsole) and replays
/// the buffered log, closing frees it (FreeConsole). No hiding/minimizing involved, so it
/// behaves the same under conhost and Windows Terminal. The open/closed state is persisted
/// and re-applied on the next start. ⏻ /app/quit stops the process with or without console.
/// </summary>
static class ConsoleWindow
{
    private static readonly object Gate = new();
    private static readonly List<string> Buffer = [];

    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StrideAssetStore", "console.json");

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    private delegate bool CtrlHandlerRoutine(uint ctrlType);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(CtrlHandlerRoutine handler, bool add);

    // Kept in a field so the GC never collects the delegate the OS is holding.
    private static CtrlHandlerRoutine? _ctrlHandler;

    /// <summary>Invoked when the user closes the console window (X / Alt+F4). Windows always
    /// terminates the process after a console close — this hook lets the host stop gracefully
    /// (flush, save) inside the ~5s grace period instead of dying mid-write.</summary>
    public static Action? OnConsoleClosing;

    /// <summary>Whether a console window is currently allocated for this process.</summary>
    public static bool IsOpen => !OperatingSystem.IsWindows() || GetConsoleWindow() != IntPtr.Zero;

    /// <summary>Buffers a banner/status line and echoes it when the console is open.
    /// On non-Windows the process keeps its normal stdout, so lines always print there.</summary>
    public static void Log(string line)
    {
        lock (Gate)
        {
            Buffer.Add(line);
            try
            {
                if (!OperatingSystem.IsWindows() || GetConsoleWindow() != IntPtr.Zero)
                {
                    Console.WriteLine(line);
                }
            }
            catch
            {
                // Writing must never take the app down.
            }
        }
    }

    /// <summary>Opens or closes the console window; returns the new open state.</summary>
    public static bool Toggle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false; // no console window concept to manage — stdout is the terminal's
        }

        lock (Gate)
        {
            if (GetConsoleWindow() != IntPtr.Zero)
            {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                FreeConsole();
                Save(open: false);
                return false;
            }

            if (!AllocConsole())
            {
                return false;
            }

            // Fresh consoles come up in the OEM codepage (CP850) — UTF-8 text turns into
            // mojibake ("ÔÇö" for —) without this.
            var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = utf8;
            var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
            Console.Title = "Community Stride Asset Store — console";
            // Closing an allocated console always terminates the process (no veto possible) —
            // so make it a CLEAN quit: the handler runs the graceful shutdown during the
            // close grace period. CTRL_CLOSE_EVENT = 2; Ctrl+C/Break keep default handling.
            _ctrlHandler ??= ctrlType =>
            {
                if (ctrlType == 2)
                {
                    OnConsoleClosing?.Invoke();
                }
                return false;
            };
            SetConsoleCtrlHandler(_ctrlHandler, true);

            foreach (var line in Buffer)
            {
                Console.WriteLine(line);
            }

            Save(open: true);
            return true;
        }
    }

    /// <summary>
    /// Applies the persisted console preference at startup. Default (first run, missing or
    /// unreadable state) = OPEN: the banner is how the user learns the app is running and
    /// where it lives. It only stays windowless when the user closed the console before.
    /// </summary>
    public static void ApplySavedState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var wantOpen = true;
        try
        {
            if (File.Exists(StateFile)
                && File.ReadAllText(StateFile).Contains("\"open\":false", StringComparison.OrdinalIgnoreCase))
            {
                wantOpen = false;
            }
        }
        catch
        {
            // Unreadable state — fall through to the visible default.
        }

        // Toggle(), not Open(): a process launched from a terminal or through `dotnet run` already
        // owns a console, and toggling it there closed the one the banner was about to be printed to.
        if (wantOpen && !IsOpen)
        {
            Toggle();
        }
    }

    private static void Save(bool open)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, $"{{\"open\":{(open ? "true" : "false")}}}");
        }
        catch
        {
            // Not persisting is harmless.
        }
    }
}
