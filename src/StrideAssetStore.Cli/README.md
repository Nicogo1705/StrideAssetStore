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
strideassetstore info grass                # everything about it, including its published versions
strideassetstore demo grass                # download, build and run its demo — any platform
strideassetstore add grass                 # install it into this project
strideassetstore list                      # what this project references, and its status
strideassetstore update                    # bring every outdated asset up to date
strideassetstore remove grass              # take it back out
```

An asset id is long, so any unambiguous fragment works: `add grass` resolves to
`com.nicogo.grass`. When it is ambiguous the command says so instead of guessing.

### Trying an asset before installing it

```bash
strideassetstore demo grass
```

Assets that ship a `Demo/Demo.csproj` can be run in one command: it downloads the asset into the
shared cache, unpacks the demo from that same clone, builds it and starts it. The demo is one
project that runs on Windows, Linux and macOS — Stride picks its graphics API from the machine it
is built on.

To fetch an asset without installing it anywhere — filling the cache before a flight, or looking at
the source before deciding:

```bash
strideassetstore download grass          # into the shared cache, no project touched
strideassetstore download grass --demo   # and unpack its demo, ready to build
```

It is the same clone `add` uses, in the same place, so installing it later finds it already there.
`list --cached` shows everything downloaded.

It asks first, and says whose repository the code comes from. Installing an asset puts source in a
project you then choose to compile; this compiles and runs somebody else's code on the spot, which
is a different thing and deserves the question. `--no-run` stops after the build.

### Versions

By default an asset follows the branch its author publishes from. To pin a released version:

```bash
strideassetstore info grass                    # which versions exist, and which are certified
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

### A shorter name

```bash
strideassetstore alias              # `sas add grass` from now on
strideassetstore alias --name sast  # or your own
strideassetstore alias --remove
```

A NuGet tool package can only declare one command, so the short name cannot ship inside it. This
writes a two-line shim next to the tool itself, in the folder that is already on your `PATH` — no
shell profile is touched, and it works the same in cmd, PowerShell and any Unix shell. `uninstall`
takes it away again.

### Keeping everything up to date

```bash
strideassetstore upgrade    # this tool, then the desktop app
```

It reports both versions, updates the app here (with its progress bar), and hands the tool half to
a terminal that opens once the command exits — a global tool cannot replace its own files while it
runs. `update` remains the one for assets; these are the two programs, not the content.

Only the tool, without touching the app:

```bash
dotnet tool update -g StrideAssetStore
```

The tool checks nuget.org for a newer version at most once a day, in the background, and mentions it
after the command's output — and every time you run `strideassetstore` with no arguments at all, which
is someone looking it over rather than using it. Set `STRIDEASSETSTORE_NO_UPDATE_CHECK=1` (or
`NO_COLOR`) to turn that off; it is skipped automatically when output is redirected.

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

Retargeting rewrites the asset's own project files in the shared cache, so it applies to every
project that references that clone — not just this one.

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

### Removing it all

```bash
strideassetstore uninstall          # the app, the downloaded assets and the settings
strideassetstore uninstall --app    # only the desktop app
strideassetstore uninstall --cache  # only the downloaded assets
```

It stops the app before removing it, and says what it is about to delete before doing it. Projects
that reference downloaded assets stop building until `add` or `update` fetches them again.

The tool cannot remove itself while it runs — `dotnet` would be deleting the executable of the
process asking. It prints the last step for you:

```bash
dotnet tool uninstall -g StrideAssetStore
```

### Scripts and CI

`--yes` skips confirmations, `--offline` uses the catalog snapshot cached on the machine, and every
command returns a non-zero exit code on failure. Colours are dropped automatically when output is
redirected. Set `GITHUB_TOKEN` to lift the anonymous GitHub API rate limit.

## Publishing an asset of your own

```bash
strideassetstore new StrideCoolThing     # a repo from the store's template, renamed and pushed
strideassetstore check                   # read it the way the store will, before anyone else does
```

`new` is the desktop app's "New asset" wizard as a command: it instantiates the template on your
GitHub account with `gh`, clones it, applies every rename, writes the manifest and pushes. The
display name and the id are derived from the repository name and your GitHub login unless you pass
`--name` and `--id`.

`check` runs where you are, on the repository you are in: manifest present and complete, a valid
store id, the thumbnail and media it declares actually there, a README to render on the asset page,
a project under `AssetData/`, and no build output committed into the folder every user clones.
`--strict` makes warnings fail too, for CI.

Release a version — the store reads versions from git tags:

```bash
strideassetstore tag          # the next patch after your latest tag
strideassetstore tag 1.2.0    # or the one you mean
```

`git tag` would do the mechanical half. This one refuses to tag a commit the world cannot fetch —
uncommitted changes, unpushed commits, no upstream — because a tag on a commit only you have
installs as nothing for everyone else, and nothing complains until someone tries.

Then submit it, from the same folder:

```bash
strideassetstore publish
```

It reads the id from your manifest, the repository from the `origin` remote and the followed branch
from HEAD, runs `check` first (a pull request pointing at a broken repository only wastes a
maintainer's time), and opens the pull request on the registry through `gh` — fork, branch, entry
file, PR. `--ref` follows a different branch, `--force` submits anyway.

Once it is published, the same three commands the app's **Manage store assets** page offers:

```bash
strideassetstore certify com.you.cool-thing --version 1.0.0 --commit <40-char-sha>
strideassetstore deprecate com.you.cool-thing --reason "Superseded" --successor com.you.better-thing
strideassetstore unpublish com.you.cool-thing
```

`certify` pins a reviewed commit as immutable — the sha is stated, never derived from the tag,
because a tag can be moved afterwards. `deprecate` leaves the asset installable and tells readers
to look elsewhere. `unpublish` deletes the entry, which breaks `add` for everyone using it, so it
asks first and points you at `deprecate`. All three open a pull request; a maintainer merges it.

## Registry maintenance

The same tool carries the commands behind the registry itself — `validate`, `build-index` and
`generate-pages`. They need a checkout of the
[AssetContainer](https://github.com/Nicogo1705/AssetContainer) repository and are documented there.

## License

MIT — [source](https://github.com/Nicogo1705/StrideAssetStore).
