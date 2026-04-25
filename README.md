# Project Diablo 2 Map Reveal

A custom map reveal tool for Project Diablo 2 (Season 10).

## Demo

[![Watch the demo](https://img.youtube.com/vi/4OkOTdvU8qU/maxresdefault.jpg)](https://youtu.be/4OkOTdvU8qU)

## Features
- **Automatic Reveal**: Instantly reveals the entire map (automap) when entering a new level or game.
- **Resource Efficient**: Runs as a lightweight background thread.
- **PD2 Native**: Uses structures and offsets specifically updated for Project Diablo 2.

## Project Structure
- `main.cpp`: Main logic and game state monitoring.
- `D2Ptrs.h` & `D2Structs.h`: Game-specific pointers and memory structures.
- `Patch.cpp` & `D2Version.cpp`: Utility for memory patching and version identification.
- `offsets.h`: Hardcoded memory offsets for the PD2 client.

## Building
1. Open the project folder.
2. Double-click `build_and_run.bat`. This script will:
   - Attempt to find the C++ compiler (`cl.exe`).
   - If missing, it will automatically fall back to the built-in Windows C# compiler (`csc.exe`).
   - Create `PD2_MapReveal.exe` in the same folder.

## Usage

1. **Launch Project Diablo 2** and wait until you reach the character select screen.
2. **Run `PD2_MapReveal.exe` as Administrator.** You can do this by either:
   - Double-clicking `build_and_run.bat` (it will compile and launch as Admin automatically), or
   - Right-clicking `PD2_MapReveal.exe` → **Run as administrator**.
3. The console window will display `Waiting for Game.exe...` until it detects the running game.
4. **Join or create a game.** Once you enter the game world, the tool will automatically detect your session and begin working.
5. **Press Tab** to open the automap. The tool requires the automap to be active to reveal the map.
6. **Play normally.** As you move through the game and enter new areas (e.g., walking through a waypoint, taking stairs, or changing acts), the tool will automatically reveal the full map of each new zone.

### Expected Behavior
- On each level change, the console will log `Level XX: Revealing...` followed by the number of rooms revealed.
- The full automap for the current zone will appear immediately — you do not need to walk through fog to uncover rooms.
- The tool runs continuously in the background and re-reveals periodically (every ~15 seconds) as a safety net.
- If the tool loses track of the game (e.g., you exit to the menu), it will reset and wait for the next game session automatically.

## Disclaimer
This tool is for educational purposes and development demonstration. Use on official servers may result in a ban. Follow Project Diablo 2's terms of service.
