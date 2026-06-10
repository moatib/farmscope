# FarmScope

Outil Windows léger pour **agréger les logs de tous les composants d'une ferme Parallels RAS**
(Connection Brokers, Secure Gateways, Enrollment Servers) dans une seule fenêtre, avec
filtres, colorisation par sévérité, recherche et export CSV.

> ⚠️ Projet **communautaire non officiel**, sans aucun lien avec Parallels / Alludo.
> « Parallels » et « RAS » sont des marques de leurs détenteurs respectifs.

## Fonctionnalités

- Découverte automatique des composants de la ferme via l'**API PowerShell RAS** (`RASAdmin`/`PSAdmin`).
- Lecture des logs en local ou sur les **partages admin C$** des serveurs distants.
- Vue agrégée triée par horodatage, **colorisation** Error/Warn, panneau de détail multi-lignes.
- Filtres par **sévérité, rôle, serveur et fichier** (ex. tous les `controller.log` de la ferme) + recherche plein texte.
- Parsing du format de log RAS (`[I …] dd-MM-yy HH:MM:SS - message`), détection d'encodage (UTF-8/UTF-16).
- **Thème sombre / clair** commutable (préférence mémorisée) et interface **FR / EN / DE** (langue Windows détectée par défaut).
- Tout est **configurable** sans recompiler via `config.json`.

## Téléchargement

Voir la page [Releases](../../releases). Deux variantes :

- **self-contained** : exe autonome, ne nécessite pas .NET installé (plus volumineux).
- **framework-dependent** : léger, nécessite le **.NET 9 Desktop Runtime** :
  `winget install Microsoft.DotNet.DesktopRuntime.9`

Vérifie l'intégrité avec le fichier `.sha256` fourni à côté de chaque archive.

## Utilisation

1. Lancer l'exe (sur un serveur RAS, ou un poste d'admin ayant accès aux partages C$).
2. Renseigner un Connection Broker (FQDN) + un compte admin RAS.
3. Les composants sont découverts et leurs logs agrégés automatiquement.

Le module PowerShell RAS doit être présent sur la machine qui exécute l'outil
(installé avec la console RAS).

## Configuration (`config.json`)

Éditable à côté de l'exe, sans recompiler : mapping rôle→fichiers, regex de parsing,
encodage, `ForceLocal` (tout lire en local pour un test mono-serveur), `ExtraLocalNames`.
Détails dans [RASLogAggregator/README.md](RASLogAggregator/README.md).

## Compiler depuis les sources

Prérequis : **.NET 9 SDK** (`winget install Microsoft.DotNet.SDK.9`).

```powershell
cd RASLogAggregator
dotnet run                      # lancer en dev
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Contribuer

Les PR sont bienvenues. La CI build chaque PR. Pour publier une release signée,
voir [RELEASING.md](RELEASING.md).

## Crédits

Développé par **Mathieu PREBIN** — [LinkedIn](https://www.linkedin.com/in/mathieu-prebin-2a62957a).

Conçu et développé avec l'assistance de **[Claude Fable 5](https://www.anthropic.com)** (Anthropic),
de l'architecture au design de l'interface.

## Licence

[MIT](LICENSE).
