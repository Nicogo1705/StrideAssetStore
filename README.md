# StrideAssetStore — Community Stride Asset Store (app & tools)
[![Community Stride Asset Store](https://img.shields.io/badge/Community_Stride_Asset_Store-browse-5b8def)](https://nicogo1705.github.io/StrideAssetStore/)

> ⚠️ **Unofficial** community project — a community-built, **decentralized asset indexer** for the
> [Stride](https://stride3d.net) game engine. **Not affiliated with, endorsed by, or operated by
> Stride / the .NET Foundation.** Built so it *could* be adopted/integrated by the Stride community
> later (config-only) if wanted — a possibility, not a plan. See the companion **AssetContainer**
> repository for the registry, schemas and CI.

This solution is the C# code: a reusable core, a CLI, a web storefront, a shared UI, and a
cross-platform desktop app. Assets are not hosted here — each asset lives in its author's own
public Git repo; this just indexes and installs them.

## Projects

| Project | Description |
|---|---|
| `src/StrideAssetStore.Core` | Pure .NET 10 library, **no filesystem or git**: models, JSON-Schema validation, catalog loading/search. This is all the browser storefront needs, so it is all the browser storefront gets. |
| `src/StrideAssetStore.Core.Local` | Everything that requires a real machine: git client, deterministic `AssetData/` hashing, `.csproj`/`.sln` inspection and editing, dependency resolution, index building, and the installer (clone, shared cache, references, update, uninstall). Used by the CLI and the desktop app; **never** by the WASM storefront. |
| `src/StrideAssetStore.Cli` | The `strideassetstore` global tool. For anyone using assets: `search`, `add`, `list`, `update`, `remove`. For registry maintainers: `validate`, `build-index` (`--incremental`, `--stars`, `--source git`), `generate-pages` (static per-asset share pages + sitemap + Atom feed). |
| `src/StrideAssetStore.UI` | Shared Razor class library (components, pages, services) used by both hosts. |
| `src/StrideAssetStore.App` | **Blazor WebAssembly** storefront for GitHub Pages: browse / search / filter / sort (state in the URL), asset detail, and the **publish** wizard (fork + PR via a GitHub token). No local access → no install; its Install button hands over to the desktop app via `stride-assetstore://`. |
| `src/StrideAssetStore.Desktop` | **Blazor Server** local app (Windows / Linux / macOS) that opens the browser and has full filesystem + git access: **install** an asset — as source (clone + `<ProjectReference>`, with dependencies) or as a **NuGet package** (`<PackageReference>`) when the asset ships one — into a project, or as a **shared asset** (download only, attach later). Source installs land in a **versioned shared cache** (`…\StrideAssetStore\Assets\<ref>\<name>`), so several versions coexist and up-to-date is checked against the ref each clone follows. **My projects** manages tracked solutions (update / attach a downloaded asset / uninstall with `.sln` cleanup); **My assets** browses the cache itself. Registers the `stride-assetstore://` protocol on Windows so the web storefront can open it. |
| `tests/StrideAssetStore.Core.Tests` | xUnit tests (incl. end-to-end index builds against synthetic git workspaces). |

`StrideAssetStore.App` = the online storefront; `StrideAssetStore.Desktop` = the local power tool. Both share
`StrideAssetStore.UI`.

## CLI usage

```bash
# Validate every registry entry + manifest (schemas, catalog, Stride version, dependencies)
dotnet run --project src/StrideAssetStore.Cli -- validate --container ../AssetContainer --source git

# Generate the aggregated index consumed by the apps
dotnet run --project src/StrideAssetStore.Cli -- build-index --container ../AssetContainer --source git --stars

# Cheap incremental refresh (only re-fetch assets whose ref moved) — used by the daily CI job
dotnet run --project src/StrideAssetStore.Cli -- build-index --container ../AssetContainer --source git --incremental --stars

# Static per-asset share pages (Open Graph, incl. og:video) + sitemap + Atom feed — used by the
# Pages deploy. With --app-index each a/<id>/ page IS the SPA shell with the meta injected, so the
# address bar is directly the shareable/embeddable URL (Discord cards).
dotnet run --project src/StrideAssetStore.Cli -- generate-pages --index index.lock.json --out publish/wwwroot --site https://user.github.io/StrideAssetStore --app-index publish/wwwroot/index.html
```

`--source local` (default) reads asset checkouts sitting next to `AssetContainer`; `--source git`
clones them.

## Run the apps

```bash
# Online storefront (WASM)
dotnet run --project src/StrideAssetStore.App

# desktop app (opens http://localhost:5111 in your browser, enables install)
dotnet run --project src/StrideAssetStore.Desktop
```

## Configuration

- **Registry location** (`Registry` section → `RegistryOptions`): `Owner` / `Repo` / `BaseBranch`.
  Defaults to `Nicogo1705/AssetContainer/main`; change it (config only, no code) to point at another
  org — e.g. a Stride community org, should the project ever be adopted.
- **Catalog index** (`Catalog:IndexUrl`): where the WASM app fetches `index.lock.json`.

## What the core does

- **Validation**: registry entry + manifest against JSON Schema (with `format` assertions), catalog
  (category/license), id/file-name consistency, `https://`-only repo URLs.
- **Integrity**: pins the resolved git commit + a deterministic, OS-independent SHA-256 of `AssetData/`;
  the installer re-verifies the hash after cloning.
- **Security**: git is invoked with no shell, `ext`/`file` transports disabled, and option-like
  arguments rejected; clone folder names are sanitized against path traversal.
- **Stride version**: detected from the `.csproj` (`Stride.* PackageReference`).
- **Dependencies**: transitive resolution by `id` (cycle/missing detection), auto-derived from
  `<ProjectReference>`.

## Build & test

```bash
dotnet build
dotnet test
```

## License

MIT. See [LICENSE.md](LICENSE.md).
