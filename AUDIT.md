# Audit avant la 1.0.0

Chaque fichier est relu **trois fois**. Une case n'est cochée que lorsque le fichier a été lu en
entier sur cette passe et que ce qui devait être corrigé l'a été.

Ce qu'on cherche : du code mort, des incohérences, des affirmations fausses, des bugs — et dans
tout texte affiché à l'utilisateur, des notes de développement qui n'ont rien à y faire. Le store
doit être clair, précis, concis et juste.

| Passe | État |
|---|---|
| 1 | 0 / 154 |
| 2 | 0 / 154 |
| 3 | 0 / 154 |

## Fichiers

| # | Fichier | P1 | P2 | P3 |
|---|---|---|---|---|
| 1 | `.github/workflows/asset-e2e.yml` | ☐ | ☐ | ☐ |
| 2 | `.github/workflows/ci.yml` | ☐ | ☐ | ☐ |
| 3 | `.github/workflows/deploy-pages.yml` | ☐ | ☐ | ☐ |
| 4 | `.github/workflows/release.yml` | ☐ | ☐ | ☐ |
| 5 | `CONTRIBUTING.md` | ☐ | ☐ | ☐ |
| 6 | `Directory.Build.props` | ☐ | ☐ | ☐ |
| 7 | `Directory.Build.targets` | ☐ | ☐ | ☐ |
| 8 | `LICENSE.md` | ☐ | ☐ | ☐ |
| 9 | `README.md` | ☐ | ☐ | ☐ |
| 10 | `StrideAssetStore.slnx` | ☐ | ☐ | ☐ |
| 11 | `WORK.md` | ☐ | ☐ | ☐ |
| 12 | `docs/announce-discord.md` | ☐ | ☐ | ☐ |
| 13 | `src/StrideAssetStore.App/Program.cs` | ☐ | ☐ | ☐ |
| 14 | `src/StrideAssetStore.App/Properties/launchSettings.json` | ☐ | ☐ | ☐ |
| 15 | `src/StrideAssetStore.App/StrideAssetStore.App.csproj` | ☐ | ☐ | ☐ |
| 16 | `src/StrideAssetStore.App/_Imports.razor` | ☐ | ☐ | ☐ |
| 17 | `src/StrideAssetStore.App/wwwroot/appsettings.json` | ☐ | ☐ | ☐ |
| 18 | `src/StrideAssetStore.App/wwwroot/data/categories.json` | ☐ | ☐ | ☐ |
| 19 | `src/StrideAssetStore.App/wwwroot/data/licenses.json` | ☐ | ☐ | ☐ |
| 20 | `src/StrideAssetStore.Cli/CommandHelpers.cs` | ☐ | ☐ | ☐ |
| 21 | `src/StrideAssetStore.Cli/Commands/AddCommand.cs` | ☐ | ☐ | ☐ |
| 22 | `src/StrideAssetStore.Cli/Commands/AppCommands.cs` | ☐ | ☐ | ☐ |
| 23 | `src/StrideAssetStore.Cli/Commands/BuildIndexCommand.cs` | ☐ | ☐ | ☐ |
| 24 | `src/StrideAssetStore.Cli/Commands/ConsumerSettings.cs` | ☐ | ☐ | ☐ |
| 25 | `src/StrideAssetStore.Cli/Commands/GeneratePagesCommand.cs` | ☐ | ☐ | ☐ |
| 26 | `src/StrideAssetStore.Cli/Commands/ListCommand.cs` | ☐ | ☐ | ☐ |
| 27 | `src/StrideAssetStore.Cli/Commands/RemoveCommand.cs` | ☐ | ☐ | ☐ |
| 28 | `src/StrideAssetStore.Cli/Commands/SearchCommand.cs` | ☐ | ☐ | ☐ |
| 29 | `src/StrideAssetStore.Cli/Commands/SharedSettings.cs` | ☐ | ☐ | ☐ |
| 30 | `src/StrideAssetStore.Cli/Commands/UpdateCommand.cs` | ☐ | ☐ | ☐ |
| 31 | `src/StrideAssetStore.Cli/Commands/ValidateCommand.cs` | ☐ | ☐ | ☐ |
| 32 | `src/StrideAssetStore.Cli/Commands/ValidateSettings.cs` | ☐ | ☐ | ☐ |
| 33 | `src/StrideAssetStore.Cli/GitHubStars.cs` | ☐ | ☐ | ☐ |
| 34 | `src/StrideAssetStore.Cli/Local/CatalogAccess.cs` | ☐ | ☐ | ☐ |
| 35 | `src/StrideAssetStore.Cli/Local/CliOutput.cs` | ☐ | ☐ | ☐ |
| 36 | `src/StrideAssetStore.Cli/Local/ProjectTarget.cs` | ☐ | ☐ | ☐ |
| 37 | `src/StrideAssetStore.Cli/Program.cs` | ☐ | ☐ | ☐ |
| 38 | `src/StrideAssetStore.Cli/README.md` | ☐ | ☐ | ☐ |
| 39 | `src/StrideAssetStore.Cli/StrideAssetStore.Cli.csproj` | ☐ | ☐ | ☐ |
| 40 | `src/StrideAssetStore.Core.Local/Catalog/FileCatalogStorage.cs` | ☐ | ☐ | ☐ |
| 41 | `src/StrideAssetStore.Core.Local/Dependencies/DependencyResolver.cs` | ☐ | ☐ | ☐ |
| 42 | `src/StrideAssetStore.Core.Local/Git/ForkLister.cs` | ☐ | ☐ | ☐ |
| 43 | `src/StrideAssetStore.Core.Local/Git/GitClient.cs` | ☐ | ☐ | ☐ |
| 44 | `src/StrideAssetStore.Core.Local/Hashing/ContentHasher.cs` | ☐ | ☐ | ☐ |
| 45 | `src/StrideAssetStore.Core.Local/Indexing/GitAssetSource.cs` | ☐ | ☐ | ☐ |
| 46 | `src/StrideAssetStore.Core.Local/Indexing/IAssetSource.cs` | ☐ | ☐ | ☐ |
| 47 | `src/StrideAssetStore.Core.Local/Indexing/IndexBuilder.cs` | ☐ | ☐ | ☐ |
| 48 | `src/StrideAssetStore.Core.Local/Indexing/LocalAssetSource.cs` | ☐ | ☐ | ☐ |
| 49 | `src/StrideAssetStore.Core.Local/Install/AssetInstaller.cs` | ☐ | ☐ | ☐ |
| 50 | `src/StrideAssetStore.Core.Local/Projects/CsprojEditor.cs` | ☐ | ☐ | ☐ |
| 51 | `src/StrideAssetStore.Core.Local/Projects/CsprojInspector.cs` | ☐ | ☐ | ☐ |
| 52 | `src/StrideAssetStore.Core.Local/Projects/SolutionInspector.cs` | ☐ | ☐ | ☐ |
| 53 | `src/StrideAssetStore.Core.Local/Releases/DesktopAppInstaller.cs` | ☐ | ☐ | ☐ |
| 54 | `src/StrideAssetStore.Core.Local/Releases/RunningApp.cs` | ☐ | ☐ | ☐ |
| 55 | `src/StrideAssetStore.Core.Local/Shell/DesktopShell.cs` | ☐ | ☐ | ☐ |
| 56 | `src/StrideAssetStore.Core.Local/StrideAssetStore.Core.Local.csproj` | ☐ | ☐ | ☐ |
| 57 | `src/StrideAssetStore.Core.Local/Validation/AssetValidator.cs` | ☐ | ☐ | ☐ |
| 58 | `src/StrideAssetStore.Core.Local/Validation/Catalog.cs` | ☐ | ☐ | ☐ |
| 59 | `src/StrideAssetStore.Core.Local/Validation/SchemaValidator.cs` | ☐ | ☐ | ☐ |
| 60 | `src/StrideAssetStore.Core.Local/Validation/ValidationReport.cs` | ☐ | ☐ | ☐ |
| 61 | `src/StrideAssetStore.Core/Catalog/AssetCatalog.cs` | ☐ | ☐ | ☐ |
| 62 | `src/StrideAssetStore.Core/Catalog/CatalogCache.cs` | ☐ | ☐ | ☐ |
| 63 | `src/StrideAssetStore.Core/Catalog/CatalogDefaults.cs` | ☐ | ☐ | ☐ |
| 64 | `src/StrideAssetStore.Core/Catalog/CatalogLoader.cs` | ☐ | ☐ | ☐ |
| 65 | `src/StrideAssetStore.Core/Catalog/CatalogSources.cs` | ☐ | ☐ | ☐ |
| 66 | `src/StrideAssetStore.Core/Catalog/StarsHistory.cs` | ☐ | ☐ | ☐ |
| 67 | `src/StrideAssetStore.Core/Catalog/StrideVersionMatcher.cs` | ☐ | ☐ | ☐ |
| 68 | `src/StrideAssetStore.Core/Models/AssetId.cs` | ☐ | ☐ | ☐ |
| 69 | `src/StrideAssetStore.Core/Models/AssetManifest.cs` | ☐ | ☐ | ☐ |
| 70 | `src/StrideAssetStore.Core/Models/Author.cs` | ☐ | ☐ | ☐ |
| 71 | `src/StrideAssetStore.Core/Models/IndexLock.cs` | ☐ | ☐ | ☐ |
| 72 | `src/StrideAssetStore.Core/Models/NugetPackage.cs` | ☐ | ☐ | ☐ |
| 73 | `src/StrideAssetStore.Core/Models/RegistryEntry.cs` | ☐ | ☐ | ☐ |
| 74 | `src/StrideAssetStore.Core/Releases/DesktopBuilds.cs` | ☐ | ☐ | ☐ |
| 75 | `src/StrideAssetStore.Core/Serialization/AssetStoreJson.cs` | ☐ | ☐ | ☐ |
| 76 | `src/StrideAssetStore.Core/StrideAssetStore.Core.csproj` | ☐ | ☐ | ☐ |
| 77 | `src/StrideAssetStore.Desktop/Components/App.razor` | ☐ | ☐ | ☐ |
| 78 | `src/StrideAssetStore.Desktop/Components/FileBrowser.razor` | ☐ | ☐ | ☐ |
| 79 | `src/StrideAssetStore.Desktop/Components/Pages/Install.razor` | ☐ | ☐ | ☐ |
| 80 | `src/StrideAssetStore.Desktop/Components/Pages/MyAssets.razor` | ☐ | ☐ | ☐ |
| 81 | `src/StrideAssetStore.Desktop/Components/Pages/MyProjects.razor` | ☐ | ☐ | ☐ |
| 82 | `src/StrideAssetStore.Desktop/Components/Pages/NewAsset.razor` | ☐ | ☐ | ☐ |
| 83 | `src/StrideAssetStore.Desktop/Components/Routes.razor` | ☐ | ☐ | ☐ |
| 84 | `src/StrideAssetStore.Desktop/Program.cs` | ☐ | ☐ | ☐ |
| 85 | `src/StrideAssetStore.Desktop/Properties/launchSettings.json` | ☐ | ☐ | ☐ |
| 86 | `src/StrideAssetStore.Desktop/Services/AssetScaffolder.cs` | ☐ | ☐ | ☐ |
| 87 | `src/StrideAssetStore.Desktop/Services/AuthorRepoService.cs` | ☐ | ☐ | ☐ |
| 88 | `src/StrideAssetStore.Desktop/Services/GhCliPublisher.cs` | ☐ | ☐ | ☐ |
| 89 | `src/StrideAssetStore.Desktop/Services/GlobalHotkeys.cs` | ☐ | ☐ | ☐ |
| 90 | `src/StrideAssetStore.Desktop/Services/ProjectStore.cs` | ☐ | ☐ | ☐ |
| 91 | `src/StrideAssetStore.Desktop/Services/ProtocolLauncher.cs` | ☐ | ☐ | ☐ |
| 92 | `src/StrideAssetStore.Desktop/StrideAssetStore.Desktop.csproj` | ☐ | ☐ | ☐ |
| 93 | `src/StrideAssetStore.Desktop/_Imports.razor` | ☐ | ☐ | ☐ |
| 94 | `src/StrideAssetStore.Desktop/wwwroot/data/categories.json` | ☐ | ☐ | ☐ |
| 95 | `src/StrideAssetStore.Desktop/wwwroot/data/licenses.json` | ☐ | ☐ | ☐ |
| 96 | `src/StrideAssetStore.UI/App.razor` | ☐ | ☐ | ☐ |
| 97 | `src/StrideAssetStore.UI/Components/AssetCard.razor` | ☐ | ☐ | ☐ |
| 98 | `src/StrideAssetStore.UI/Components/AssistedPrPanel.razor` | ☐ | ☐ | ☐ |
| 99 | `src/StrideAssetStore.UI/Components/CliPrPanel.razor` | ☐ | ☐ | ☐ |
| 100 | `src/StrideAssetStore.UI/Components/GitHubSignIn.razor` | ☐ | ☐ | ☐ |
| 101 | `src/StrideAssetStore.UI/Components/MethodPicker.razor` | ☐ | ☐ | ☐ |
| 102 | `src/StrideAssetStore.UI/Components/Modal.razor` | ☐ | ☐ | ☐ |
| 103 | `src/StrideAssetStore.UI/Components/PrResult.razor` | ☐ | ☐ | ☐ |
| 104 | `src/StrideAssetStore.UI/Components/PublishMethod.cs` | ☐ | ☐ | ☐ |
| 105 | `src/StrideAssetStore.UI/Layout/MainLayout.razor` | ☐ | ☐ | ☐ |
| 106 | `src/StrideAssetStore.UI/Pages/About.razor` | ☐ | ☐ | ☐ |
| 107 | `src/StrideAssetStore.UI/Pages/AssetDetail.razor` | ☐ | ☐ | ☐ |
| 108 | `src/StrideAssetStore.UI/Pages/Download.razor` | ☐ | ☐ | ☐ |
| 109 | `src/StrideAssetStore.UI/Pages/Home.razor` | ☐ | ☐ | ☐ |
| 110 | `src/StrideAssetStore.UI/Pages/ManifestGenerator.razor` | ☐ | ☐ | ☐ |
| 111 | `src/StrideAssetStore.UI/Pages/NotFound.razor` | ☐ | ☐ | ☐ |
| 112 | `src/StrideAssetStore.UI/Pages/Publish.razor` | ☐ | ☐ | ☐ |
| 113 | `src/StrideAssetStore.UI/Pages/Setup.razor` | ☐ | ☐ | ☐ |
| 114 | `src/StrideAssetStore.UI/ServiceCollectionExtensions.cs` | ☐ | ☐ | ☐ |
| 115 | `src/StrideAssetStore.UI/Services/AppEnvironment.cs` | ☐ | ☐ | ☐ |
| 116 | `src/StrideAssetStore.UI/Services/AppInfo.cs` | ☐ | ☐ | ☐ |
| 117 | `src/StrideAssetStore.UI/Services/AttentionState.cs` | ☐ | ☐ | ☐ |
| 118 | `src/StrideAssetStore.UI/Services/CatalogState.cs` | ☐ | ☐ | ☐ |
| 119 | `src/StrideAssetStore.UI/Services/CliPublishing.cs` | ☐ | ☐ | ☐ |
| 120 | `src/StrideAssetStore.UI/Services/GitHubAuth.cs` | ☐ | ☐ | ☐ |
| 121 | `src/StrideAssetStore.UI/Services/GitHubPublisher.cs` | ☐ | ☐ | ☐ |
| 122 | `src/StrideAssetStore.UI/Services/GitLinks.cs` | ☐ | ☐ | ☐ |
| 123 | `src/StrideAssetStore.UI/Services/LocalStorageCatalogCache.cs` | ☐ | ☐ | ☐ |
| 124 | `src/StrideAssetStore.UI/Services/MarkdownRenderer.cs` | ☐ | ☐ | ☐ |
| 125 | `src/StrideAssetStore.UI/Services/RegistryOptions.cs` | ☐ | ☐ | ☐ |
| 126 | `src/StrideAssetStore.UI/Services/UpdateService.cs` | ☐ | ☐ | ☐ |
| 127 | `src/StrideAssetStore.UI/StrideAssetStore.UI.csproj` | ☐ | ☐ | ☐ |
| 128 | `src/StrideAssetStore.UI/_Imports.razor` | ☐ | ☐ | ☐ |
| 129 | `src/StrideAssetStore.UI/wwwroot/app.css` | ☐ | ☐ | ☐ |
| 130 | `src/StrideAssetStore.UI/wwwroot/js/interop.js` | ☐ | ☐ | ☐ |
| 131 | `tests/StrideAssetStore.Core.Local.Tests/AssetInstallerTests.cs` | ☐ | ☐ | ☐ |
| 132 | `tests/StrideAssetStore.Core.Local.Tests/InstallerWorkspace.cs` | ☐ | ☐ | ☐ |
| 133 | `tests/StrideAssetStore.Core.Local.Tests/StrideAssetStore.Core.Local.Tests.csproj` | ☐ | ☐ | ☐ |
| 134 | `tests/StrideAssetStore.Core.Local.Tests/xunit.runner.json` | ☐ | ☐ | ☐ |
| 135 | `tests/StrideAssetStore.Core.Tests/AssetCatalogTests.cs` | ☐ | ☐ | ☐ |
| 136 | `tests/StrideAssetStore.Core.Tests/AssetValidatorTests.cs` | ☐ | ☐ | ☐ |
| 137 | `tests/StrideAssetStore.Core.Tests/CatalogLoaderTests.cs` | ☐ | ☐ | ☐ |
| 138 | `tests/StrideAssetStore.Core.Tests/CatalogTestData.cs` | ☐ | ☐ | ☐ |
| 139 | `tests/StrideAssetStore.Core.Tests/ContentHasherTests.cs` | ☐ | ☐ | ☐ |
| 140 | `tests/StrideAssetStore.Core.Tests/CsprojEditorTests.cs` | ☐ | ☐ | ☐ |
| 141 | `tests/StrideAssetStore.Core.Tests/CsprojInspectorTests.cs` | ☐ | ☐ | ☐ |
| 142 | `tests/StrideAssetStore.Core.Tests/DependencyResolverTests.cs` | ☐ | ☐ | ☐ |
| 143 | `tests/StrideAssetStore.Core.Tests/GitClientTests.cs` | ☐ | ☐ | ☐ |
| 144 | `tests/StrideAssetStore.Core.Tests/GitTagParsingTests.cs` | ☐ | ☐ | ☐ |
| 145 | `tests/StrideAssetStore.Core.Tests/IncrementalBuildTests.cs` | ☐ | ☐ | ☐ |
| 146 | `tests/StrideAssetStore.Core.Tests/IndexBuilderTests.cs` | ☐ | ☐ | ☐ |
| 147 | `tests/StrideAssetStore.Core.Tests/IndexLockSchemaTests.cs` | ☐ | ☐ | ☐ |
| 148 | `tests/StrideAssetStore.Core.Tests/ManifestSerializationTests.cs` | ☐ | ☐ | ☐ |
| 149 | `tests/StrideAssetStore.Core.Tests/SolutionInspectorTests.cs` | ☐ | ☐ | ☐ |
| 150 | `tests/StrideAssetStore.Core.Tests/StarsHistoryTests.cs` | ☐ | ☐ | ☐ |
| 151 | `tests/StrideAssetStore.Core.Tests/StrideAssetStore.Core.Tests.csproj` | ☐ | ☐ | ☐ |
| 152 | `tests/StrideAssetStore.Core.Tests/StrideVersionMatcherTests.cs` | ☐ | ☐ | ☐ |
| 153 | `tests/StrideAssetStore.Core.Tests/SyntheticWorkspace.cs` | ☐ | ☐ | ☐ |
| 154 | `tests/StrideAssetStore.Core.Tests/TestPaths.cs` | ☐ | ☐ | ☐ |

## Corrigé

_(rien encore sur cette passe)_

