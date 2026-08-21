# Chantier en cours — brief et checklist

Ce fichier est la mémoire du chantier demandé par Nicogo le 2026-08-21. **À relire à la fin de
chaque cycle de travail** pour vérifier que rien n'a été oublié ou fait à moitié.
Une case ne se coche que lorsque le point est terminé *et vérifié* (build + tests verts, ou
résultat CI observé) — pas quand le code est écrit.

---

## ⚠ Découverte majeure — à traiter en priorité

**Aucun asset du store n'est installable par un tiers.** Les assets déclarent
`Stride.Engine 4.4.0.2` et `Stride.Core.Assets.CompilerApp 4.4.0.2`, qui **n'existent pas sur
nuget.org**. Le public n'y trouve que 4.3.0.2507 (stable) et 4.4.0-beta1…5. La 4.4.0.2 vient des
paquets Stride buildés localement sur la machine de Nicogo.

Vérifié, pas supposé : restauration de `StrideGrassSystem.csproj` avec nuget.org comme seule
source et un dossier de paquets vierge →

```
error NU1102: Package Stride.Engine avec la version (>= 4.4.0.2) introuvable
              Version(s) 61 trouvée(s) dans nuget.org [version la plus proche : 4.4.0-brta4]
error NU1102: Package Stride.Core.Assets.CompilerApp avec la version (>= 4.4.0.2) introuvable
```

Conséquence : quelqu'un qui découvre le store, installe un asset et lance `dotnet build` se prend
un échec de restauration. Ça touche les 8 assets du catalogue.

Pistes (décision de Nicogo) :
- republier les assets contre une version Stride publique (4.3.0.2507 ou une 4.4.0-beta) ;
- ou attendre la sortie publique de 4.4 et l'assumer d'ici là dans l'UI ;
- ou publier un feed NuGet avec les paquets 4.4.0.2.

Mitigation déjà livrée : `strideassetstore add <asset> --stride <version>` retarge l'asset installé
vers la version Stride du jeu. Vérifié en réel (4.4.0.2 → 4.3.0.2507 dans le csproj cloné).

---

## Ce qui a été demandé

### 1. Rename complet en `StrideAssetStore`
Portée validée : **code + repo GitHub + URL Pages**.

- [x] Projets, assemblies, namespaces, solution, dossiers `src/` et `tests/`
- [x] Repo GitHub renommé (`Nicogo1705/StrideAssetStore`), remote local mis à jour
- [x] Package NuGet : `PackageId` = `StrideAssetStore`, commande = `strideassetstore`
- [x] `deploy-pages.yml` : `<base href="/StrideAssetStore/" />` (corrigé par le rename global)
- [x] URLs dans les README, le csproj du package, les workflows
- [ ] Dossier local `C:\Users\Nicogo\source\repos\AssetStore` → **à renommer par Nicogo** (c'est le
      répertoire de travail de la session, je ne peux pas le renommer sous mes propres pieds)

### 2. Projet `StrideAssetStore.Core.Local`
- [x] Création du projet + entrée dans la solution
- [x] Descendus dans `Core.Local` : `Git`, `Hashing`, `Indexing`, `Projects`, `Dependencies`,
      `Validation`, `FileCatalogSource`/`FileCatalogCache`, l'installeur (`DesktopInstaller` →
      `Install/AssetInstaller`), `Shell/DesktopShell`, `Releases/DesktopAppInstaller`
- [x] Restent dans `Core` : `Models`, `Serialization`, `Catalog`, `StarsHistory`, `Releases/DesktopBuilds`
- [x] `Desktop.Tests` → `Core.Local.Tests`
- [x] **Invariant garanti** : `UI` et `App` cassent le build si `Core.Local` apparaît dans leurs
      références résolues. Vérifié en ajoutant la référence et en constatant l'échec.

### 3. CLI consommateur — parité avec l'app locale
- [x] `search` — recherche dans le catalogue (facettes catégorie / Stride / certifié)
- [x] `add` — install source ou NuGet, dépendances, hash vérifié, `--version` / `--ref` / `--into`
      / `--stride`
- [x] `list` — assets du projet avec statut et ref, `--cached` pour le cache partagé
- [x] `update` — mise à jour de tout, ou bascule d'un asset via `--version` / `--ref`
- [x] `remove` — désinstallation, `--delete-clone`
- [x] `app install` / `update` / `status` / `start` / `open`
- [x] Non-interactif : `--yes`, `--offline`, codes de sortie, refus de prompter hors terminal,
      message clair si `git` manque
