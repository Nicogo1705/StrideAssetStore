# StrideAssetStore

Command-line companion to the **[Community Stride Asset Store](https://nicogo1705.github.io/StrideAssetStore/)** —
an unofficial, decentralized asset index for the [Stride](https://stride3d.net) game engine.

Assets are not hosted anywhere: each one lives in its author's own public Git repository. This tool
finds them, clones them into your game, keeps them up to date, and takes them back out.

> Unofficial community project. Not affiliated with, endorsed by, or operated by Stride or the
> .NET Foundation.

## Install

```bash
dotnet tool install -g StrideAssetStore
```

`git` must be on your PATH — installing an asset clones its repository.

## Use it

Run these from your game's folder. The nearest `.sln`, `.slnx` or `.csproj` is found by walking up
from the current directory, so there is usually nothing to configure.

```bash
strideassetstore search grass              # find something
strideassetstore add grass                 # install it into this project
strideassetstore list                      # what this project references, and its status
strideassetstore update                    # bring every outdated asset up to date
strideassetstore remove grass              # take it back out
```

An asset id is long, so any unambiguous fragment works: `add grass` resolves to
`com.nicogo.grass`. When it is ambiguous the command says so instead of guessing.

### Versions

By default an asset follows the branch its author publishes from. To pin a released version:

```bash
strideassetstore add grass --version 1.0.0     # a tag the author published
strideassetstore update grass --version 1.1.0  # move an installed asset onto another version
strideassetstore add grass --ref my-branch     # or a raw git ref
```

Each version is cloned into its own folder of a shared per-machine cache, so several versions of
the same asset coexist and different projects can follow different ones. The reference written into
your `.csproj` is portable — a teammate who runs `strideassetstore update` gets the same code.


### Forks

To use someone's fork of an asset — or your own — instead of the author's repository:

```bash
strideassetstore forks grass                                  # what exists on GitHub
strideassetstore add grass --fork someone/StrideGrassSystem
strideassetstore add grass --fork you/StrideGrassSystem --ref my-branch
```

A fork keeps its own tags and its own history, so **nothing the registry says about the asset
applies to it**: the content hash isn't verified and no certification carries over. It is cached
under its own folder, so the official asset on your machine is untouched.

The fork is recorded on the project reference itself:

```xml
<ProjectReference Include="…\StrideGrassSystem__you\…" Fork="you/StrideGrassSystem" />
```

so it travels with your project — a teammate who clones the repository and runs `update` follows
the same fork, without being told. `list` shows those assets as `fork` rather than `local`.

### Keeping the tool itself up to date

```bash
dotnet tool update -g StrideAssetStore
```

The tool checks nuget.org for a newer version at most once a day, in the background, and mentions it
after the command's output. Set `STRIDEASSETSTORE_NO_UPDATE_CHECK=1` (or `NO_COLOR`) to turn that
off; it is skipped automatically when output is redirected.

Retargeting rewrites the asset's own project files in the shared cache, so it applies to every
project that references that clone — not just this one.

### Solutions with more than one project

```bash
strideassetstore add grass --project MyGame.Windows
strideassetstore add grass --all-projects
```

Both apply to `remove` as well. Without either, a command that would touch several projects stops
and lists them rather than picking one.

### Stride versions

An asset targets whatever Stride version its author used. When your game is on a different build,
retarget it as you install:

```bash
strideassetstore add grass --stride 4.4.0-beta5
```

Without this, your project restores two Stride versions at once — or fails outright if the author's
version isn't on your feeds.

### The desktop app

The same store with a UI, an install wizard and a publishing flow. This tool manages its whole
lifecycle:

```bash
strideassetstore app install     # download and install it for this machine
strideassetstore app status      # what's installed, what's running, what's been released
strideassetstore app start       # run it (serves http://localhost:5111)
strideassetstore app stop        # quit it
strideassetstore app update      # update it
strideassetstore app open        # open the store in a browser
```

`update` is how the desktop app gets updated — it never replaces itself, so an update doesn't
depend on its interface working. It stops a running app before replacing its files — on Windows a
running executable is locked, and extracting over it would leave a broken install — then starts it
again if it was running.
`start` won't launch a second copy, and waits until the app actually answers before saying so.

### Scripts and CI

`--yes` skips confirmations, `--offline` uses the catalog snapshot cached on the machine, and every
command returns a non-zero exit code on failure. Colours are dropped automatically when output is
redirected. Set `GITHUB_TOKEN` to lift the anonymous GitHub API rate limit.

## Registry maintenance

The same tool carries the commands behind the registry itself — `validate`, `build-index` and
`generate-pages`. They need a checkout of the
[AssetContainer](https://github.com/Nicogo1705/AssetContainer) repository and are documented there.

## Publishing an asset

Publishing is a pull request adding one JSON entry to the registry. The desktop app has a wizard for
it; the store's [Publish page](https://nicogo1705.github.io/StrideAssetStore/publish) explains the
manual route.

## License

MIT — [source](https://github.com/Nicogo1705/StrideAssetStore).
