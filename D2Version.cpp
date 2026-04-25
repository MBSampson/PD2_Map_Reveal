#include "D2Version.h"
#include <Shlwapi.h>
#include <windows.h>


#pragma comment(lib, "shlwapi.lib")

namespace D2Version {
VersionID versionID = VERSION_113c;

VersionID GetGameVersionID() { return versionID; }

void Init() {
  // For PD2, we mostly target 1.13c
  versionID = VERSION_113c;
}

std::string GetGameVersionString() { return "1.13c"; }

std::string GetHumanReadableVersion() { return "1.13c"; }
} // namespace D2Version
