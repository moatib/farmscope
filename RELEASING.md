# Publier une release

## Couper une version

```bash
git tag v1.0.0
git push --tags
```

Le workflow `.github/workflows/release.yml` se déclenche, build les deux variantes
(self-contained + framework-dependent), les signe (si configuré), calcule les SHA256
et crée la Release GitHub avec les fichiers attachés.

La version du binaire est dérivée du tag (`v1.0.0` → `1.0.0`).

## Activer la signature de code

Le workflow build et publie **sans signature** par défaut (les binaires fonctionnent,
mais SmartScreen avertira les utilisateurs tant que la réputation n'est pas établie).
Deux façons d'ajouter la signature :

### Option A — Microsoft Trusted Signing (~10 $/mois, cloud, sans token)

1. Créer un compte Trusted Signing dans Azure + un *certificate profile*.
2. Créer un service principal Azure ayant le rôle *Trusted Signing Certificate Profile Signer*.
3. Ajouter ces secrets au dépôt (Settings → Secrets and variables → Actions) :

   | Secret | Valeur |
   |---|---|
   | `AZURE_TENANT_ID` | ID du tenant Azure |
   | `AZURE_CLIENT_ID` | App ID du service principal |
   | `AZURE_CLIENT_SECRET` | Secret du service principal |
   | `TRUSTED_SIGNING_ENDPOINT` | ex. `https://eus.codesigning.azure.net` |
   | `TRUSTED_SIGNING_ACCOUNT` | nom du compte Trusted Signing |
   | `TRUSTED_SIGNING_PROFILE` | nom du certificate profile |

Dès que `AZURE_CLIENT_ID` est présent, l'étape de signature s'active automatiquement.
Préférer à terme l'authentification **OIDC** (`azure/login`) plutôt qu'un client secret.

### Option B — SignPath Foundation (gratuit pour l'open source)

[SignPath](https://about.signpath.io/product/open-source) fournit gratuitement de la
signature de code aux projets OSS éligibles. Après onboarding du projet :

- Remplacer l'étape « Sign with Trusted Signing » par
  l'action `signpath/github-action-submit-signing-request`.
- Secrets requis : `SIGNPATH_API_TOKEN` (+ organization-id / project-slug /
  signing-policy-slug en paramètres de l'action).

C'est l'option recommandée pour un outil communautaire gratuit.

## Rappels

- **Horodatage** : toujours signer avec horodatage (déjà configuré) — la signature
  reste valide même après expiration du certificat.
- **Réputation SmartScreen** : se construit avec le volume de téléchargements
  (quel que soit OV/EV). Les premières releases déclencheront un avertissement ;
  publier via GitHub Releases aide à accumuler cette réputation.
- **Ne jamais committer** de clé/certificat (`.pfx`, `.snk`) — déjà couvert par `.gitignore`.
