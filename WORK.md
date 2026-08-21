# Chantier — état au 2026-08-21

**Tout est vert.** Reste à trancher : le tag `v1.0.0`.

## Vérifié de bout en bout

- `Install an asset and run a Stride game` : **Windows, Linux et macOS verts** — le CLI se packe et
  s'installe, un jeu Stride 4.4 se génère, l'asset s'installe, le jeu compile et **rend 30 frames**.
  Linux via Vulkan logiciel (lavapipe), macOS via MoltenVK.
- `Build & test the solution` : vert, 85 tests.
- `Publish the storefront to GitHub Pages` : vert, site en ligne avec le bon `base href`.
- Registre : index régénéré, 8 assets `ok`, tous en `4.4.0-beta5`, certifiés jusqu'en 1.2.0.

## Ce qu'il faut retenir de la session

1. **Stride 4.4 a renommé l'asset compiler** : `Stride.Core.Assets.CompilerApp` → `Stride.AssetCompiler`.
   Garder l'ancien à côté d'un moteur 4.4 compile la bibliothèque et casse tout jeu qui la référence.
2. **`StridePlatform` choisit l'API graphique.** Non déclarée, un projet passe pour du Windows et va
   chercher DXGI sur un Mac. `OpenGL` n'existe plus en 4.4 : `Direct3D11`, `Direct3D12`, `Vulkan`, `Null`.
3. Un `Game` nu n'a ni GameSettings ni GraphicsCompositor : la fenêtre s'ouvre noire et `Draw` n'est
   jamais atteint.
4. Le contexte `runner` est interdit dans un `env:` de niveau workflow, et un `name:` contenant un
   deux-points doit être quoté — sinon GitHub refuse de charger le fichier.

## Reste à faire

- [ ] Tag **v1.0.0** → publie les builds desktop + le CLI sur nuget.org (Trusted Publishing).
- [ ] Poster `docs/announce-discord.md` **après** avoir vérifié que le site charge.
- [ ] Renommer le dossier local `repos\AssetStore` (je ne peux pas le faire sous mes pieds).

## Règles de travail

- Pas de `git push` sans demande ; pas de tag `v*` automatique.
- Ne pas lancer d'exécutable sans accord.
- Vérifier le build avant de lancer quoi que ce soit.
- Tracer jusqu'au consommateur réel avant d'affirmer qu'un truc marche.
