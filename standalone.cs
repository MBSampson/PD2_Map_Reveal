using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace PD2MapReveal
{
    class Program
    {
        // --- Win32 P/Invoke ---
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint acc, bool inh, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, uint size, uint prot, out uint old);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, uint size, uint allocType, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr h, IntPtr addr, uint size, uint freeType);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr attr, uint stackSize, IntPtr startAddr, IntPtr param, uint flags, out uint threadId);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        static IntPtr hProcess;
        static IntPtr d2Client;
        static IntPtr d2Common;

        // --- D2Client offsets (1.13c) ---
        const int PlayerUnit = 0x11BBFC;
        const int RevealFn = 0x62580;           // D2CLIENT_RevealAutomapRoom
        const int AutomapLayerVar = 0x11C1C4;   // p_D2CLIENT_AutomapLayer

        // AutomapLayer struct offsets (from D2Structs.h)
        // nLayerNo at +0x00 identifies which act/layer this automap belongs to
        const int AutomapLayer_nLayerNo = 0x00;
        const int AutomapLayer_pFloors = 0x08;
        const int AutomapLayer_pWalls = 0x0C;

        // Room/Level traversal offsets (verified from D2Structs.h)
        const int Room2_pRoom2Next = 0x24;
        const int Room2_dwRoomFlags = 0x28;
        const int Room2_pRoom1 = 0x30;
        const int Room2_dwPosX = 0x34;
        const int Room2_dwPosY = 0x38;
        const int Room2_pLevel = 0x58;
        const int Level_pRoom2First = 0x10;
        const int Level_pMisc = 0x1B4;
        const int Level_dwLevelNo = 0x1D0;
        const int ActMisc_pAct = 0x46C;
        const int Path_pRoom1 = 0x1C;
        const int Room1_pRoom2 = 0x10;

        // --- Memory read helpers ---
        static int Read(IntPtr addr)
        {
            byte[] b = new byte[4]; int r;
            if (ReadProcessMemory(hProcess, addr, b, 4, out r)) return BitConverter.ToInt32(b, 0);
            return 0;
        }

        static short ReadShort(IntPtr addr)
        {
            byte[] b = new byte[2]; int r;
            if (ReadProcessMemory(hProcess, addr, b, 2, out r)) return BitConverter.ToInt16(b, 0);
            return 0;
        }

        static void Write(IntPtr addr, int value)
        {
            int w; byte[] b = BitConverter.GetBytes(value);
            WriteProcessMemory(hProcess, addr, b, 4, out w);
        }

        // Safe patching: only writes if existing bytes match expected values
        static bool SafePatch(IntPtr addr, byte[] expected, byte[] patch)
        {
            byte[] current = new byte[expected.Length]; int r;
            ReadProcessMemory(hProcess, addr, current, current.Length, out r);
            for (int i = 0; i < expected.Length; i++)
                if (current[i] != expected[i]) return false;
            uint oldProt; int w; uint dummy;
            VirtualProtectEx(hProcess, addr, (uint)patch.Length, 0x40, out oldProt);
            WriteProcessMemory(hProcess, addr, patch, patch.Length, out w);
            VirtualProtectEx(hProcess, addr, (uint)patch.Length, oldProt, out dummy);
            return true;
        }

        /// <summary>
        /// Resolve a function address by ordinal by reading the PE export table
        /// directly from the remote process memory. No local DLL loading needed.
        /// </summary>
        static IntPtr ResolveOrdinalRemote(IntPtr dllBase, int ordinal)
        {
            // Read DOS header -> e_lfanew at offset 0x3C
            int e_lfanew = Read((IntPtr)((long)dllBase + 0x3C));
            if (e_lfanew == 0 || e_lfanew > 0x1000) return IntPtr.Zero;

            // PE signature at e_lfanew, optional header starts at e_lfanew + 0x18 (32-bit)
            // Export directory RVA is in DataDirectory[0] at optional header + 0x60
            int exportDirRVA = Read((IntPtr)((long)dllBase + e_lfanew + 0x78));
            if (exportDirRVA == 0) return IntPtr.Zero;

            IntPtr exportDir = (IntPtr)((long)dllBase + exportDirRVA);

            // Export directory structure:
            // +0x10 = Base (ordinal base)
            // +0x14 = NumberOfFunctions
            // +0x1C = AddressOfFunctions (RVA)
            int ordBase = Read((IntPtr)((long)exportDir + 0x10));
            int numFunctions = Read((IntPtr)((long)exportDir + 0x14));
            int addrOfFunctionsRVA = Read((IntPtr)((long)exportDir + 0x1C));

            // Calculate index from ordinal
            int index = ordinal - ordBase;
            if (index < 0 || index >= numFunctions) return IntPtr.Zero;

            // Read the function RVA from the AddressOfFunctions array
            int funcRVA = Read((IntPtr)((long)dllBase + addrOfFunctionsRVA + index * 4));
            if (funcRVA == 0) return IntPtr.Zero;

            return (IntPtr)((long)dllBase + funcRVA);
        }

        /// <summary>
        /// Build and execute shellcode in the remote process to reveal a single room.
        /// For rooms without Room1: calls AddRoomData, RevealAutomapRoom, RemoveRoomData
        /// For rooms with Room1: calls RevealAutomapRoom only
        /// </summary>
        static bool RevealRoomRemote(
            int pRoom2, int pAct, int dwLevelNo, int dwPosX, int dwPosY,
            IntPtr addRoomAddr, IntPtr removeRoomAddr, IntPtr revealAddr, int pAutomapLayer,
            bool needAdd)
        {
            List<byte> code = new List<byte>();

            if (needAdd)
            {
                // --- Call D2COMMON_AddRoomData(pAct, dwLevelNo, dwPosX, dwPosY, NULL) ---
                // All __stdcall: args pushed right-to-left
                // push 0 (pRoom = NULL)
                code.Add(0x6A); code.Add(0x00);
                // push dwPosY
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(dwPosY));
                // push dwPosX
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(dwPosX));
                // push dwLevelNo
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(dwLevelNo));
                // push pAct
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(pAct));
                // mov eax, addRoomAddr; call eax
                code.Add(0xB8); code.AddRange(BitConverter.GetBytes((int)addRoomAddr));
                code.Add(0xFF); code.Add(0xD0);
            }

            // Read pRoom1 from memory at runtime: mov eax, [pRoom2 + 0x30]
            code.Add(0xB8); code.AddRange(BitConverter.GetBytes(pRoom2 + Room2_pRoom1));
            // mov eax, [eax]
            code.Add(0x8B); code.Add(0x00);
            // test eax, eax
            code.Add(0x85); code.Add(0xC0);
            // jz skip (placeholder offset)
            int jzPatchPos = code.Count;
            code.Add(0x74); code.Add(0x00);

            // Save pRoom1 on stack for RemoveRoomData later
            // push eax (save pRoom1)
            code.Add(0x50);

            // --- Call D2CLIENT_RevealAutomapRoom(pRoom1, 1, pAutomapLayer) ---
            // __stdcall: args right-to-left
            // push pAutomapLayer
            code.Add(0x68); code.AddRange(BitConverter.GetBytes(pAutomapLayer));
            // push 1 (dwClipFlag)
            code.Add(0x6A); code.Add(0x01);
            // push eax (pRoom1 — still in eax)
            code.Add(0x50);
            // mov eax, revealAddr; call eax
            code.Add(0xB8); code.AddRange(BitConverter.GetBytes((int)revealAddr));
            code.Add(0xFF); code.Add(0xD0);

            if (needAdd)
            {
                // --- Call D2COMMON_RemoveRoomData(pAct, dwLevelNo, dwPosX, dwPosY, pRoom1) ---
                // pop ecx (recover pRoom1 from stack)
                code.Add(0x59);
                // push ecx (pRoom1)
                code.Add(0x51);
                // push dwPosY
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(dwPosY));
                // push dwPosX
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(dwPosX));
                // push dwLevelNo
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(dwLevelNo));
                // push pAct
                code.Add(0x68); code.AddRange(BitConverter.GetBytes(pAct));
                // mov eax, removeRoomAddr; call eax
                code.Add(0xB8); code.AddRange(BitConverter.GetBytes((int)removeRoomAddr));
                code.Add(0xFF); code.Add(0xD0);
                // jmp to ret
                int jmpPos = code.Count;
                code.Add(0xEB); code.Add(0x00); // placeholder
                // Patch the jz to land here (skip block)
                code[jzPatchPos + 1] = (byte)(code.Count - (jzPatchPos + 2));
                // Patch the jmp to land at ret
                // (we'll patch after adding ret)
                // pop ecx to balance the push eax we did before the jz didn't skip
                // Actually, if jz skips, the push eax never happened, so no pop needed
                // And if jz didn't skip, we already popped with pop ecx above
                // So the jmp target is just the ret
                code[jmpPos + 1] = (byte)(code.Count - (jmpPos + 2));
            }
            else
            {
                // No RemoveRoomData needed; balance the push eax (pRoom1)
                // pop ecx (discard saved pRoom1)
                code.Add(0x59);
                // jmp to ret
                int jmpPos = code.Count;
                code.Add(0xEB); code.Add(0x00);
                // Patch jz to skip here
                code[jzPatchPos + 1] = (byte)(code.Count - (jzPatchPos + 2));
                code[jmpPos + 1] = (byte)(code.Count - (jmpPos + 2));
            }

            // ret 4 (CreateRemoteThread passes one LPVOID parameter)
            code.Add(0xC2); code.AddRange(BitConverter.GetBytes((short)4));

            byte[] shellcode = code.ToArray();

            // Allocate executable memory in the remote process
            IntPtr remoteCode = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)(shellcode.Length + 16), 0x3000, 0x40);
            if (remoteCode == IntPtr.Zero) return false;

            int written;
            WriteProcessMemory(hProcess, remoteCode, shellcode, shellcode.Length, out written);

            uint threadId;
            IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, remoteCode, IntPtr.Zero, 0, out threadId);
            if (hThread == IntPtr.Zero)
            {
                VirtualFreeEx(hProcess, remoteCode, 0, 0x8000);
                return false;
            }

            // Wait for completion (5 second timeout per room)
            uint waitResult = WaitForSingleObject(hThread, 5000);
            CloseHandle(hThread);

            // CRITICAL: If the thread timed out (0x102 = WAIT_TIMEOUT), do NOT free
            // the code memory. The thread is still executing inside the game process —
            // freeing its code memory causes an access violation crash.
            // A small memory leak is vastly preferable to crashing the game.
            if (waitResult == 0x00000102)
            {
                Console.WriteLine("  [WARN] Remote thread timed out — leaked memory to prevent crash");
                return false;
            }

            VirtualFreeEx(hProcess, remoteCode, 0, 0x8000);
            return true;
        }

        /// <summary>
        /// Full map reveal: iterates all Room2s in the level and injects shellcode
        /// to call the game's own reveal functions. Works for outdoor and indoor areas.
        /// </summary>
        /// <param name="lightMode">When true (periodic re-reveals), skip rooms that need
        /// AddRoomData to avoid lock contention and access violations during gameplay.</param>
        static int RevealLevel(int pLevel, IntPtr addRoomAddr, IntPtr removeRoomAddr, IntPtr revealAddr, int pAutomapLayer, bool lightMode = false)
        {
            int dwLevelNo = Read((IntPtr)(pLevel + Level_dwLevelNo));
            int pMisc = Read((IntPtr)(pLevel + Level_pMisc));
            if (pMisc == 0) { Console.WriteLine("  [WARN] pMisc is null"); return 0; }
            int pAct = Read((IntPtr)(pMisc + ActMisc_pAct));
            if (pAct == 0) { Console.WriteLine("  [WARN] pAct is null"); return 0; }

            int pRoom2 = Read((IntPtr)(pLevel + Level_pRoom2First));
            int count = 0;
            int addedCount = 0;
            int safety = 0;

            while (pRoom2 != 0 && safety < 5000)
            {
                int pRoom1 = Read((IntPtr)(pRoom2 + Room2_pRoom1));
                int dwPosX = Read((IntPtr)(pRoom2 + Room2_dwPosX));
                int dwPosY = Read((IntPtr)(pRoom2 + Room2_dwPosY));
                bool needAdd = (pRoom1 == 0);

                // In light mode (periodic re-reveal), skip rooms without Room1.
                // AddRoomData/RemoveRoomData are heavyweight game functions that take
                // internal locks — calling them via remote thread during active gameplay
                // causes lock contention, deadlocks, and access violations.
                if (lightMode && needAdd)
                {
                    pRoom2 = Read((IntPtr)(pRoom2 + Room2_pRoom2Next));
                    safety++;
                    continue;
                }

                if (RevealRoomRemote(pRoom2, pAct, dwLevelNo, dwPosX, dwPosY,
                    addRoomAddr, removeRoomAddr, revealAddr, pAutomapLayer, needAdd))
                {
                    count++;
                    if (needAdd) addedCount++;
                }

                // Also set the room flags for good measure
                int flags = Read((IntPtr)(pRoom2 + Room2_dwRoomFlags));
                if ((flags & 0x01) == 0)
                {
                    Write((IntPtr)(pRoom2 + Room2_dwRoomFlags), flags | 0x01);
                }

                pRoom2 = Read((IntPtr)(pRoom2 + Room2_pRoom2Next));
                safety++;
            }

            string mode = lightMode ? " [light]" : "";
            Console.WriteLine("  -> " + count + " rooms revealed (" + addedCount + " needed Room1 init)" + mode);
            return count;
        }

        [HandleProcessCorruptedStateExceptions]
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("PD2 Map Reveal (Aggressive Reveal)");
                Console.WriteLine("===============================");
                Console.WriteLine();

                // Wait for game process
                Console.Write("Waiting for Game.exe...");
                uint pid = 0;
                while (pid == 0)
                {
                    Process[] procs = Process.GetProcessesByName("Game");
                    if (procs.Length > 0) pid = (uint)procs[0].Id;
                    else { Console.Write("."); Thread.Sleep(1000); }
                }
                Console.WriteLine(" Found! (PID: " + pid + ")");

                hProcess = OpenProcess(0x1F0FFF, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    Console.WriteLine("[FATAL] Failed to open process. Run as Administrator.");
                    Console.ReadLine();
                    return;
                }

                Process gameProc = Process.GetProcessById((int)pid);
                d2Client = IntPtr.Zero;
                d2Common = IntPtr.Zero;

                foreach (ProcessModule mod in gameProc.Modules)
                {
                    string name = mod.ModuleName.ToLower();
                    if (name == "d2client.dll") d2Client = mod.BaseAddress;
                    if (name == "d2common.dll") d2Common = mod.BaseAddress;
                }

                if (d2Client == IntPtr.Zero) { Console.WriteLine("[FATAL] D2Client.dll not found."); Console.ReadLine(); return; }
                if (d2Common == IntPtr.Zero) { Console.WriteLine("[FATAL] D2Common.dll not found."); Console.ReadLine(); return; }

                Console.WriteLine("[OK] D2Client @ 0x" + ((int)d2Client).ToString("X8"));
                Console.WriteLine("[OK] D2Common @ 0x" + ((int)d2Common).ToString("X8"));

                // Resolve D2Common function addresses by reading PE export table from remote process
                // Ordinals from D2Ptrs.h: AddRoomData = 10401, RemoveRoomData = 11099
                IntPtr addRoomAddr = ResolveOrdinalRemote(d2Common, 10401);
                IntPtr removeRoomAddr = ResolveOrdinalRemote(d2Common, 11099);
                IntPtr revealAddr = (IntPtr)((long)d2Client + RevealFn);

                if (addRoomAddr == IntPtr.Zero) { Console.WriteLine("[FATAL] Failed to resolve AddRoomData (ordinal 10401)"); Console.ReadLine(); return; }
                if (removeRoomAddr == IntPtr.Zero) { Console.WriteLine("[FATAL] Failed to resolve RemoveRoomData (ordinal 11099)"); Console.ReadLine(); return; }

                Console.WriteLine("[OK] AddRoomData    @ 0x" + ((int)addRoomAddr).ToString("X8"));
                Console.WriteLine("[OK] RemoveRoomData @ 0x" + ((int)removeRoomAddr).ToString("X8"));
                Console.WriteLine("[OK] RevealAutomap  @ 0x" + ((int)revealAddr).ToString("X8"));

                // Patch bytes for NOP-ing visibility checks in RevealAutomapRoom
                byte[] jz = { 0x74, 0x10 };
                byte[] nop = { 0x90, 0x90 };

                // Helper to apply (or re-apply) NOP patches
                // Called on each new game since patches can revert when DLLs reload
                bool patchesApplied = false;
                Action applyPatches = delegate
                {
                    bool f = SafePatch((IntPtr)((long)d2Client + RevealFn + 0x4C), jz, nop);
                    bool w = SafePatch((IntPtr)((long)d2Client + RevealFn + 0xA2), jz, nop);
                    if (f) Console.WriteLine("[OK] Floor reveal patched");
                    if (w) Console.WriteLine("[OK] Wall reveal patched");
                    if (!f && !w && !patchesApplied) Console.WriteLine("[--] Patches already applied");
                    patchesApplied = true;
                };

                // Apply initial patches
                applyPatches();

                Console.WriteLine();
                Console.WriteLine("Map reveal active. Open automap (Tab) and change levels.");
                Console.WriteLine("Press Ctrl+C to exit.");
                Console.WriteLine();

                // Track player pointer and level pointer to detect new games/levels
                // Using both prevents missing reveals when a new game starts at the same level number
                int lastPlayerPtr = 0;  // Detects new game sessions
                int lastLevelPtr = 0;   // Detects level changes even with same level number
                int lastLevelNo = -1;   // Detects level changes within the same game
                int lastAutomapLayer = 0;   // Layer 2: Tracks automap layer pointer for staleness detection
                int lastRevealTick = Environment.TickCount; // Layer 3: Timer for periodic re-reveal safety net
                const int REREVEAL_INTERVAL_MS = 15000;     // Layer 3: Re-reveal every 15 seconds as safety net

                // BUG FIX: Stabilization delay (ms) after a level change before revealing.
                // When entering a new zone, the game needs time to initialize the Level,
                // Room2 linked list, and AutomapLayer for the new area. Revealing too early
                // reads a stale AutomapLayer pointer (from the previous zone), which writes
                // cells into a layer the renderer no longer displays — causing a blank map.
                const int LEVEL_CHANGE_STABILIZE_MS = 800;

                // BUG FIX: Number of retries if the AutomapLayer appears stale after a
                // level change. The game may take several hundred ms to swap the layer.
                const int AUTOMAP_LAYER_RETRIES = 5;
                const int AUTOMAP_LAYER_RETRY_DELAY_MS = 300;

                // BUG FIX: Quick post-reveal verification delay. After revealing, we
                // re-check the automap layer pointer to see if the game swapped it
                // out from under us (indicating we revealed against a stale layer).
                const int POST_REVEAL_VERIFY_DELAY_MS = 500;

                while (true)
                {
                    try
                    {
                        int pPlayer = Read((IntPtr)((long)d2Client + PlayerUnit));
                        if (pPlayer != 0)
                        {
                            // Detect new game: player pointer changed
                            if (pPlayer != lastPlayerPtr)
                            {
                                Console.WriteLine("[*] New game detected (player @ 0x" + pPlayer.ToString("X8") + ")");
                                lastPlayerPtr = pPlayer;
                                lastLevelPtr = 0;
                                lastLevelNo = -1;
                                patchesApplied = false;
                                lastAutomapLayer = 0;
                                lastRevealTick = Environment.TickCount;

                                // Re-apply NOP patches in case they reverted between games
                                applyPatches();

                                // Brief delay to let the game finish initializing
                                Thread.Sleep(1500);
                                continue;
                            }

                            // Layer 1: Continuously verify NOP patches every cycle.
                            // Warden can silently revert patches mid-session; this catches it immediately.
                            applyPatches();

                            int pPath = Read((IntPtr)(pPlayer + 0x2C));
                            if (pPath == 0) { Thread.Sleep(500); continue; }
                            int pRoom1 = Read((IntPtr)(pPath + Path_pRoom1));
                            if (pRoom1 == 0) { Thread.Sleep(500); continue; }
                            int pRoom2 = Read((IntPtr)(pRoom1 + Room1_pRoom2));
                            if (pRoom2 == 0) { Thread.Sleep(500); continue; }
                            int pLevel = Read((IntPtr)(pRoom2 + Room2_pLevel));
                            if (pLevel == 0) { Thread.Sleep(500); continue; }
                            int levelNo = Read((IntPtr)(pLevel + Level_dwLevelNo));

                            // Read the current automap layer pointer every cycle (needed for Layer 2)
                            int pAutomapLayer = Read((IntPtr)((long)d2Client + AutomapLayerVar));

                            // --- Layer 2: Detect automap layer staleness ---
                            // If the game re-creates the AutomapLayer (tab toggle, internal refresh),
                            // revealed cells belong to the old/freed object and the current one is empty.
                            bool automapLayerChanged = (pAutomapLayer != 0 && lastAutomapLayer != 0 && pAutomapLayer != lastAutomapLayer);

                            // --- Layer 3: Periodic re-reveal safety net ---
                            // Catch-all for any map invalidation we didn't anticipate.
                            int elapsed = Environment.TickCount - lastRevealTick;
                            bool timerExpired = (elapsed > REREVEAL_INTERVAL_MS) && lastLevelNo > 0;

                            // Original trigger: level number or level pointer changed
                            bool levelChanged = (levelNo != lastLevelNo) || (pLevel != lastLevelPtr);

                            // Combined decision: reveal if ANY layer triggers
                            bool shouldReveal = levelChanged || automapLayerChanged || timerExpired;

                            if (shouldReveal && levelNo > 0)
                            {
                                // BUG FIX: On a genuine level change, wait for the game to
                                // finish initializing the new zone's structures. Without this
                                // delay, we often read a stale AutomapLayer pointer that still
                                // belongs to the previous zone, causing reveals to go into a
                                // layer the renderer ignores (blank map).
                                if (levelChanged)
                                {
                                    Console.WriteLine("Level " + levelNo + ": Waiting " + LEVEL_CHANGE_STABILIZE_MS + "ms for game to stabilize...");
                                    Thread.Sleep(LEVEL_CHANGE_STABILIZE_MS);

                                    // Re-read the automap layer AFTER the stabilization delay.
                                    // The old value was likely stale (from the previous zone).
                                    pAutomapLayer = Read((IntPtr)((long)d2Client + AutomapLayerVar));
                                }

                                if (pAutomapLayer != 0)
                                {
                                    // BUG FIX: Validate the AutomapLayer is actually fresh.
                                    // Read the layer's nLayerNo field and check that the layer
                                    // has valid floor/wall cell pointers. A stale/freed layer
                                    // often has junk in nLayerNo or zeroed-out cell pointers.
                                    // If stale, retry a few times to give the game time to
                                    // allocate the new layer.
                                    if (levelChanged)
                                    {
                                        int layerNo = Read((IntPtr)(pAutomapLayer + AutomapLayer_nLayerNo));
                                        int retriesLeft = AUTOMAP_LAYER_RETRIES;

                                        // Heuristic: a valid automap layer for a level should have
                                        // a nLayerNo that is non-negative and reasonable (< 256).
                                        // Also, if the pointer hasn't changed from the old level's
                                        // layer, it's likely stale and the game hasn't swapped yet.
                                        while (retriesLeft > 0 && (pAutomapLayer == lastAutomapLayer || layerNo < 0 || layerNo > 255))
                                        {
                                            Console.WriteLine("  [WAIT] AutomapLayer appears stale (layerNo=" + layerNo + ", ptr=0x" + pAutomapLayer.ToString("X8") + "), retrying...");
                                            Thread.Sleep(AUTOMAP_LAYER_RETRY_DELAY_MS);
                                            pAutomapLayer = Read((IntPtr)((long)d2Client + AutomapLayerVar));
                                            if (pAutomapLayer != 0)
                                                layerNo = Read((IntPtr)(pAutomapLayer + AutomapLayer_nLayerNo));
                                            retriesLeft--;
                                        }

                                        if (pAutomapLayer == 0)
                                        {
                                            Console.WriteLine("Level " + levelNo + ": AutomapLayer still null after retries. Will retry next cycle.");
                                            // Don't update lastLevelNo so we retry next cycle
                                            Thread.Sleep(500);
                                            continue;
                                        }
                                    }

                                    // Log which layer triggered the reveal for diagnostics
                                    string reason = levelChanged ? "Level change" :
                                                    automapLayerChanged ? "Automap layer refreshed" :
                                                    "Periodic re-reveal";
                                    Console.WriteLine("Level " + levelNo + ": Revealing (" + reason + ", layer=0x" + pAutomapLayer.ToString("X8") + ")...");

                                    // Periodic re-reveals use light mode: only re-reveal rooms
                                    // already loaded by the game (skips AddRoomData entirely)
                                    bool isLightReveal = !levelChanged && !automapLayerChanged;
                                    RevealLevel(pLevel, addRoomAddr, removeRoomAddr, revealAddr, pAutomapLayer, isLightReveal);

                                    // BUG FIX: Post-reveal verification. After revealing, briefly
                                    // wait and re-read the automap layer pointer. If the game
                                    // swapped the layer out from under us during the reveal
                                    // (e.g. because we started revealing too early and the game
                                    // then replaced the layer), we need to re-reveal against the
                                    // new/correct layer. This catches the race condition where
                                    // the layer pointer is valid but about to be replaced.
                                    if (levelChanged)
                                    {
                                        Thread.Sleep(POST_REVEAL_VERIFY_DELAY_MS);
                                        int postRevealLayer = Read((IntPtr)((long)d2Client + AutomapLayerVar));
                                        if (postRevealLayer != 0 && postRevealLayer != pAutomapLayer)
                                        {
                                            // The game replaced the automap layer during/after our
                                            // reveal — our cells went into a stale layer. Re-reveal
                                            // against the new correct layer.
                                            Console.WriteLine("  [FIX] AutomapLayer changed post-reveal (0x" + pAutomapLayer.ToString("X8") + " -> 0x" + postRevealLayer.ToString("X8") + "). Re-revealing...");
                                            pAutomapLayer = postRevealLayer;
                                            RevealLevel(pLevel, addRoomAddr, removeRoomAddr, revealAddr, pAutomapLayer, false);
                                        }
                                    }

                                    lastLevelNo = levelNo;
                                    lastLevelPtr = pLevel;
                                    lastAutomapLayer = pAutomapLayer;
                                    lastRevealTick = Environment.TickCount;
                                }
                                else
                                {
                                    // Automap layer not initialized yet (player may not have Tab open)
                                    Console.WriteLine("Level " + levelNo + ": Waiting for automap (press Tab)...");
                                }
                            }
                            else if (pAutomapLayer != 0 && lastAutomapLayer == 0)
                            {
                                // First time seeing a valid automap layer — just start tracking it
                                lastAutomapLayer = pAutomapLayer;
                            }
                        }
                        else
                        {
                            // Player not in game — reset all tracking state
                            if (lastPlayerPtr != 0)
                            {
                                Console.WriteLine("[*] Player left game");
                            }
                            lastPlayerPtr = 0;
                            lastLevelPtr = 0;
                            lastLevelNo = -1;
                            lastAutomapLayer = 0;
                            lastRevealTick = Environment.TickCount;
                        }
                    }
                    catch (AccessViolationException avEx)
                    {
                        // Corrupted state exception — game memory may be in a bad state.
                        // Log and continue rather than crashing the tool.
                        Console.WriteLine("[ERR] Access violation: " + avEx.Message);
                        Console.WriteLine("[ERR] Resetting state — will re-reveal on next cycle.");
                        lastLevelNo = -1;
                        lastLevelPtr = 0;
                        lastAutomapLayer = 0;
                        Thread.Sleep(2000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERR] " + ex.Message);
                    }

                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("[FATAL] " + ex.ToString());
                Console.WriteLine();
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }
        }
    }
}
