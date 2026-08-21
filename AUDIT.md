# Audit avant la 1.0.0

Chaque fichier est relu **trois fois**. Une case n'est cochée que lorsque le fichier a été lu en
entier sur cette passe et que ce qui devait être corrigé l'a été.

Ce qu'on cherche : du code mort, des incohérences, des affirmations fausses, des bugs — et dans
tout texte affiché à l'utilisateur, des notes de développement qui n'ont rien à y faire. Le store
doit être clair, précis, concis et juste.

La liste est générée depuis `git ls-files` (hors binaires et hors ce fichier), pour qu'un fichier
ajouté entre deux passes ne puisse pas passer entre les mailles.

| Passe | État |
|---|---|
| 1 | 158 / 158 |
| 2 | 0 / 158 |
| 3 | 0 / 158 |

## Passe 1 — corrections appliquées

Lecture intégrale, répartie sur six relectures parallèles (Core, Core.Local, CLI, UI, Desktop,
CI + docs + registre). Ce qui en est sorti et a été corrigé :

- **Fork cloné à vide.** Un fork était cloné avec le même filtre sparse `/AssetData/` qu'un asset du
  registre : le repli « cherche un projet dans tout le clone » parcourait un arbre qui ne pouvait rien
  contenir d'autre. Branches mortes sous un commentaire qui affirmait le contraire.
- **Contrôles de l'app joignables par n'importe quel site.** Un `Origin` absent était pris pour la barre
  d'adresse ; les navigateurs l'omettent sur toute sous-ressource GET, donc
  `<img src="http://localhost:5111/app/quit">` arrêtait l'app. `/app/update` n'avait aucun contrôle.
- **État de fork conservé d'un asset à l'autre** dans la page d'installation — installation depuis le
  mauvais dépôt.
- **Release publiée à moitié construite** : `gh release create` sans `--draft`, alors que le commentaire
  en fin de workflow décrivait l'inverse.
- **Blocage définitif** quand `git push` attendait des identifiants : plus de `WaitForExit` sans limite,
  et les invites sont désactivées.
- **Presse-papiers menteur** : « ✓ Copied » sans rien avoir copié hors contexte sécurisé.
- **Détection d'instance** par simple port ouvert : l'app ouvrait un onglet sur le serveur d'un autre outil.
- **`<base href>`** réécrit par un `sed` silencieux s'il ne matchait plus.
- **Métadonnées de version** : hash, taille et version de Stride du `latest` affichés pour un tag,
  alors que le catalogue ne les enregistre pas par tag.
- **Textes** : contradictions (mise à jour, serveur, token, « lu en direct depuis ton dépôt »), notes de
  développement, contrôles Windows proposés sur Linux et macOS.
- **Registre** : lien mort vers l'ancien dépôt, `commit` que « le bot remplit » et qui ne l'est jamais,
  CODEOWNERS décrit au champ près alors qu'il protège le dossier, « upgrader Stride » qui n'existe pas,
  PR de schéma vertes sans rien valider, `$id` sur un domaine qui n'est pas le nôtre.

À vérifier côté réglages GitHub (invisible depuis les fichiers) : la protection de branche de
`AssetContainer` doit laisser le bot pousser `index.lock.json`, que CODEOWNERS couvre.

## Fichiers

