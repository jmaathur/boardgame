# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity battleship game project built with Unity 6000.0.32f1. The project uses the Universal Render Pipeline (URP) and includes the New Input System.

## Architecture

### Project Structure

- **Assets/BoardGame/**: Core game code and prefabs
  - **Scripts/**: Game logic (currently contains `BoardManager.cs`)
  - **Prefabs/**: Reusable game objects (`BoardTile.prefab`)
- **Assets/Scenes/**: Unity scenes (`SampleScene.unity`)
- **Assets/Settings/**: Project-wide settings (URP, Input System, etc.)

### Key Components

- **BoardManager.cs** (`Assets/BoardGame/Scripts/BoardManager.cs`): Main board controller with references to parent transform and board tile prefab (currently empty implementation)

## Development Commands

### Opening the Project

Open the project in Unity Editor (version 6000.0.32f1 or compatible):
```bash
# The project can be opened by launching Unity Hub and selecting this directory
# Or via command line (macOS):
open -a Unity battleship_game.sln
```

### Building the Project

Unity projects are typically built through the Unity Editor:
- File → Build Settings → Build
- Or use Unity command line build with platform-specific arguments

### Testing

The project includes Unity Test Framework (`com.unity.test-framework` v1.4.5):
- Tests can be run via Unity Editor: Window → General → Test Runner
- Supports both Play Mode and Edit Mode tests

### Code Editing

C# scripts are in the `Assembly-CSharp` and `Assembly-CSharp-Editor` assemblies:
- Main game scripts: `Assembly-CSharp.csproj`
- Editor scripts: `Assembly-CSharp-Editor.csproj`
- Solution file: `battleship_game.sln`

## Key Unity Packages

- **Input System** (v1.11.2): New Input System for player controls
- **URP** (v17.0.3): Universal Render Pipeline for rendering
- **AI Navigation** (v2.0.5): NavMesh for AI pathfinding
- **Timeline** (v1.8.7): Cinematic sequencing
- **Visual Scripting** (v1.9.5): Node-based scripting system

## Notes

- Project currently in early development stage with minimal game logic implemented
- BoardManager component needs implementation for board generation and tile management
- Uses Unity's new MonoBehaviour lifecycle (methods include Start/Update)
