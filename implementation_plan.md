# Implementation Plan - Project Diablo 2 Map Reveal

Developing a standalone map reveal tool for Project Diablo 2. The tool will hook into the game process and reveal the entire map upon entering a game.

## Architecture
The project will consist of a DLL (C++) that can be injected into `Game.exe`. The DLL will:
1. Identify the game version and locate necessary DLL bases (`D2Client.dll`, `D2Common.dll`).
2. Hook or periodically check the player state.
3. Once a player is in-game, iterate through all rooms in the current level.
4. Call `D2Common!InitLevel` and `D2Client!RevealAutomapRoom` to reveal each room.

## Key Offsets (Tentative - Based on BH Research)
- `D2CLIENT_pPlayer`: Base pointer for the local player unit.
- `D2CLIENT_RevealAutomapRoom`: Function to reveal a specific room.
- `D2COMMON_InitLevel`: Function to initialize room data if not loaded.
- `D2CLIENT_AutomapLayer`: Pointer to the current automap layer.

## Development Steps
1. **Research Offsets**: Verify the specific offsets for PD2 Season 10.
2. **C++ DLL Structure**: Create the entry point and basic memory management.
3. **Map Reveal Logic**:
    - Get `pPlayer`.
    - Traverse `pPlayer -> pPath -> pRoom1 -> pRoom2 -> pLevel`.
    - Loop through `pLevel -> pRoom2First`.
    - Apply reveal logic to each room.
4. **Automation**: Implement a thread that monitors game state and triggers the reveal once per level change.
5. **Testing**: Verify in-game.

## Standalone Mode (External)
For convenience, a standalone version is available that does not require DLL injection. It operates by reading and writing process memory from a separate executable.

- `standalone.cpp`: The external logic implementation.
- `build_and_run.bat`: Compiles the standalone version into `PD2_MapReveal.exe`.

## Files to Create
- `main.cpp`: DLL entry point.
- `standalone.cpp`: EXE entry point.
- `build_and_run.bat`: Automated build script.
- `offsets.h`: Memory addresses.
- `d2structs.h`: Game data structures.
