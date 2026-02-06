# CLAUDE.md

## Project Overview

**quantum-dharma-npc** is a Unity game project.

## Tech Stack

- **Engine:** Unity
- **Language:** C#
- **License:** MIT

## Project Structure

```
Assets/          # Game assets, scripts, scenes, prefabs
Library/         # Unity-generated cache (git-ignored)
ProjectSettings/ # Unity project settings
Packages/        # Unity package manager dependencies
```

## Development Guidelines

- Unity `.meta` files must always be committed alongside their corresponding assets
- Never commit the `Library/`, `Temp/`, `Obj/`, or `Build/` directories
- C# scripts go under `Assets/Scripts/`
- Use PascalCase for public methods and properties, camelCase for private fields
- Prefix private fields with underscore (e.g., `_health`)

## Build & Run

- Open the project in Unity Editor
- Use **File > Build Settings** to configure and build

## Testing

- Unity Test Runner: **Window > General > Test Runner**
- Tests use the `NUnit` framework via Unity's Test Framework package
