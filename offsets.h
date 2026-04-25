#pragma once
#include <windows.h>

// Project Diablo 2 (based on 1.13c) Offsets
// Source: D2Ptrs.h from Project-Diablo-2/BH

namespace Offsets {
    // DLL Base Addresses (to be filled at runtime)
    static DWORD D2Client = 0;
    static DWORD D2Common = 0;

    // Functions (Offsets from DLL Base)
    constexpr DWORD RevealAutomapRoom = 0x62580;
    constexpr DWORD InitLevel         = 0x2E360; // D2Common
    
    // Variables (Offsets from DLL Base)
    constexpr DWORD PlayerUnit        = 0x11BBFC;
    constexpr DWORD AutomapLayer      = 0x11C1C4;
}
