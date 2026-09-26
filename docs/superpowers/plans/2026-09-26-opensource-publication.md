# Publication Open Source — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le dépôt PeekDows publiable en public sur GitHub en corrigeant les 10 actions de `2026-09-26-OPENSOURCE-AUDIT.md` (licence MIT, purge de l'e-mail auteur de l'historique, allègement des docs internes, audit de la surface GitHub, scans certifiants).

**Architecture:** Travail en 4 phases strictement ordonnées : (1) sauvegarde sur le distant privé (branches `backup/*`) + gates de référence, (2) ajouts de contenu par commits normaux (LICENSE, docs, README, gardes), (3) une seule réécriture d'historique dans un clone jetable niché sous `./.tmp-rewrite/` avec `git filter-repo` combinant mailmap (métadonnées auteur/committer, les messages de commit sont intacts) et suppression des docs internes de toute l'historique, puis force-push sur dépôt encore privé, (4) durcissement GitHub + checklist go/no-go. La bascule en public est EXCLUE de ce plan (réservée au propriétaire). Aucune tâche de code applicatif : TDD ne s'applique pas, chaque step se vérifie par une commande exacte et sa sortie attendue.

**Tech Stack:** git + git-filter-repo, PowerShell 5.1, `dotnet build/test` (.NET 10 SDK), gitleaks, `gh` CLI, réglages web GitHub.

**Contrainte de confinement (absolue) :** toutes les opérations fichier restent sous `C:\Portfolio Prog\PeekDows`. INTERDIT : `$env:TEMP`, tout dossier voisin (`C:\Portfolio Prog\<autre-chose>`), tout chemin hors repo. Les seuls effets hors repo autorisés sont réseau : push vers le distant GitHub, `winget`/`pip`/`gh auth` (outillage standard).

---

## Décisions figées (issues de l'audit, tranchées avant ce plan)

- Licence : **MIT**, copyright `2026 Abdoullah0055`.
- E-mail auteur : **réécriture** vers `Abdoullah0055@users.noreply.github.com` sur tout l'historique.
- Docs : **allègement** — restent publics `README.md`, `docs/installer.md`, `docs/MANUAL_TEST_CHECKLIST.md`, ce plan ; sortent de l'historique `PRD.md`, `AGENTS.md`, `docs/superpowers/specs/`, les 3 anciens plans, `docs/audits/`, `docs/mockups/`.
- Sauvegarde : branches `backup/pre-opensource-2026-09-26-*` poussées sur le distant privé en Task 1, supprimées en Task 15 juste avant la bascule. Rapport d'audit : conservé local uniquement, non versionné, gitignoré en Task 1.
- Branche par défaut : on garde **`master`** (le distant pointe déjà `Peekdows/HEAD -> Peekdows/master`).
- Trailers Codebuff : conservés tels quels (pas de réécriture des messages).
- Bascule en public : **réservée au propriétaire, hors plan**.

## File Structure

| Fichier | Action | Responsabilité |
|---|---|---|
| `LICENSE` (racine) | Create (Task 4) | Texte MIT complet, rend la publication légalement open source |
| `CONTRIBUTING.md` (racine) | Create (Task 5) | Prérequis, workflow branches sur `master`, commandes build/test |
| `SECURITY.md` (racine) | Create (Task 5) | Canal de signalement privé via GitHub, versions supportées |
| `CODE_OF_CONDUCT.md` (racine) | Create (Task 5) | Règles de conduite compactes (base Contributor Covenant) |
| `THIRD-PARTY-NOTICES.md` (racine) | Create (Task 10) | Les 5 packages NuGet + licences + termes WebView2 |
| `README.md:105-107` | Modify (Task 6) | Remplace `All rights reserved` par § License MIT + note WebView2 |
| `.gitignore` (fin de fichier) | Modify (Task 1) | Ignore le rapport d'audit local (données privées, jamais committé) |
| `.gitattributes` (racine) | Create (Task 7) | Garde-fous binaires (`*.exe`, `*.msi`, `*.ico`, `*.png`…) |
| `.github/dependabot.yml` | Create (Task 7) | Mises à jour NuGet hebdomadaires (Dependabot) |
| `docs/superpowers/plans/2026-09-26-opensource-publication.md` | Create puis commit (Task 3) | Ce plan, conservé public (aucune donnée personnelle dedans) |
| Fichiers listés en § Décisions | Delete de toute l'historique via filter-repo (Task 12) | Purge docs internes |
| `.tmp-rewrite/`, `.tmp-mailmap` | Éphémères (Tasks 12-14, untracked, jamais `git add`) | Clone jetable + mailmap, supprimés en Task 14 |