- [x] Vérifié de bout en bout contre le catalogue réel (clone, hash, référence, list, update,
      remove) et le package installé via `dotnet tool install`

### 4. Multi-plateforme (Windows / Linux / macOS)
- [x] `explorer.exe` codé en dur (3 sites) → `DesktopShell` (explorer / open / xdg-open)
- [x] `OpenBrowser` du desktop dédupliqué dans le même helper
- [x] `SelfUpdater` : la branche Unix (`/bin/sh`, `nohup`) était déjà correcte — vérifié
- [x] Séparateurs de chemin : déjà normalisés (`.sln`/`.slnx`, marqueur MSBuild du cache global) — vérifié
- [x] `DesktopAppInstaller` pose le bit exécutable sous Unix (le zip ne le porte pas)
- [ ] Reste à prouver par la CI que ça tourne vraiment sur les 3 OS (point 5)

### 5. Action GitHub : installer un asset et lancer le jeu
- [x] `.github/workflows/asset-e2e.yml` : sur Windows / Linux / macOS — packe et installe le tool,
      génère un jeu Stride code-only (community toolkit, sans Game Studio), installe l'asset via le
      CLI, compile, **lance le jeu** et exige une sentinelle `E2E-RENDERED` (30 frames rendues),
      Xvfb + Mesa logiciel sous Linux, log versé en artefact à l'échec
- [ ] **Jamais exécuté** — à lancer et à lire. Attendu : ça tombe sur le problème Stride 4.4.0.2
      ci-dessus, ce qui est exactement son rôle. La leg macOS est la plus incertaine.

### 6. Publication NuGet
- [x] Trusted Publishing configuré côté nuget.org (policy « StrideAssetStore release »)
- [x] Job `publish-cli` dans `release.yml` : OIDC (`id-token: write`, `NuGet/login` épinglée par
      SHA), `dotnet pack` + `dotnet nuget push --skip-duplicate`. Pas de secret.
- [x] `dotnet pack` vérifié en local, et le tool empaqueté s'installe et s'exécute
- [ ] **Jamais exécuté en CI** — se déclenchera au prochain tag `v*`
- ⚠ La policy nuget.org épingle le nom de fichier `release.yml` : le renommer casse la publication.

### 7. Documentation
- [x] `README.md` racine : projets, split Core/Core.Local, section CLI consommateur
- [x] `src/StrideAssetStore.Cli/README.md` — README du package NuGet (requis par le `pack`)
- [x] Page Download et section Architecture de About : le CLI y est présenté
- [x] Scan des noms et URLs périmés dans src/, tests/ et .github/ : seul le User-Agent du CLI traînait

### 8. Nettoyage
- [x] `OpenBrowser` dupliqué supprimé, `DesktopBuilds` remonté dans Core, doc de classe de
      l'installeur corrigée (elle se disait « desktop-only »), `OutputType Library` obsolète retiré
      du projet de test, URL du catalogue centralisée dans `CatalogDefaults`
- [x] Passe dead-code : aucun type ni membre public non consommé — rien à supprimer au-delà des doublons déjà retirés

---

## Corrections livrées avant le chantier (ne pas défaire)

- `e3a8d96` — `_framework/blazor.web.js` n'était jamais publié (target du SDK conditionnée à
  `OutputType == 'Exe'`, l'app est un `WinExe`). **v1.3.7 → v1.5.2 étaient non-interactives.**
  Le smoke test de release vérifie désormais le script.

## Historique des commits du chantier

- `04c9e22` rename StrideAssetStore · `e8bdba7` split Core.Local · `3be56c0` CLI consommateur

## Reste à trancher avec Nicogo

- Publier une **v1.5.3** (fix `blazor.web.js` + tout ce chantier) — demandé deux fois, jamais
  confirmé, et règle établie : pas de tag `v*` sans demande explicite.
- Lancer le build corrigé en local pour vérifier de visu que l'UI redevient interactive.
- Que faire du problème Stride 4.4.0.2 (voir en haut).
- Rien n'est poussé : tous les commits sont locaux.

## Règles de travail à respecter

- Pas de `git push` sans demande explicite ; pas de tag `v*` automatique.
- Ne pas lancer d'exécutable sans accord (ça vole le port 5111 et casse ses tests de mise à jour).
- Vérifier le build **avant** de lancer quoi que ce soit, sinon on teste un binaire périmé.
- Tracer jusqu'au consommateur réel avant d'affirmer qu'un truc marche.
