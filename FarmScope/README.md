# RAS Log Aggregator

Outil .exe autonome (C# / WPF, .NET 8) pour **interroger une ferme Parallels RAS**,
découvrir ses composants via l'**API PowerShell RASAdmin**, puis **agréger les logs**
(`C:\ProgramData\Parallels\RASLogs\`) des Brokers, Gateways et Enrollment Servers
dans une fenêtre unique avec filtres, colorisation par sévérité, recherche, tail et export CSV.

## Fonctionnement

1. **Connexion** : tu saisis un Connection Broker (FQDN) + un compte admin RAS.
2. **Découverte** : l'outil lance `powershell.exe`, importe `RASAdmin` (ou `PSAdmin`),
   ouvre une session (`New-RASSession`) et énumère les composants
   (`Get-RASBroker` / `Get-RASGateway` / `Get-RASEnrollmentServer`).
3. **Lecture** : pour chaque composant, il lit les fichiers de log correspondants en
   UNC sur le partage admin `\\serveur\C$\ProgramData\Parallels\RASLogs\`
   (ouverture en `FileShare.ReadWrite`, donc lecture possible même fichier ouvert par le service).
4. **Agrégation** : tout est fusionné, trié par horodatage, et affiché dans un DataGrid filtrable.

## Prérequis

- **Windows** + **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`) pour compiler.
- Le **module PowerShell RAS** installé sur la machine qui exécute l'outil
  (présent avec la console RAS, ou via le package PowerShell RAS).
- Le compte qui lance l'outil doit avoir un **accès admin aux partages C$** des serveurs RAS
  (compte domaine admin local sur les cibles), ou bien coche « identifiants Windows différents ».

## Compilation

```powershell
cd RASLogAggregator

# Build/run rapide en dev
dotnet run

# Exe autonome single-file (ne nécessite pas .NET installé sur la cible)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# Résultat : bin\Release\net8.0-windows\win-x64\publish\RASLogAggregator.exe
#            + config.json (éditable à côté de l'exe)
```

Pour un exe plus léger qui suppose .NET 8 installé sur la cible :
`--self-contained false` (~quelques Mo au lieu de ~150 Mo).

## Points à vérifier / ajuster pour TON environnement

Tout est centralisé dans **`config.json`** (éditable sans recompiler) :

- **`TimestampRegex` / `TimestampFormats`** — le format exact des lignes de log RAS
  varie selon la version. Ouvre un `controller.log` réel, regarde le début d'une ligne,
  et ajuste la regex/les formats si les horodatages ne sont pas parsés
  (les entrées non parsées restent affichées, simplement non triées chronologiquement).
- **`RoleLogFiles`** — mapping rôle → fichiers (source : KB Parallels 125166).
  Ajoute/retire des fichiers selon ce qui t'intéresse.
- **`LogSubPath` / `AdminShare`** — si tes logs ou partages diffèrent.
- **`MaxLinesPerFile`** — nombre de dernières lignes lues par fichier (lecture par la fin
  pour ne pas tirer 200 Mo via SMB).

Côté découverte (`Services/RasDiscoveryService.cs`, script PowerShell intégré) :

- Si ta version utilise des **noms de cmdlets différents** (ex. `Get-RASPA` au lieu de
  `Get-RASBroker`, ou `Get-RASEnrollmentServerStatus`), les fallbacks sont déjà prévus,
  mais tu peux les compléter là.
- Si `New-RASSession` attend d'autres paramètres dans ton build, ajuste l'appel.

## Sécurité

- Le mot de passe RAS transite vers PowerShell via **variables d'environnement**
  (pas en ligne de commande, donc invisible dans la liste des process), puis est
  effacé. Le script temporaire est supprimé après usage.
- L'accès C$ utilise par défaut le **token Windows courant**. L'option « identifiants
  différents » monte les partages via `WNetAddConnection2` et les démonte après lecture.

## Pistes d'évolution

- Lecture des archives `.zip` de logs (rotation RAS) pour l'historique.
- Tail incrémental (offset par fichier) plutôt que relecture de la fin.
- Arbre Site → Serveur → Composant en panneau latéral.
- Récupération aussi des RDSH/Provider/Guest agents (déjà mappables dans `config.json`).