**Note vie privée (lire avant d'exécuter) :** ce plan ne contient volontairement aucune adresse e-mail personnelle en clair. L'ancienne adresse est toujours manipulée via variables (`$oldMail`) ou via le motif générique `@gmail.com`. Ne jamais copier-coller l'adresse en clair dans un fichier versionné. Le fichier `.tmp-mailmap` est untracked et supprimé en Task 14 — ne jamais l'ajouter à git.

---

### Task 1: Sauvegarde sur distant privé + gitignore du rapport local

**Files:**
- Modify: `.gitignore` (fin de fichier)
- Aucune écriture hors repo (les push réseau vers le distant privé sont autorisés).

- [ ] **Step 1: Pousser les branches de sauvegarde sur le distant privé**

```powershell
git push Peekdows master:backup/pre-opensource-2026-09-26-master
git push Peekdows fix/directional-focus-after-virtual-desktop-switch:backup/pre-opensource-2026-09-26-fix
```

Run: depuis `C:\Portfolio Prog\PeekDows` (auth via le credential manager existant).
Expected: deux lignes `* [new branch] ... -> backup/pre-opensource-2026-09-26-*`. Si échec d'authentification : STOP, retourner BLOCKED (le propriétaire devra authentifier git avant de continuer).

- [ ] **Step 2: Vérifier les sauvegardes côté distant**

```powershell
git ls-remote Peekdows "backup/*"
git rev-list --all --count
```

Expected: `ls-remote` affiche exactement les 2 refs `backup/pre-opensource-2026-09-26-master` et `backup/pre-opensource-2026-09-26-fix` ; le compte affiche `62`.

- [ ] **Step 3: Gitignorer le rapport d'audit local puis committer**

Le fichier `2026-09-26-OPENSOURCE-AUDIT.md` est untracked et contient des données privées : il doit rester local et ne jamais être committé par accident. Ajouter à la toute fin de `.gitignore`, après la ligne `!src/PeekDows.Core/Win32/**` :

Ancienne fin de fichier (lignes 485-487) :

```text
# Keep PeekDows Win32 interop source files tracked
!src/PeekDows.Core/Win32/
!src/PeekDows.Core/Win32/**
```

Nouvelle fin de fichier :

```text
# Keep PeekDows Win32 interop source files tracked
!src/PeekDows.Core/Win32/
!src/PeekDows.Core/Win32/**

# Local pre-publication audit (private data, never commit)
2026-09-26-OPENSOURCE-AUDIT.md
```

Puis :

```bash
git add .gitignore
git commit -m "chore: ignore local pre-publication audit report"
```

- [ ] **Step 4: Vérifier l'état du repo**

```powershell
git status --short
```

Expected: une seule ligne `?? docs/superpowers/plans/` (ce plan, pas encore committé — c'est la Task 3). Le rapport d'audit n'apparaît plus.

---

### Task 2: Vérifier la visibilité actuelle du dépôt distant

**Files:** aucun (lecture seule, interface web).

- [ ] **Step 1: Ouvrir les réglages du dépôt**

Aller à `https://github.com/Abdoullah0055/PeekDows/settings` et lire la section « Danger Zone » (visibilité actuelle : Private ou Public). Noter le résultat : il conditionne la suite.

- [ ] **Step 2: Appliquer la règle de décision**

  - Si **Private** : continuer le plan normalement. Le force-push de la Task 14 reste sans conséquence publique.
  - Si **Public** : l'ancienne adresse est déjà exposée ; continuer quand même (la réécriture limite l'exposition future et nettoie les clones à venir), mais noter dans la checklist finale que l'exposition passée ne peut pas être annulée.

- [ ] **Step 3: Ne rien cliquer d'autre**

Ne pas changer la visibilité. La bascule en public est réservée au propriétaire et hors plan.

---

### Task 3: Gate build + test de référence, puis committer ce plan

**Files:**
- Commit (existant, untracked): `docs/superpowers/plans/2026-09-26-opensource-publication.md`

- [ ] **Step 1: Lancer le build de référence**

```powershell
dotnet build PeekDows.slnx
```

Run: depuis `C:\Portfolio Prog\PeekDows`.
Expected: `0 Error(s)`, `Build succeeded.`

- [ ] **Step 2: Lancer la suite de tests de référence**

```powershell
dotnet test PeekDows.slnx
```

Run: depuis `C:\Portfolio Prog\PeekDows`.
Expected: `Passed!` (zéro échec). Si un test échoue ici, STOP : corriger d'abord sur `master` avant de continuer le plan, sinon la vérification post-réécriture (Task 13) sera inexploitable.

- [ ] **Step 3: Committer ce plan (il ne contient aucune donnée personnelle)**

```bash
git add docs/superpowers/plans/2026-09-26-opensource-publication.md
git commit -m "docs: add open-source publication plan"
git status --short
```

Expected: `git status --short` vide (repo propre : le rapport d'audit est gitignoré depuis la Task 1).

---

### Task 4: Créer le fichier LICENSE (MIT)

**Files:**
- Create: `LICENSE`
- Test: lecture du fichier + gate build (Task 3 inchangé, aucun code touché)

- [ ] **Step 1: Créer `LICENSE` avec le contenu exact ci-dessous**

```text
MIT License

Copyright (c) 2026 Abdoullah0055

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Vérifier le fichier**

```powershell
Get-Content LICENSE -TotalCount 3
```

Expected:
```text
MIT License

Copyright (c) 2026 Abdoullah0055
```

- [ ] **Step 3: Commit**

```bash
git add LICENSE
git commit -m "docs: add MIT LICENSE"
```

---

### Task 5: Créer CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md

**Files:**
- Create: `CONTRIBUTING.md`
- Create: `SECURITY.md`
- Create: `CODE_OF_CONDUCT.md`

- [ ] **Step 1: Créer `CONTRIBUTING.md` avec le contenu exact ci-dessous**

```markdown
# Contributing to PeekDows

Thanks for your interest in contributing.

## Prerequisites

- Windows 10/11
- .NET 10.0 SDK (`dotnet --version` doit afficher une version 10.x)

## Workflow

1. Forkez le dépôt et créez une branche depuis `master` (`git checkout -b feat/ma-fonctionnalite`).
2. La branche par défaut des PR est `master`.
3. Vérifiez que le build et les tests passent avant chaque commit.

## Validation gate

```sh
dotnet build PeekDows.slnx
dotnet test PeekDows.slnx
```

Le build doit afficher `0 Error(s)` et les tests `Passed!`.

## Conventions

- Commits conventionnels : `feat:`, `fix:`, `test:`, `docs:`, `perf:`, `refactor:`.
- Un test xUnit par classe testée, dans `tests/PeekDows.Tests/`.
- Pas de secrets, chemins locaux (`C:\Users\...`) ou données personnelles dans le code, les tests ou les messages de commit.
```

- [ ] **Step 2: Créer `SECURITY.md` avec le contenu exact ci-dessous**

```markdown
# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 0.1.x   | :white_check_mark: |

## Reporting a Vulnerability

N'utilisez pas les issues publiques pour signaler une faille de sécurité.
Utilisez le signalement privé de vulnérabilité GitHub
(onglet **Security** du dépôt → **Report a vulnerability**).
Décrivez la faille, son impact et, si possible, les étapes de reproduction.

Nous accuserons réception sous 7 jours et viserons un correctif
sous 30 jours pour les failles confirmées.
```

- [ ] **Step 3: Créer `CODE_OF_CONDUCT.md` avec le contenu exact ci-dessous**

```markdown
# Code of Conduct

## Our Pledge

Nous nous engageons à faire de ce projet un espace accueillant pour toutes
et tous, quels que soient l'âge, le genre, l'origine, le handicap visible
ou invisible, ou le niveau d'expérience.

## Our Standards

Comportements attendus : langage respectueux, critique constructive,
empathie envers les autres contributeurs.
Comportements inacceptables : insultes, harcèlement, propos discriminatoires,
publication d'informations privées d'autrui sans consentement.

## Enforcement

Tout comportement inacceptable peut être signalé aux mainteneurs via un
message privé sur GitHub. Les mainteneurs examineront chaque signalement
et pourront avertir, exclure temporairement ou bannir un contributeur.
```

- [ ] **Step 4: Vérifier les trois fichiers**

```powershell
Get-ChildItem LICENSE, CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md
```

Expected: les 4 fichiers listés, sans erreur.

- [ ] **Step 5: Commit**

```bash
git add CONTRIBUTING.md SECURITY.md CODE_OF_CONDUCT.md
git commit -m "docs: add contributing, security and conduct guides"
```

---

### Task 6: Corriger la section License du README

**Files:**
- Modify: `README.md:105-107`
- Test: lecture de la section

- [ ] **Step 1: Remplacer la fin du README**

Ancien texte exact (`README.md:105-107`) :

```markdown
## License

All rights reserved.
```

Nouveau texte exact :

```markdown
## License

MIT — see [LICENSE](LICENSE).

This project depends on the Microsoft Edge WebView2 runtime for its
settings window. Building from source restores the `Microsoft.Web.WebView2`
NuGet package, which is distributed under Microsoft's own WebView2 terms
(see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md)).
```

- [ ] **Step 2: Vérifier la section**

```powershell
Get-Content README.md -Tail 10
```

Expected: les 10 dernières lignes affichent le nouveau § License ci-dessus, et plus aucune occurrence de `All rights reserved` :

```powershell
Select-String -Path README.md -Pattern "All rights reserved"
```

Expected: aucune sortie.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: switch README license section to MIT with WebView2 note"
```

---

### Task 7: Créer .gitattributes et Dependabot NuGet

**Files:**
- Create: `.gitattributes`
- Create: `.github/dependabot.yml`

- [ ] **Step 1: Créer `.gitattributes` avec le contenu exact ci-dessous**

```text
*.exe binary
*.msi binary
*.msix binary
*.msp binary
*.ico binary
*.png binary
*.pdb binary
```

- [ ] **Step 2: Créer `.github/dependabot.yml` avec le contenu exact ci-dessous**

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 5
```

- [ ] **Step 3: Vérifier**

```powershell
Get-Content .gitattributes; Get-Content .github/dependabot.yml
```

Expected: les deux contenus affichés tels quels.

- [ ] **Step 4: Commit**

```bash
git add .gitattributes .github/dependabot.yml
git commit -m "chore: add gitattributes binary guards and Dependabot for NuGet"
```

---

### Task 8: Installer l'outillage et lancer le scan gitleaks pré-réécriture

**Files:** aucun (outillage + preuve ; `winget`/`pip` sont des installs système standard, autorisés).

- [ ] **Step 1: Installer git-filter-repo**

```powershell
pip install git-filter-repo
git filter-repo --version
```

Expected: un numéro de version (ex. `2.45.0`). Si `pip` est absent, installer via `winget install Python.Python.3.12` puis relancer.

- [ ] **Step 2: Installer gitleaks**

```powershell
winget search gitleaks
```

Noter l'ID exact affiché (attendu : `Gitleaks.Gitleaks`), puis :

```powershell
winget install --id Gitleaks.Gitleaks -e
gitleaks version
```

Expected: un numéro de version. Si `winget` échoue, télécharger le binaire depuis `https://github.com/gitleaks/gitleaks/releases` et placer `gitleaks.exe` sur le `PATH`.

- [ ] **Step 3: Lancer le scan certifiant sur tout l'historique**

```powershell
gitleaks detect --source . --log-opts="--all" -v
```

Run: depuis `C:\Portfolio Prog\PeekDows`.
Expected: `no leaks found` (code de sortie 0). Conserver la sortie comme preuve (copie dans le message de la Task 15).

---

### Task 9: Inspecter l'objet pendant du fsck (lecture seule)

**Files:** aucun.

- [ ] **Step 1: Lister le contenu de l'arbre pendant**

```powershell
git ls-tree 4534e1d3227692085a3a546054ed76cb52b8c729
```

Expected: une liste de chemins familiers (résidu probable du renommage `SettingsWindow.cs` → `SettingsLegacyWindow.cs` du commit `20de7b7`), **aucun** nom de type `.env`, `*.pem`, `*.key`, `*secret*`, `*password*`.

- [ ] **Step 2: Décision**

  - Si bénin : cocher et passer à la suite (l'objet disparaîtra de toute façon après la réécriture + `git gc`).
  - Si un nom sensible apparaît : STOP, ajouter son chemin à la liste `--path` de la Task 12 avant d'exécuter.

---

### Task 10: Vérifier les licences NuGet et créer THIRD-PARTY-NOTICES.md

**Files:**
- Create: `THIRD-PARTY-NOTICES.md`

- [ ] **Step 1: Vérifier chaque package sur nuget.org**

Ouvrir ces 5 pages et noter la licence affichée :
`https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47`,
`https://www.nuget.org/packages/coverlet.collector/6.0.4`,
`https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1`,
`https://www.nuget.org/packages/xunit/2.9.3`,
`https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.4`.

Attendu : WebView2 sous termes Microsoft propriétaires ; les 4 autres sous licences permissives (MIT / Apache-2.0). Si une licence réelle diffère du tableau du Step 2, mettre à jour le tableau avant de créer le fichier.

- [ ] **Step 2: Créer `THIRD-PARTY-NOTICES.md` avec le contenu exact ci-dessous**

```markdown
# Third-Party Notices

PeekDows (MIT) depends on the following NuGet packages:

| Package | Version | License | Notes |
|---|---|---|---|
| Microsoft.Web.WebView2 | 1.0.4191.47 | Microsoft proprietary (WebView2 Runtime terms) | Required at runtime for the settings window; restored from NuGet on build |
| coverlet.collector | 6.0.4 | MIT | Test-only dependency |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Test-only dependency |
| xunit | 2.9.3 | Apache-2.0 | Test-only dependency |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | Test-only dependency |

Licences vérifiées sur nuget.org le 2026-09-26.
```

- [ ] **Step 3: Commit**

```bash
git add THIRD-PARTY-NOTICES.md
git commit -m "docs: add third-party notices for NuGet dependencies"
```

---

### Task 11: Auditer la surface GitHub (issues, PR, releases, wiki, secrets)

**Files:** aucun (interface web + `gh` CLI ; `gh auth` stocke ses identifiants au standard du système, autorisé).

- [ ] **Step 1: S'authentifier**

```powershell
gh auth login
gh auth status
```

Expected: `Logged in to github.com account Abdoullah0055` (ou le compte propriétaire du dépôt).

- [ ] **Step 2: Lister issues et PR, toutes états confondus**

```powershell
gh issue list --state all --limit 100
gh pr list --state all --limit 100
```

Expected: listes affichées (éventuellement vides). Pour chaque issue/PR, même fermée : ouvrir la page, chercher logs, captures d'écran, tokens ou chemins locaux (`C:\Users\...`). Éditer ou supprimer tout contenu sensible trouvé (l'historique des issues reste visible après passage en public).

- [ ] **Step 3: Vérifier releases, secrets et workflows**

```powershell
gh release list
gh secret list
gh workflow list
```

Expected: `gh release list` vide ou ne contenant que des binaires génériques ; `gh secret list` sans secret à valeur exposée ; `gh workflow list` vide (aucun workflow local) ou, si un workflow existe côté distant, vérifier qu'il n'affiche pas de `${{ secrets.* }}` en clair dans les logs.

- [ ] **Step 4: Vérifier le wiki**

Ouvrir `https://github.com/Abdoullah0055/PeekDows/wiki`. Si un wiki existe avec du contenu interne : le purger ou le désactiver (Settings → General → Features → décocher Wikis).

- [ ] **Step 5: Activer la confidentialité de l'e-mail (prérequis à la Task 12)**

Sur `https://github.com/settings/emails` : cocher **Keep my email addresses private** et **Block command line pushes that expose my email**. Configurer ensuite le `user.email` local sur l'adresse noreply (commande exacte, sans PII) :

```powershell
git config user.email "Abdoullah0055@users.noreply.github.com"
git config user.email
```

Expected: `Abdoullah0055@users.noreply.github.com`.

---

### Task 12: Réécrire l'historique (mailmap + purge des docs internes)

**Files:** `.tmp-mailmap` et `.tmp-rewrite/` (racine du repo, untracked, jamais `git add`, supprimés en Task 14).

> Exécuter tous les steps depuis `C:\Portfolio Prog\PeekDows`. Ne jamais lancer `git filter-repo` directement dans le repo de travail : uniquement dans `.tmp-rewrite/` via `git -C`.

- [ ] **Step 1: Générer le fichier mailmap (aucune adresse en clair recopiée à la main)**

```powershell
$line = git shortlog -sne --all
$oldMail = ([regex]::Match($line, '<(.+?)>').Groups[1].Value)
"Abdoullah0055 <Abdoullah0055@users.noreply.github.com> Abdoullah0055 <$oldMail>" | Set-Content "C:\Portfolio Prog\PeekDows\.tmp-mailmap" -Encoding utf8
Get-Content "C:\Portfolio Prog\PeekDows\.tmp-mailmap"
```

Run: depuis `C:\Portfolio Prog\PeekDows`.
Expected: exactement une ligne de la forme `Abdoullah0055 <...noreply...> Abdoullah0055 <...>` (l'ancienne adresse provient de la variable, jamais tapée). Ne jamais `git add` ce fichier.

- [ ] **Step 2: Créer le clone jetable niché dans le repo**

```powershell
git clone "C:\Portfolio Prog\PeekDows" "C:\Portfolio Prog\PeekDows\.tmp-rewrite"
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" rev-list --all --count
```

Expected: `done.` puis le nombre de commits après les Tasks 1 et 3-10 (62 + 1 gitignore + 1 plan + 5 docs = `69` si chaque task a committé une fois ; sinon noter le nombre affiché comme référence R).

- [ ] **Step 3: Lancer la réécriture (mailmap + suppression des docs internes de toute l'historique)**

```powershell
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" filter-repo --force --mailmap "C:\Portfolio Prog\PeekDows\.tmp-mailmap" --invert-paths --path PRD.md --path AGENTS.md --path docs/superpowers/specs --path docs/superpowers/plans/2026-09-09-animated-arrange-single-window-maximize.md --path docs/superpowers/plans/2026-09-11-settings-app-v2.md --path docs/superpowers/plans/2026-09-11-settings-app-v2-checklist.md --path docs/audits --path docs/mockups
```

(Note : le rapport `2026-09-26-OPENSOURCE-AUDIT.md` n'a jamais été versionné — aucun flag nécessaire, et il est gitignoré depuis la Task 1.)

Expected: sortie se terminant par un résumé (`Rewrite ... Ref ... rewritten`) sans erreur. Les messages de commit sont intacts (seuls auteur/committer changent) ; les trailers Codebuff sont conservés.

- [ ] **Step 4: Vérifier la réécriture dans le clone jetable**

```powershell
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" shortlog -sne --all
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" rev-list --all | ForEach-Object { git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" grep -l "@gmail\.com" $_ 2>$null } | Select-Object -Unique
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" ls-files
```

Expected:
1. `shortlog` n'affiche qu'une seule identité avec l'adresse `...noreply...`, zéro occurrence de l'ancien domaine.
2. La commande `grep` sur tout l'historique ne retourne **aucune ligne**.
3. `ls-files` ne contient plus `PRD.md`, `AGENTS.md`, ni rien sous `docs/audits/`, `docs/mockups/`, `docs/superpowers/specs/`, mais contient toujours `LICENSE`, `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `THIRD-PARTY-NOTICES.md`, `.gitattributes`, `.github/dependabot.yml` et ce plan.

- [ ] **Step 5: Vérifier build + tests sur l'historique réécrit**

```powershell
dotnet build "C:\Portfolio Prog\PeekDows\.tmp-rewrite\PeekDows.slnx"
dotnet test "C:\Portfolio Prog\PeekDows\.tmp-rewrite\PeekDows.slnx"
```

Expected: `0 Error(s)` puis `Passed!`. Si échec ici, STOP : ne pas pusher, diagnostiquer dans le clone jetable (le repo de travail et le distant sont intacts).

---

### Task 13: Vérifications finales avant push (dans le clone jetable)

**Files:** aucun.

- [ ] **Step 1: Re-scanner les secrets sur l'historique réécrit**

```powershell
gitleaks detect --source "C:\Portfolio Prog\PeekDows\.tmp-rewrite" --log-opts="--all" -v
```

Expected: `no leaks found`.

- [ ] **Step 2: Contrôler qu'aucun commit n'est vide ou cassé**

```powershell
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" log --all --oneline
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" fsck --lost-found
```

Expected: log lisible (des commits purement docs comme l'ancien `AGENTS.md` ont pu être élagués car devenus vides — c'est normal) ; `fsck` sans `dangling blob` suspect. Si un commit applicatif a disparu, STOP et comparer avec les branches de sauvegarde (Task 1).

---

### Task 14: Force-push + resync + nettoyage des éphémères

**Files:** suppression de `.tmp-rewrite/` et `.tmp-mailmap` (jamais versionnés).

> Précondition : le dépôt distant doit toujours être **Private** (Task 2). Sinon, appliquer la règle de décision de la Task 2 Step 2 avant de continuer.

- [ ] **Step 1: Reconnecter le distant et pusher depuis le clone jetable**

`filter-repo` supprime les remotes du clone : le re-déclarer explicitement, vérifier, puis pusher :

```powershell
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" remote add Peekdows https://github.com/Abdoullah0055/PeekDows.git
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" remote -v
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" push --force --all Peekdows
git -C "C:\Portfolio Prog\PeekDows\.tmp-rewrite" push --force --tags Peekdows
```

Expected: `remote -v` montre uniquement `Peekdows https://github.com/Abdoullah0055/PeekDows.git` ; les push affichent `... -> master (forced update)` (et la branche fix également). Les branches `backup/*` du distant ne sont PAS touchées (sauvegarde intacte).

- [ ] **Step 2: Resynchroniser le clone de travail et supprimer les éphémères**

```powershell
git fetch Peekdows
git reset --hard Peekdows/master
Remove-Item -Recurse -Force "C:\Portfolio Prog\PeekDows\.tmp-rewrite"
Remove-Item -Force "C:\Portfolio Prog\PeekDows\.tmp-mailmap"
git status --short
```

Run: depuis `C:\Portfolio Prog\PeekDows`.
Expected: `git status --short` vide (le rapport d'audit est gitignoré, les éphémères supprimés, le plan est versionné).

- [ ] **Step 3: Re-vérifier l'identité dans le clone de travail**

```powershell
git shortlog -sne --all
git log --all --format="%ae %ce" | Select-Object -Unique
```

Expected: uniquement l'adresse `...noreply...`, aucune autre adresse.

---

### Task 15: Durcissement + suppression sauvegardes + go/no-go (SANS bascule publique)

**Files:** aucun (réglages web + vérifications). La bascule en public est EXCLUE : réservée au propriétaire.

- [ ] **Step 1: Activer les protections (dépôt encore privé)**

Sur `https://github.com/Abdoullah0055/PeekDows/settings` :
1. **Security → Code security** : activer *Secret scanning* et *Push protection*.
2. **Branches → Add branch protection rule** pour `master` : cocher *Require a pull request before merging* (1 approbation).
3. **Security → Private vulnerability reporting** : activer (rend effectif le `SECURITY.md` de la Task 5).

- [ ] **Step 2: Vérifier Dependabot**

Sur `https://github.com/Abdoullah0055/PeekDows/network/updates` (onglet Insights → Dependency graph → Dependabot) : confirmer que le fichier `.github/dependabot.yml` est détecté (première passe sous 24 h).

- [ ] **Step 3: Checklist go/no-go (tout doit être vrai)**

```powershell
git shortlog -sne --all
Test-Path LICENSE, CONTRIBUTING.md, SECURITY.md, CODE_OF_CONDUCT.md, THIRD-PARTY-NOTICES.md, .gitattributes, .github/dependabot.yml
Test-Path PRD.md, AGENTS.md
Test-Path .tmp-rewrite, .tmp-mailmap
Select-String -Path README.md -Pattern "All rights reserved"
git status --short
dotnet build PeekDows.slnx
dotnet test PeekDows.slnx
gitleaks detect --source . --log-opts="--all" -v
```

Expected, dans l'ordre : une seule identité noreply ; `True` × 7 ; `False` × 2 ; `False` × 2 (éphémères supprimés) ; aucune sortie ; aucune sortie ; `0 Error(s)` ; `Passed!` ; `no leaks found`. Plus les confirmations manuelles : Task 11 (issues/PR/releases/wiki/secrets propres), Task 2 (décision visibilité appliquée), preuves gitleaks des Tasks 8 et 13 archivées.

- [ ] **Step 4: Supprimer les branches de sauvegarde du distant**

Uniquement après un Step 3 entièrement vert (la sauvegarde a rempli son rôle) :

```powershell
git push Peekdows --delete backup/pre-opensource-2026-09-26-master backup/pre-opensource-2026-09-26-fix
git ls-remote Peekdows "backup/*"
```

Expected: confirmations de suppression, puis **aucune sortie** pour `ls-remote` (plus aucune ref backup : l'ancien historique à e-mail exposé n'existe plus nulle part côté distant).

- [ ] **Step 5: STOP — bascule réservée au propriétaire (NE PAS EXÉCUTER)**

La bascule Settings → Danger Zone → **Make public** est faite par le propriétaire lui-même, hors subagents.

- [ ] **Step 6: Vérification post-publication (seulement après feu vert du propriétaire)**

```powershell
git clone https://github.com/Abdoullah0055/PeekDows.git "C:\Portfolio Prog\PeekDows\.tmp-public-check"
git -C "C:\Portfolio Prog\PeekDows\.tmp-public-check" log --all --format="%ae %ce" | Select-Object -Unique
Remove-Item -Recurse -Force "C:\Portfolio Prog\PeekDows\.tmp-public-check"
```

Expected: uniquement l'adresse noreply dans le clone public anonyme, puis suppression du dossier (le `git status` final du repo reste vide). Si autre chose apparaît : repasser le dépôt en privé (Danger Zone) et diagnostiquer.
