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
