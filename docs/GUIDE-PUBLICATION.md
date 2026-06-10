# Guide de publication — FarmScope sur GitHub (open source + signature)

Guide pas à pas, de zéro jusqu'à la première release signée.

---

## Étape 1 — Vérifier le nom (10 min)

Avant de figer « FarmScope » :

1. Chercher `FarmScope` sur GitHub, Google et le registre EUIPO (https://euipo.europa.eu/eSearch/)
   pour vérifier qu'aucun logiciel/marque homonyme gênant n'existe.
2. Si conflit, choisir une variante (FarmLens, LogHarvest…) — le renommage se fait dans
   5 chaînes : `AssemblyName`/`Product` du `.csproj`, le `Title` des 2 fenêtres XAML,
   le bandeau de `MainWindow.xaml`, et le README.

## Étape 2 — Créer le dépôt GitHub (10 min)

1. Sur https://github.com → **New repository** :
   - Nom : `farmscope`
   - Visibilité : **Public**
   - Ne PAS initialiser avec README/licence (on pousse les nôtres).
2. En local, depuis le dossier extrait du zip `RASLogAggregator-repo` :

```powershell
cd C:\dev\farmscope          # le dossier contenant README.md, LICENSE, .github, etc.
git init -b main
git add .
git commit -m "Initial commit: FarmScope 1.0"
git remote add origin https://github.com/<ton-user>/farmscope.git
git push -u origin main
```

3. Vérifier sur GitHub que l'onglet **Actions** montre le workflow `build` qui tourne
   (déclenché par le push) et qu'il passe au vert. C'est la CI : chaque push/PR sera compilé.

## Étape 3 — Habiller le dépôt (15 min)

1. **Description + topics** (roue dentée en haut à droite de la page du repo) :
   description courte EN (« Unofficial log aggregator for Parallels RAS farms »),
   topics : `parallels-ras`, `logging`, `wpf`, `sysadmin`, `windows`.
2. **Capture d'écran** : prendre une capture de l'app avec des logs chargés,
   la mettre dans `docs/screenshot.png`, et l'ajouter en haut du README :
   `![FarmScope](docs/screenshot.png)`.
3. Relire le README : c'est la vitrine. Vérifier la mention « projet non officiel ».

## Étape 4 — Première release (non signée) (5 min)

But : valider le pipeline de bout en bout avant de brancher la signature.

```powershell
git tag v1.0.0
git push --tags
```

Le workflow `release` se déclenche : build des 2 variantes (self-contained +
framework-dependent), zips, SHA256, et création de la **Release** GitHub avec les
fichiers attachés. Un warning « binaires non signés » est normal à ce stade.

Télécharger le zip depuis la release sur une machine de test et vérifier qu'il tourne.

## Étape 5 — Demander la signature gratuite (SignPath Foundation)

SignPath Foundation signe gratuitement les projets open source éligibles.
C'est la voie recommandée pour un outil communautaire.

1. Critères usuels : dépôt public avec licence OSI (MIT ✔), builds reproductibles
   via CI (✔), et un mainteneur identifiable.
2. Candidater : https://signpath.org/apply (formulaire : lien du repo, description,
   licence, lien du workflow CI).
3. Délai de réponse : généralement quelques jours à quelques semaines.
4. Une fois accepté, SignPath fournit : une organisation, un projet, une *signing policy*
   et un token API.

### Brancher SignPath dans le workflow

1. Ajouter le secret `SIGNPATH_API_TOKEN` dans le repo
   (Settings → Secrets and variables → Actions → New repository secret).
2. Dans `.github/workflows/release.yml`, remplacer l'étape « Sign with Trusted Signing »
   par l'action SignPath (adapter org/projet/policy fournis à l'onboarding) :

```yaml
      - name: Upload unsigned artifact
        id: unsigned
        uses: actions/upload-artifact@v4
        with:
          name: unsigned-exe
          path: publish/**/*.exe

      - name: Sign with SignPath
        uses: signpath/github-action-submit-signing-request@v1
        with:
          api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
          organization-id: '<organization-id>'
          project-slug: 'farmscope'
          signing-policy-slug: 'release-signing'
          github-artifact-id: ${{ steps.unsigned.outputs.artifact-id }}
          wait-for-completion: true
          output-artifact-directory: publish-signed
```

3. Adapter l'étape de packaging pour zipper depuis `publish-signed`.

> Alternative payante sans candidature : **Microsoft Trusted Signing** (~10 $/mois),
> déjà câblé dans le workflow — il suffit d'ajouter les 6 secrets listés dans
> `RELEASING.md` et l'étape s'active seule.

## Étape 6 — Release signée + vérification (10 min)

1. Couper une nouvelle version :
   ```powershell
   git tag v1.0.1
   git push --tags
   ```
2. Télécharger l'exe de la release, puis vérifier la signature :
   ```powershell
   Get-AuthenticodeSignature .\FarmScope.exe | Format-List Status, SignerCertificate
   # Status attendu : Valid
   ```
   (ou clic droit → Propriétés → onglet **Signatures numériques**)
3. Vérifier le hash annoncé :
   ```powershell
   Get-FileHash .\FarmScope-1.0.1-win-x64-selfcontained.zip -Algorithm SHA256
   ```

## Étape 7 — Réduire les frictions SmartScreen/AV (en continu)

- La réputation SmartScreen se construit avec le **volume de téléchargements propres** ;
  les premières releases peuvent encore déclencher un avertissement même signées —
  c'est attendu, ça s'estompe.
- Soumettre le binaire signé à Microsoft pour analyse (faux positifs Defender) :
  https://www.microsoft.com/wdsi/filesubmission
- En cas de faux positif sur d'autres AV, utiliser leurs portails de soumission.
- Toujours publier les `.sha256` à côté des zips (déjà automatisé).

## Étape 8 — Annoncer à la communauté

- Forum Parallels RAS (rubrique communauté), Reddit r/sysadmin / r/Parallels,
  LinkedIn, et les Discord/Slack d'admins virtualisation.
- Dans l'annonce : capture d'écran, 3 fonctionnalités clés, lien Releases,
  rappel « non officiel », et appel à contributions/issues.
- Activer **GitHub Issues** et ajouter un template de bug (logs RAS étant variés,
  demander : version RAS, fichier/format de ligne, capture).

## Rappels de maintenance

- Chaque release = un tag `vX.Y.Z` poussé ; tout le reste est automatique.
- Ne jamais committer de clé/certificat (le `.gitignore` couvre `.pfx`/`.snk`).
- Mettre à jour `CHANGELOG` dans la description de release (générée automatiquement
  par `generate_release_notes`, à compléter à la main si besoin).
