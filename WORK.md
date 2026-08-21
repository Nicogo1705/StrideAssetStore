# Chantier en cours — brief et checklist

Ce fichier est la mémoire du chantier demandé par Nicogo le 2026-08-21. **À relire à la fin de
chaque cycle de travail** pour vérifier que rien n'a été oublié ou fait à moitié.
Une case ne se coche que lorsque le point est terminé *et vérifié* (build + tests verts, ou
résultat CI observé) — pas quand le code est écrit.

---

## Ce qui a été demandé

### 1. Rename complet en `StrideAssetStore`
Le projet ne disait pas « Stride » dans son propre nom. Portée validée : **code + repo GitHub + URL
Pages**.

- [x] Projets, assemblies, namespaces, solution, dossiers `src/` et `tests/`
- [x] Repo GitHub renommé (`Nicogo1705/StrideAssetStore`), remote local mis à jour
- [x] Package NuGet : `PackageId` = `StrideAssetStore`, commande = `strideassetstore`
- [ ] `deploy-pages.yml` : le `<base href>` est codé en dur sur `/AssetStore/` → **le site est cassé
      tant que ce n'est pas corrigé**
- [ ] Toutes les URLs restantes (README, page Download, `AppInfo.Repo`, badges)
- [ ] Dossier local `C:\Users\Nicogo\source\repos\AssetStore` → à renommer par Nicogo (c'est le
      répertoire de travail de la session, je ne peux pas le renommer sous mes propres pieds)

### 2. Projet `StrideAssetStore.Core.Local`
« Le serveur hébergé sur les pages GitHub n'a pas besoin de savoir gérer des repos. »

- [x] Création du projet + entrée dans la solution
- [x] Descendus dans `Core.Local` : `Git`, `Hashing`, `Indexing`, `Projects`, `Dependencies`,
      `Validation`, `FileCatalogSource`/`FileCatalogCache`, et l'installeur
      (`DesktopInstaller` → `Install/AssetInstaller`)
- [x] Restent dans `Core` (tout ce dont le WASM a besoin) : `Models`, `Serialization`, `Catalog`,
      `StarsHistory` (pur, sert au tri showcase)
- [x] `Desktop.Tests` → `Core.Local.Tests` (ces tests ne testaient que l'installeur)
- [ ] **Invariant à garantir** : `Core` ne doit contenir aucun accès disque ni git. Ajouter un test
      ou une règle qui casse le build si `Core` ou `UI` référencent `Core.Local`.

### 3. CLI consommateur — parité complète avec l'app locale
« Il faut que le CLI puisse gérer les assets installés localement, avec leurs versions, les mettre à
jour, etc. Tout comme via l'interface de l'app locale. »

- [ ] `search <query>` — sans lui, `add` oblige à connaître l'id exact
- [ ] `add <id>` — install source (clone + `ProjectReference`) ou NuGet (`PackageReference`),
      dépendances comprises ; découverte auto du `.sln`/`.csproj` depuis le répertoire courant,
      `--project` pour lever l'ambiguïté
- [ ] `list` — assets référencés par le projet courant **et** contenu du cache partagé, avec statut
      (à jour / obsolète / cassé / manquant) et version
- [ ] `update [<id>] [--all]` — mise à jour vers la ref suivie
- [ ] `remove <id>` — désinstallation avec nettoyage du `.sln`
- [ ] Choix de version / ref (équivalent du `SwitchRef` de l'app)
- [ ] `app install` / `app update` — installer et mettre à jour **l'app locale elle-même** depuis le
      CLI (idée de Nicogo, à faire)
- [ ] Ergonomie non interactive : `--yes`, codes de sortie, message clair si `git` est absent

### 4. Multi-plateforme sans souci (Windows / Linux / macOS)
Relire le code pour que tout fonctionne vraiment sur les trois.

- [ ] `explorer.exe` codé en dur (3 endroits : `MyAssets.razor` ×2, `MyProjects.razor`) →
      `open` sur macOS, `xdg-open` sur Linux
- [ ] `SelfUpdater` : branche `cmd.exe` — vérifier l'équivalent Unix
- [ ] Séparateurs de chemin `\` en dur, notamment le marqueur MSBuild du cache global dans
      `AssetInstaller`
- [ ] `SpecialFolder.ApplicationData` : vérifier le comportement réel sur Linux/macOS
- [ ] Vérifier que les chemins écrits dans les `.csproj` restent portables entre OS

### 5. Action GitHub : télécharger un asset et lancer le jeu
Décision de Nicogo : **tenter le run réel sur les trois OS**, quitte à avoir des jobs rouges.
Corrections apportées par Nicogo, à ne pas réoublier : Game Studio n'est pas nécessaire pour builder
un jeu, et le runtime Stride comme l'asset compiler sont multi-plateformes. La seule vraie inconnue
est le GPU sur les runners (Mesa/llvmpipe probablement nécessaire sous Linux).

- [ ] Workflow qui, sur Windows / Linux / macOS : installe un asset via le CLI dans un projet Stride
      jetable, compile, puis **lance le jeu** avec timeout et vérifie qu'il atteint la première frame

### 6. Publication NuGet
- [x] Trusted Publishing configuré côté nuget.org par Nicogo (policy « StrideAssetStore release »,
      repo `Nicogo1705/StrideAssetStore`, workflow `release.yml`, glob `StrideAssetStore*`)
- [ ] Job dans `release.yml` : `permissions: id-token: write`, action `NuGet/login`, `dotnet pack`
      + `dotnet nuget push` (**pas** de secret `NUGET_API_KEY`, c'est OIDC)

### 7. Documentation
- [ ] Mettre à jour tous les README et infos
- [ ] Note sur `dotnet tool install`
- [ ] `README.md` dédié au package NuGet (déjà référencé par le csproj du CLI — **le fichier
      n'existe pas encore, le pack échouera tant qu'il manque**)
- [ ] Nettoyer ce qui est périmé dans les README

### 8. Nettoyage
- [ ] Virer le dead code
- [ ] Nettoyer / optimiser au passage

---

## Corrections livrées avant le chantier (ne pas défaire)

- `e3a8d96` — `_framework/blazor.web.js` n'était jamais publié (target du SDK conditionnée à
  `OutputType == 'Exe'`, l'app est un `WinExe`). **Toutes les versions v1.3.7 → v1.5.2 étaient
  non-interactives.** Le smoke test de release vérifie désormais le script.
- `04c9e22` — rename `StrideAssetStore`.

## Reste à trancher avec Nicogo

- Publier une **v1.5.3** avec le fix `blazor.web.js` (demandé deux fois, jamais confirmé — et
  règle établie : pas de tag `v*` sans demande explicite).
- Lancer le build corrigé en local pour vérifier de visu que l'UI redevient interactive.

## Règles de travail à respecter

- Pas de `git push` sans demande explicite ; pas de tag `v*` automatique.
- Ne pas lancer d'exécutable sans accord (ça vole le port 5111 et casse ses tests de mise à jour).
- Vérifier le build **avant** de lancer quoi que ce soit, sinon on teste un binaire périmé.
- Tracer jusqu'au consommateur réel avant d'affirmer qu'un truc marche.
