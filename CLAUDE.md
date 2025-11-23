# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity battleship game project built with Unity 6000.0.32f1. The project uses the Universal Render Pipeline (URP), TextMesh Pro for UI text, and includes the New Input System.

## Architecture

### Board System

The game uses a grid-based board system with the following architecture:

- **BoardManager** (`Assets/BoardGame/Scripts/BoardManager.cs`): Manages a 72x60 grid of tiles
  - Instantiates tiles at Start via `InitializeBoard()`
  - Stores tiles in a 2D array `gameTiles[BOARD_WIDTH, BOARD_HEIGHT]`
  - Each tile is positioned at (x, 0, y) in world space
  - Tiles are parented to `ParentTransform` using `SetParent(ParentTransform, false)` to preserve world positions
  - Sets up each tile's `BoardTile` component with row/col coordinates and caption text format: "B1:[xx,yy]"
  - Handles mouse click detection using raycasting to detect tiles with "BoardTile" tag

- **BoardTile** (`Assets/BoardTile.cs`): Individual tile component
  - Stores grid position (`row`, `col`)
  - Has a TextMesh Pro text component (`boardTileCaptionText`) for displaying tile coordinates
  - Must be tagged with "BoardTile" for click detection to work

### Key Interaction Pattern

Mouse clicks are handled via Physics.Raycast in BoardManager.Update():
1. Ray cast from camera through mouse position
2. Detect hits on objects tagged "BoardTile"
3. Retrieve BoardTile component and log caption text

### Project Structure

- **Assets/BoardGame/**: Core game code and prefabs
  - **Scripts/**: Game logic (`BoardManager.cs`)
  - **Prefabs/**: Tile prefabs (`BoardTile.prefab`, `BoardTileRoot.prefab`)
- **Assets/BoardTile.cs**: Tile component (note: located at root of Assets, not in BoardGame folder)
- **Assets/Scenes/**: Unity scenes (`SampleScene.unity`)
- **Assets/Settings/**: Project-wide settings (URP, Input System)

## Development Commands

### Opening the Project

Open in Unity Editor (version 6000.0.32f1 or compatible):
```bash
# Via Unity Hub: select this directory
# Via command line (macOS):
open -a Unity battleship_game.sln
```

### Building

Unity projects are built through the Unity Editor:
- File → Build Settings → Build
- Or use Unity command line build with platform-specific arguments

### Testing

The project includes Unity Test Framework (`com.unity.test-framework` v1.4.5):
- Window → General → Test Runner
- Supports Play Mode and Edit Mode tests

## Key Unity Packages

- **TextMesh Pro**: UI text rendering (used for tile captions)
- **Input System** (v1.11.2): New Input System for player controls
- **URP** (v17.0.3): Universal Render Pipeline for rendering
- **AI Navigation** (v2.0.5): NavMesh for AI pathfinding
- **Timeline** (v1.8.7): Cinematic sequencing

## Important Notes

- Board dimensions are 72 tiles wide × 60 tiles high (BOARD_WIDTH × BOARD_HEIGHT)
- Tiles must have "BoardTile" tag for click detection
- Caption text format is "B1:[xx,yy]" where xx=row, yy=col (zero-padded to 2 digits)
- Tile.row stores x coordinate, Tile.col stores y coordinate