| # | Fichier | P1 | P2 | P3 |
|---|---|---|---|---|
| 1 | `.github/workflows/asset-e2e.yml` | ☑ | ☐ | ☐ |
| 2 | `.github/workflows/ci.yml` | ☑ | ☐ | ☐ |
| 3 | `.github/workflows/deploy-pages.yml` | ☑ | ☐ | ☐ |
| 4 | `.github/workflows/release.yml` | ☑ | ☐ | ☐ |
| 5 | `.gitignore` | ☑ | ☐ | ☐ |
| 6 | `CONTRIBUTING.md` | ☑ | ☐ | ☐ |
| 7 | `Directory.Build.props` | ☑ | ☐ | ☐ |
| 8 | `Directory.Build.targets` | ☑ | ☐ | ☐ |
| 9 | `LICENSE.md` | ☑ | ☐ | ☐ |
| 10 | `README.md` | ☑ | ☐ | ☐ |
| 11 | `StrideAssetStore.slnx` | ☑ | ☐ | ☐ |
| 12 | `WORK.md` | ☑ | ☐ | ☐ |
| 13 | `docs/announce-discord.md` | ☑ | ☐ | ☐ |
| 14 | `src/StrideAssetStore.App/Program.cs` | ☑ | ☐ | ☐ |
| 15 | `src/StrideAssetStore.App/Properties/launchSettings.json` | ☑ | ☐ | ☐ |
| 16 | `src/StrideAssetStore.App/StrideAssetStore.App.csproj` | ☑ | ☐ | ☐ |
| 17 | `src/StrideAssetStore.App/_Imports.razor` | ☑ | ☐ | ☐ |
| 18 | `src/StrideAssetStore.App/wwwroot/appsettings.json` | ☑ | ☐ | ☐ |
| 19 | `src/StrideAssetStore.App/wwwroot/data/categories.json` | ☑ | ☐ | ☐ |
| 20 | `src/StrideAssetStore.App/wwwroot/data/licenses.json` | ☑ | ☐ | ☐ |
| 21 | `src/StrideAssetStore.App/wwwroot/index.html` | ☑ | ☐ | ☐ |
| 22 | `src/StrideAssetStore.Cli/CommandHelpers.cs` | ☑ | ☐ | ☐ |
| 23 | `src/StrideAssetStore.Cli/Commands/AddCommand.cs` | ☑ | ☐ | ☐ |
| 24 | `src/StrideAssetStore.Cli/Commands/AppCommands.cs` | ☑ | ☐ | ☐ |
| 25 | `src/StrideAssetStore.Cli/Commands/BuildIndexCommand.cs` | ☑ | ☐ | ☐ |
| 26 | `src/StrideAssetStore.Cli/Commands/ConsumerSettings.cs` | ☑ | ☐ | ☐ |
| 27 | `src/StrideAssetStore.Cli/Commands/ForkListCommand.cs` | ☑ | ☐ | ☐ |
| 28 | `src/StrideAssetStore.Cli/Commands/GeneratePagesCommand.cs` | ☑ | ☐ | ☐ |
| 29 | `src/StrideAssetStore.Cli/Commands/ListCommand.cs` | ☑ | ☐ | ☐ |
| 30 | `src/StrideAssetStore.Cli/Commands/RemoveCommand.cs` | ☑ | ☐ | ☐ |
| 31 | `src/StrideAssetStore.Cli/Commands/SearchCommand.cs` | ☑ | ☐ | ☐ |
| 32 | `src/StrideAssetStore.Cli/Commands/SharedSettings.cs` | ☑ | ☐ | ☐ |
| 33 | `src/StrideAssetStore.Cli/Commands/UpdateCommand.cs` | ☑ | ☐ | ☐ |
| 34 | `src/StrideAssetStore.Cli/Commands/ValidateCommand.cs` | ☑ | ☐ | ☐ |
| 35 | `src/StrideAssetStore.Cli/Commands/ValidateSettings.cs` | ☑ | ☐ | ☐ |
| 36 | `src/StrideAssetStore.Cli/GitHubStars.cs` | ☑ | ☐ | ☐ |
| 37 | `src/StrideAssetStore.Cli/Local/CatalogAccess.cs` | ☑ | ☐ | ☐ |
| 38 | `src/StrideAssetStore.Cli/Local/CliOutput.cs` | ☑ | ☐ | ☐ |
| 39 | `src/StrideAssetStore.Cli/Local/ProjectTarget.cs` | ☑ | ☐ | ☐ |
| 40 | `src/StrideAssetStore.Cli/Program.cs` | ☑ | ☐ | ☐ |
| 41 | `src/StrideAssetStore.Cli/README.md` | ☑ | ☐ | ☐ |
| 42 | `src/StrideAssetStore.Cli/StrideAssetStore.Cli.csproj` | ☑ | ☐ | ☐ |
| 43 | `src/StrideAssetStore.Core.Local/Catalog/FileCatalogStorage.cs` | ☑ | ☐ | ☐ |
| 44 | `src/StrideAssetStore.Core.Local/Dependencies/DependencyResolver.cs` | ☑ | ☐ | ☐ |
| 45 | `src/StrideAssetStore.Core.Local/Git/ForkLister.cs` | ☑ | ☐ | ☐ |
| 46 | `src/StrideAssetStore.Core.Local/Git/GitClient.cs` | ☑ | ☐ | ☐ |
| 47 | `src/StrideAssetStore.Core.Local/Hashing/ContentHasher.cs` | ☑ | ☐ | ☐ |
| 48 | `src/StrideAssetStore.Core.Local/Indexing/GitAssetSource.cs` | ☑ | ☐ | ☐ |
| 49 | `src/StrideAssetStore.Core.Local/Indexing/IAssetSource.cs` | ☑ | ☐ | ☐ |
| 50 | `src/StrideAssetStore.Core.Local/Indexing/IndexBuilder.cs` | ☑ | ☐ | ☐ |
| 51 | `src/StrideAssetStore.Core.Local/Indexing/LocalAssetSource.cs` | ☑ | ☐ | ☐ |
| 52 | `src/StrideAssetStore.Core.Local/Install/AssetInstaller.cs` | ☑ | ☐ | ☐ |
| 53 | `src/StrideAssetStore.Core.Local/Projects/CsprojEditor.cs` | ☑ | ☐ | ☐ |
| 54 | `src/StrideAssetStore.Core.Local/Projects/CsprojInspector.cs` | ☑ | ☐ | ☐ |
| 55 | `src/StrideAssetStore.Core.Local/Projects/SolutionInspector.cs` | ☑ | ☐ | ☐ |
| 56 | `src/StrideAssetStore.Core.Local/Releases/DesktopAppInstaller.cs` | ☑ | ☐ | ☐ |
| 57 | `src/StrideAssetStore.Core.Local/Releases/RunningApp.cs` | ☑ | ☐ | ☐ |
| 58 | `src/StrideAssetStore.Core.Local/Shell/DesktopShell.cs` | ☑ | ☐ | ☐ |
| 59 | `src/StrideAssetStore.Core.Local/Shell/ProcessRunner.cs` | ☑ | ☐ | ☐ |
| 60 | `src/StrideAssetStore.Core.Local/StrideAssetStore.Core.Local.csproj` | ☑ | ☐ | ☐ |
| 61 | `src/StrideAssetStore.Core.Local/Validation/AssetValidator.cs` | ☑ | ☐ | ☐ |
| 62 | `src/StrideAssetStore.Core.Local/Validation/Catalog.cs` | ☑ | ☐ | ☐ |
| 63 | `src/StrideAssetStore.Core.Local/Validation/SchemaValidator.cs` | ☑ | ☐ | ☐ |
| 64 | `src/StrideAssetStore.Core.Local/Validation/ValidationReport.cs` | ☑ | ☐ | ☐ |
| 65 | `src/StrideAssetStore.Core/Catalog/AssetCatalog.cs` | ☑ | ☐ | ☐ |
| 66 | `src/StrideAssetStore.Core/Catalog/CatalogCache.cs` | ☑ | ☐ | ☐ |
| 67 | `src/StrideAssetStore.Core/Catalog/CatalogDefaults.cs` | ☑ | ☐ | ☐ |
| 68 | `src/StrideAssetStore.Core/Catalog/CatalogLoader.cs` | ☑ | ☐ | ☐ |
| 69 | `src/StrideAssetStore.Core/Catalog/CatalogSources.cs` | ☑ | ☐ | ☐ |
| 70 | `src/StrideAssetStore.Core/Catalog/StarsHistory.cs` | ☑ | ☐ | ☐ |
| 71 | `src/StrideAssetStore.Core/Catalog/StrideVersionMatcher.cs` | ☑ | ☐ | ☐ |
| 72 | `src/StrideAssetStore.Core/Models/AssetId.cs` | ☑ | ☐ | ☐ |
| 73 | `src/StrideAssetStore.Core/Models/AssetManifest.cs` | ☑ | ☐ | ☐ |
| 74 | `src/StrideAssetStore.Core/Models/Author.cs` | ☑ | ☐ | ☐ |
| 75 | `src/StrideAssetStore.Core/Models/IndexLock.cs` | ☑ | ☐ | ☐ |
| 76 | `src/StrideAssetStore.Core/Models/NugetPackage.cs` | ☑ | ☐ | ☐ |
| 77 | `src/StrideAssetStore.Core/Models/RegistryEntry.cs` | ☑ | ☐ | ☐ |
| 78 | `src/StrideAssetStore.Core/Releases/DesktopBuilds.cs` | ☑ | ☐ | ☐ |
| 79 | `src/StrideAssetStore.Core/Serialization/AssetStoreJson.cs` | ☑ | ☐ | ☐ |
| 80 | `src/StrideAssetStore.Core/StrideAssetStore.Core.csproj` | ☑ | ☐ | ☐ |
| 81 | `src/StrideAssetStore.Desktop/Components/App.razor` | ☑ | ☐ | ☐ |
| 82 | `src/StrideAssetStore.Desktop/Components/FileBrowser.razor` | ☑ | ☐ | ☐ |
| 83 | `src/StrideAssetStore.Desktop/Components/Pages/Install.razor` | ☑ | ☐ | ☐ |
| 84 | `src/StrideAssetStore.Desktop/Components/Pages/MyAssets.razor` | ☑ | ☐ | ☐ |
| 85 | `src/StrideAssetStore.Desktop/Components/Pages/MyProjects.razor` | ☑ | ☐ | ☐ |
| 86 | `src/StrideAssetStore.Desktop/Components/Pages/NewAsset.razor` | ☑ | ☐ | ☐ |
| 87 | `src/StrideAssetStore.Desktop/Components/Routes.razor` | ☑ | ☐ | ☐ |
| 88 | `src/StrideAssetStore.Desktop/Program.cs` | ☑ | ☐ | ☐ |
| 89 | `src/StrideAssetStore.Desktop/Properties/launchSettings.json` | ☑ | ☐ | ☐ |
| 90 | `src/StrideAssetStore.Desktop/Services/AssetScaffolder.cs` | ☑ | ☐ | ☐ |
| 91 | `src/StrideAssetStore.Desktop/Services/AuthorRepoService.cs` | ☑ | ☐ | ☐ |
| 92 | `src/StrideAssetStore.Desktop/Services/GhCliPublisher.cs` | ☑ | ☐ | ☐ |
| 93 | `src/StrideAssetStore.Desktop/Services/GlobalHotkeys.cs` | ☑ | ☐ | ☐ |
| 94 | `src/StrideAssetStore.Desktop/Services/ProjectStore.cs` | ☑ | ☐ | ☐ |
| 95 | `src/StrideAssetStore.Desktop/Services/ProtocolLauncher.cs` | ☑ | ☐ | ☐ |
| 96 | `src/StrideAssetStore.Desktop/StrideAssetStore.Desktop.csproj` | ☑ | ☐ | ☐ |
| 97 | `src/StrideAssetStore.Desktop/_Imports.razor` | ☑ | ☐ | ☐ |
| 98 | `src/StrideAssetStore.Desktop/wwwroot/data/categories.json` | ☑ | ☐ | ☐ |
| 99 | `src/StrideAssetStore.Desktop/wwwroot/data/licenses.json` | ☑ | ☐ | ☐ |
| 100 | `src/StrideAssetStore.UI/App.razor` | ☑ | ☐ | ☐ |
| 101 | `src/StrideAssetStore.UI/Components/AssetCard.razor` | ☑ | ☐ | ☐ |
| 102 | `src/StrideAssetStore.UI/Components/AssistedPrPanel.razor` | ☑ | ☐ | ☐ |
| 103 | `src/StrideAssetStore.UI/Components/CliPrPanel.razor` | ☑ | ☐ | ☐ |
| 104 | `src/StrideAssetStore.UI/Components/GitHubSignIn.razor` | ☑ | ☐ | ☐ |
| 105 | `src/StrideAssetStore.UI/Components/MethodPicker.razor` | ☑ | ☐ | ☐ |
| 106 | `src/StrideAssetStore.UI/Components/Modal.razor` | ☑ | ☐ | ☐ |
| 107 | `src/StrideAssetStore.UI/Components/PrResult.razor` | ☑ | ☐ | ☐ |
| 108 | `src/StrideAssetStore.UI/Components/PublishMethod.cs` | ☑ | ☐ | ☐ |
| 109 | `src/StrideAssetStore.UI/Layout/MainLayout.razor` | ☑ | ☐ | ☐ |
| 110 | `src/StrideAssetStore.UI/Pages/About.razor` | ☑ | ☐ | ☐ |
| 111 | `src/StrideAssetStore.UI/Pages/AssetDetail.razor` | ☑ | ☐ | ☐ |
| 112 | `src/StrideAssetStore.UI/Pages/Download.razor` | ☑ | ☐ | ☐ |
| 113 | `src/StrideAssetStore.UI/Pages/Home.razor` | ☑ | ☐ | ☐ |
| 114 | `src/StrideAssetStore.UI/Pages/ManifestGenerator.razor` | ☑ | ☐ | ☐ |
| 115 | `src/StrideAssetStore.UI/Pages/NotFound.razor` | ☑ | ☐ | ☐ |
| 116 | `src/StrideAssetStore.UI/Pages/Publish.razor` | ☑ | ☐ | ☐ |
| 117 | `src/StrideAssetStore.UI/Pages/Setup.razor` | ☑ | ☐ | ☐ |
| 118 | `src/StrideAssetStore.UI/ServiceCollectionExtensions.cs` | ☑ | ☐ | ☐ |
| 119 | `src/StrideAssetStore.UI/Services/AppEnvironment.cs` | ☑ | ☐ | ☐ |
| 120 | `src/StrideAssetStore.UI/Services/AppInfo.cs` | ☑ | ☐ | ☐ |
| 121 | `src/StrideAssetStore.UI/Services/AttentionState.cs` | ☑ | ☐ | ☐ |
| 122 | `src/StrideAssetStore.UI/Services/CatalogState.cs` | ☑ | ☐ | ☐ |
| 123 | `src/StrideAssetStore.UI/Services/CliPublishing.cs` | ☑ | ☐ | ☐ |
| 124 | `src/StrideAssetStore.UI/Services/GitHubAuth.cs` | ☑ | ☐ | ☐ |
| 125 | `src/StrideAssetStore.UI/Services/GitHubPublisher.cs` | ☑ | ☐ | ☐ |
| 126 | `src/StrideAssetStore.UI/Services/GitLinks.cs` | ☑ | ☐ | ☐ |
| 127 | `src/StrideAssetStore.UI/Services/LocalStorageCatalogCache.cs` | ☑ | ☐ | ☐ |
| 128 | `src/StrideAssetStore.UI/Services/MarkdownRenderer.cs` | ☑ | ☐ | ☐ |
| 129 | `src/StrideAssetStore.UI/Services/RegistryOptions.cs` | ☑ | ☐ | ☐ |
| 130 | `src/StrideAssetStore.UI/Services/UpdateService.cs` | ☑ | ☐ | ☐ |
| 131 | `src/StrideAssetStore.UI/StrideAssetStore.UI.csproj` | ☑ | ☐ | ☐ |
| 132 | `src/StrideAssetStore.UI/_Imports.razor` | ☑ | ☐ | ☐ |
| 133 | `src/StrideAssetStore.UI/wwwroot/app.css` | ☑ | ☐ | ☐ |
| 134 | `src/StrideAssetStore.UI/wwwroot/js/interop.js` | ☑ | ☐ | ☐ |
| 135 | `tests/StrideAssetStore.Core.Local.Tests/AssetInstallerTests.cs` | ☑ | ☐ | ☐ |
| 136 | `tests/StrideAssetStore.Core.Local.Tests/InstallerWorkspace.cs` | ☑ | ☐ | ☐ |
| 137 | `tests/StrideAssetStore.Core.Local.Tests/StrideAssetStore.Core.Local.Tests.csproj` | ☑ | ☐ | ☐ |
| 138 | `tests/StrideAssetStore.Core.Local.Tests/xunit.runner.json` | ☑ | ☐ | ☐ |
| 139 | `tests/StrideAssetStore.Core.Tests/AssetCatalogTests.cs` | ☑ | ☐ | ☐ |
| 140 | `tests/StrideAssetStore.Core.Tests/AssetValidatorTests.cs` | ☑ | ☐ | ☐ |
| 141 | `tests/StrideAssetStore.Core.Tests/CatalogLoaderTests.cs` | ☑ | ☐ | ☐ |
| 142 | `tests/StrideAssetStore.Core.Tests/CatalogTestData.cs` | ☑ | ☐ | ☐ |
| 143 | `tests/StrideAssetStore.Core.Tests/ContentHasherTests.cs` | ☑ | ☐ | ☐ |
| 144 | `tests/StrideAssetStore.Core.Tests/CsprojEditorTests.cs` | ☑ | ☐ | ☐ |
| 145 | `tests/StrideAssetStore.Core.Tests/CsprojInspectorTests.cs` | ☑ | ☐ | ☐ |
| 146 | `tests/StrideAssetStore.Core.Tests/DependencyResolverTests.cs` | ☑ | ☐ | ☐ |
| 147 | `tests/StrideAssetStore.Core.Tests/GitClientTests.cs` | ☑ | ☐ | ☐ |
| 148 | `tests/StrideAssetStore.Core.Tests/GitTagParsingTests.cs` | ☑ | ☐ | ☐ |
| 149 | `tests/StrideAssetStore.Core.Tests/IncrementalBuildTests.cs` | ☑ | ☐ | ☐ |
| 150 | `tests/StrideAssetStore.Core.Tests/IndexBuilderTests.cs` | ☑ | ☐ | ☐ |
| 151 | `tests/StrideAssetStore.Core.Tests/IndexLockSchemaTests.cs` | ☑ | ☐ | ☐ |
| 152 | `tests/StrideAssetStore.Core.Tests/ManifestSerializationTests.cs` | ☑ | ☐ | ☐ |
| 153 | `tests/StrideAssetStore.Core.Tests/SolutionInspectorTests.cs` | ☑ | ☐ | ☐ |
| 154 | `tests/StrideAssetStore.Core.Tests/StarsHistoryTests.cs` | ☑ | ☐ | ☐ |
| 155 | `tests/StrideAssetStore.Core.Tests/StrideAssetStore.Core.Tests.csproj` | ☑ | ☐ | ☐ |
| 156 | `tests/StrideAssetStore.Core.Tests/StrideVersionMatcherTests.cs` | ☑ | ☐ | ☐ |
| 157 | `tests/StrideAssetStore.Core.Tests/SyntheticWorkspace.cs` | ☑ | ☐ | ☐ |
| 158 | `tests/StrideAssetStore.Core.Tests/TestPaths.cs` | ☑ | ☐ | ☐ |
