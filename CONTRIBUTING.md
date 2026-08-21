# Contributing to the tools

This repository holds the **code**: the storefront, the desktop app and the CLI.

- Publishing an **asset** to the store? That happens in the registry —
  [AssetContainer/CONTRIBUTING.md](https://github.com/Nicogo1705/AssetContainer/blob/main/CONTRIBUTING.md).
- Reporting a **problem with an asset** (broken, mislabelled, wrong license)? Open an issue on the
  registry, not on the asset's author.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and `git` on your `PATH`.

```bash
git clone https://github.com/Nicogo1705/StrideAssetStore
cd StrideAssetStore
dotnet build
dotnet test
```

To run either host:

```bash
dotnet run --project src/StrideAssetStore.App        # storefront (WebAssembly)
dotnet run --project src/StrideAssetStore.Desktop    # desktop app, http://localhost:5111
```

The desktop app installs assets into real projects and clones into a shared per-machine cache
(`%APPDATA%/StrideAssetStore` on Windows, `~/.config/StrideAssetStore` elsewhere). If you are
experimenting, install with `--into` so clones land next to your test project instead.

## The one architectural rule

`StrideAssetStore.Core` must stay runnable in a browser: no filesystem, no git, no process
launching. Anything that needs a real machine belongs in `StrideAssetStore.Core.Local`.

This is enforced — `StrideAssetStore.UI` and `StrideAssetStore.App` fail their build if
`Core.Local` shows up in their resolved references, transitively included. If that error stops you,
the answer is almost never to remove the check; it is that the code you added is on the wrong side
of the line.

The shared Razor UI runs in both hosts, so a page cannot assume a filesystem either. Look at how
`AppEnvironment` and `InstallAvailable` gate the local-only parts.

## What good looks like here

- **Comments explain why, not what.** The codebase leans on this heavily; a comment that restates
  the code will be asked about in review.
- **Warnings are errors** (`TreatWarningsAsErrors`), and nullable reference types are on.
- **Tests run offline.** `Core.Local.Tests` builds real (tiny) git repositories in a temp folder
  rather than reaching the network, and redirects the shared cache into the workspace. Follow that
  pattern instead of mocking git.
- **Verify what you claim.** If you fix something user-visible, check it in the running app or the
  installed tool — not only in a unit test. A published build shipped a dead UI for a month because
  a smoke test only asserted that the page loaded, never that its script existed.

## Commits and pull requests

Write commit messages that explain the problem and why this is the fix; the log is the design
history of the project. One concern per PR, and say how you verified it.

Anything that changes the release archives, the update check or the protocol handler affects people
already running the app — call that out explicitly in the PR.

## Releases

Releases are cut from a `v*` tag, which triggers `release.yml`:

1. self-contained desktop builds for Windows, Linux and macOS, each smoke-tested on its native
   runner — the app must serve **and** deliver `_framework/blazor.web.js`;
2. `SHA256SUMS` for the unsigned archives;
3. the CLI published to nuget.org as `StrideAssetStore` via Trusted Publishing (OIDC, no stored
   key).

> nuget.org's publishing policy pins the workflow **file name**. Renaming `release.yml` silently
> breaks publishing until the policy is updated.

Version numbers come from the tag; local builds are deliberately stamped `99.0.0.0` so a
development binary is never mistaken for a release.

## Reporting a bug

Include the version (the footer of the app, or `strideassetstore --version`), your OS, and what you
expected. For install problems, the output of `strideassetstore list` and the relevant part of your
`.csproj` is usually enough to reproduce.
