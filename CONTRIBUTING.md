# Contributing to AutoWhisper

Thanks for your interest in contributing! Here's how you can help.

## Getting Started

1. Fork the repository
2. Clone your fork and create a new branch from `main`
3. Install prerequisites: .NET 10 SDK
4. Build and run:
   ```bash
   dotnet build src/AutoWhisper/AutoWhisper.csproj -c Release
   dotnet run --project src/AutoWhisper/AutoWhisper.csproj
   ```

## Making Changes

- Keep changes focused — one feature or fix per pull request
- Follow the existing code style and project structure
- Test your changes locally before submitting

## Commit Messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/):

- `feat:` — new feature
- `fix:` — bug fix
- `docs:` — documentation only
- `refactor:` — code change that neither fixes a bug nor adds a feature
- `chore:` — maintenance tasks

Examples:
```
feat: add support for new Whisper model size
fix: resolve hotkey not releasing on focus loss
docs: update build instructions
```

## Pull Requests

1. Push your branch to your fork
2. Open a pull request against `main`
3. Describe what your change does and why
4. Link any related issues

## Code of Conduct

Be respectful and constructive. We're all here to make AutoWhisper better.
