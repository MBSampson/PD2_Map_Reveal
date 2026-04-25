#define _DEFINE_PTRS
#include "D2Ptrs.h"
#include "D2Structs.h"
#include <process.h>
#include <windows.h>


// Track current level to avoid spamming reveal
DWORD dwCurrentLevelId = -1;

void RevealCurrentMap() {
  UnitAny *pPlayer = *p_D2CLIENT_PlayerUnit;
  if (!pPlayer || !pPlayer->pPath || !pPlayer->pPath->pRoom1 ||
      !pPlayer->pPath->pRoom1->pRoom2 ||
      !pPlayer->pPath->pRoom1->pRoom2->pLevel) {
    return;
  }

  Level *pLevel = pPlayer->pPath->pRoom1->pRoom2->pLevel;
  if (pLevel->dwLevelNo == dwCurrentLevelId) {
    return;
  }

  AutomapLayer *pLayer = *p_D2CLIENT_AutomapLayer;
  if (!pLayer)
    return;

  // Reveal all rooms in current level
  for (Room2 *pRoom2 = pLevel->pRoom2First; pRoom2;
       pRoom2 = pRoom2->pRoom2Next) {
    bool bAdded = false;
    if (!pRoom2->pRoom1) {
      D2COMMON_AddRoomData(pRoom2->pLevel->pMisc->pAct,
                           pRoom2->pLevel->dwLevelNo, pRoom2->dwPosX,
                           pRoom2->dwPosY, NULL);
      bAdded = true;
    }

    if (pRoom2->pRoom1) {
      D2CLIENT_RevealAutomapRoom(pRoom2->pRoom1, 1, pLayer);
    }

    if (bAdded) {
      D2COMMON_RemoveRoomData(pRoom2->pLevel->pMisc->pAct,
                              pRoom2->pLevel->dwLevelNo, pRoom2->dwPosX,
                              pRoom2->dwPosY, pRoom2->pRoom1);
    }
  }

  dwCurrentLevelId = pLevel->dwLevelNo;
}

unsigned int __stdcall MapThread(void *pArg) {
  while (true) {
    // Sleep to avoid over-utilization
    Sleep(500);

    // Check if player is in game
    UnitAny *pPlayer = *p_D2CLIENT_PlayerUnit;
    if (pPlayer) {
      RevealCurrentMap();
    } else {
      dwCurrentLevelId = -1; // Reset when out of game
    }
  }
  return 0;
}

BOOL WINAPI DllMain(HINSTANCE hInst, DWORD dwReason, LPVOID lpReserved) {
  switch (dwReason) {
  case DLL_PROCESS_ATTACH:
    _beginthreadex(NULL, 0, MapThread, NULL, 0, NULL);
    break;
  }
  return TRUE;
}
