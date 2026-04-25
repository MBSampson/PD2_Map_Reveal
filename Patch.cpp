#include "Patch.h"
#include <map>

std::vector<Patch *> Patch::Patches;

static std::map<Dll, std::string> DllNames = {
    {D2CLIENT, "D2Client.dll"}, {D2COMMON, "D2Common.dll"},
    {D2GFX, "D2Gfx.dll"},       {D2LANG, "D2Lang.dll"},
    {D2WIN, "D2Win.dll"},       {D2NET, "D2Net.dll"},
    {D2GAME, "D2Game.dll"},     {D2LAUNCH, "D2Launch.dll"},
    {FOG, "Fog.dll"},           {BNCLIENT, "BnClient.dll"},
    {STORM, "Storm.dll"},       {D2CMP, "D2Cmp.dll"},
    {D2MULTI, "D2Multi.dll"},   {D2MCPCLIENT, "D2McpClient.dll"},
    {D2SOUND, "D2Sound.dll"}};

int Patch::GetDllOffset(Dll dll, int offset) {
  if (offset < 0) {
    // Ordinal
    return (int)GetProcAddress(GetModuleHandleA(DllNames[dll].c_str()),
                               (LPCSTR)-offset);
  }

  DWORD base = (DWORD)GetModuleHandleA(DllNames[dll].c_str());
  if (!base)
    return 0;

  return base + offset;
}

bool Patch::WriteBytes(int address, int len, BYTE *bytes) {
  DWORD oldProtect;
  if (VirtualProtect((void *)address, len, PAGE_EXECUTE_READWRITE,
                     &oldProtect)) {
    memcpy((void *)address, bytes, len);
    VirtualProtect((void *)address, len, oldProtect, &oldProtect);
    return true;
  }
  return false;
}

Patch::Patch(PatchType type, Dll dll, Offsets offsets, int function, int length)
    : type(type), dll(dll), offsets(offsets), function(function),
      length(length), injected(false) {
  Patches.push_back(this);
}

bool Patch::Install() {
  // Basic patch installation logic
  return true;
}

bool Patch::Remove() { return true; }
