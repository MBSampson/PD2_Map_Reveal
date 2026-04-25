#include <iostream>
#include <string>
#include <tlhelp32.h>
#include <vector>
#include <windows.h>


// Offsets for PD2 1.13c (External)
namespace Offsets {
constexpr DWORD PlayerUnit = 0x11BBFC; // D2Client
// Room structures used by D2 1.13c
constexpr DWORD PathOffset = 0x2C;
constexpr DWORD Room1Offset = 0x1C;
constexpr DWORD Room2Offset = 0x10;
constexpr DWORD LevelOffset = 0x58;
constexpr DWORD Room2FirstOffset = 0x10;
constexpr DWORD Room2NextOffset = 0x24;
constexpr DWORD RoomFlagsOffset = 0x28;
} // namespace Offsets

DWORD GetModuleBase(DWORD dwProcessId, const char *szModuleName) {
  HANDLE hSnap = CreateToolhelp32Snapshot(
      TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, dwProcessId);
  if (hSnap != INVALID_HANDLE_VALUE) {
    MODULEENTRY32 modEntry;
    modEntry.dwSize = sizeof(modEntry);
    if (Module32First(hSnap, &modEntry)) {
      do {
        if (_stricmp(modEntry.szModule, szModuleName) == 0) {
          CloseHandle(hSnap);
          return (DWORD)modEntry.modBaseAddr;
        }
      } while (Module32Next(hSnap, &modEntry));
    }
  }
  CloseHandle(hSnap);
  return 0;
}

int main() {
  std::cout << "Project Diablo 2 Standalone Map Reveal" << std::endl;
  std::cout << "Searching for Game.exe..." << std::endl;

  HWND hwnd = FindWindowA(NULL, "Diablo II");
  if (!hwnd) {
    std::cout << "Game not found! Please run Project Diablo 2." << std::endl;
    Sleep(3000);
    return 1;
  }

  DWORD pid;
  GetWindowThreadProcessId(hwnd, &pid);
  HANDLE hProcess = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
  if (!hProcess) {
    std::cout << "Failed to open game process." << std::endl;
    return 1;
  }

  DWORD d2Client = GetModuleBase(pid, "D2Client.dll");
  if (!d2Client) {
    std::cout << "Could not find D2Client.dll" << std::endl;
    return 1;
  }

  std::cout << "Connected. Monitoring for level changes..." << std::endl;

  DWORD lastLevelId = -1;

  while (true) {
    DWORD pPlayer = 0;
    ReadProcessMemory(hProcess, (LPCVOID)(d2Client + Offsets::PlayerUnit),
                      &pPlayer, sizeof(DWORD), NULL);

    if (pPlayer) {
      DWORD pPath = 0;
      ReadProcessMemory(hProcess, (LPCVOID)(pPlayer + Offsets::PathOffset),
                        &pPath, sizeof(DWORD), NULL);

      if (pPath) {
        DWORD pRoom1 = 0;
        ReadProcessMemory(hProcess, (LPCVOID)(pPath + Offsets::Room1Offset),
                          &pRoom1, sizeof(DWORD), NULL);

        if (pRoom1) {
          DWORD pRoom2 = 0;
          ReadProcessMemory(hProcess, (LPCVOID)(pRoom1 + Offsets::Room2Offset),
                            &pRoom2, sizeof(DWORD), NULL);

          if (pRoom2) {
            DWORD pLevel = 0;
            ReadProcessMemory(hProcess,
                              (LPCVOID)(pRoom2 + Offsets::LevelOffset), &pLevel,
                              sizeof(DWORD), NULL);

            DWORD levelNo = 0;
            ReadProcessMemory(hProcess, (LPCVOID)(pLevel + 0x1D0), &levelNo,
                              sizeof(DWORD), NULL); // dwLevelNo offset

            if (levelNo != lastLevelId) {
              std::cout << "New Level Detected: " << levelNo
                        << ". Unveiling map..." << std::endl;

              DWORD pRoom2First = 0;
              ReadProcessMemory(hProcess,
                                (LPCVOID)(pLevel + Offsets::Room2FirstOffset),
                                &pRoom2First, sizeof(DWORD), NULL);

              DWORD currRoom2 = pRoom2First;
              while (currRoom2) {
                DWORD dwFlags = 0;
                ReadProcessMemory(
                    hProcess, (LPCVOID)(currRoom2 + Offsets::RoomFlagsOffset),
                    &dwFlags, sizeof(DWORD), NULL);

                // Set the revealed flag (0x01)
                dwFlags |= 0x01;
                WriteProcessMemory(
                    hProcess, (LPVOID)(currRoom2 + Offsets::RoomFlagsOffset),
                    &dwFlags, sizeof(DWORD), NULL);

                // Move to next room
                ReadProcessMemory(
                    hProcess, (LPCVOID)(currRoom2 + Offsets::Room2NextOffset),
                    &currRoom2, sizeof(DWORD), NULL);
              }

              lastLevelId = levelNo;
              std::cout << "Map Revealed." << std::endl;
            }
          }
        }
      }
    } else {
      lastLevelId = -1;
    }

    Sleep(1000);
  }

  CloseHandle(hProcess);
  return 0;
}
