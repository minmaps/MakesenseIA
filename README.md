# MakesenseIA

MakesenseIA est une adaptation **desktop** de [makesense.ai](https://www.makesense.ai/) conçue pour contourner les limites de performance des navigateurs web sur les jeux d’images volumineux et les workflows d’annotation intensifs.

## Pourquoi cette adaptation desktop

La version web de makesense.ai reste très utile, mais certains usages avancés rencontrent des limites côté navigateur :

- consommation mémoire difficile à contrôler sur de gros lots d’images ;
- latence UI plus visible quand le rendu et l’inférence sont lourds ;
- dépendance au runtime du navigateur pour le décodage/rendu.

Cette version desktop vise à fournir une base plus robuste pour la performance, le contrôle des ressources et l’évolution vers des traitements natifs.

## Objectifs du projet

- conserver l’expérience d’annotation de makesense.ai ;
- améliorer la fluidité sur des charges importantes ;
- permettre un pipeline desktop avec composants natifs (rendu/inférence) ;
- préparer une base technique pour des intégrations accélérées (GPU/IA) côté poste local.

## Architecture du dépôt

Le dépôt contient deux axes complémentaires :

- **Application web historique** (React/TypeScript) à la racine du repo ;
- **Socle desktop natif** dans `native/` :
  - `Makesense.Desktop` : interface desktop WPF/MVVM ;
  - `Makesense.Core` : cœur natif C++20 (rendu/performance) ;
  - `Makesense.Formats` : modèles et sérialisation ;
  - `Makesense.Desktop.Tests` : tests du périmètre desktop.

## Prérequis

### Partie web

- Node.js (compatible avec la configuration du projet)
- npm

### Partie desktop native (Windows)

- .NET SDK
- PowerShell (`pwsh`)
- CMake + toolchain Visual Studio C++

## Démarrage rapide

### 1) Lancer la version web (héritée)

```bash
npm install
npm start
```

### 2) Construire la base desktop native

```powershell
pwsh ./native/build-native.ps1 -Configuration Debug
```

## Scripts utiles (racine)

```bash
npm run dev
npm run build
npm test
npm run lint
```

## État actuel

Le projet est en transition vers une approche desktop orientée performance. La base native est présente et la parité fonctionnelle complète avec l’éditeur web historique est encore en progression.

## Licence

Ce projet est distribué sous licence **GPL-3.0**. Voir le fichier [LICENSE](./LICENSE).
