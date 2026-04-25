using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PD2MapReveal
{
    class Program
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        static void Main(string[] args)
        {
            uint pid = 0;
            Process[] processes = Process.GetProcessesByName("Game");
            if (processes.Length > 0) pid = (uint)processes[0].Id;
            else return;

            IntPtr hProcess = OpenProcess(0x10, false, pid);
            Process gameProc = Process.GetProcessById((int)pid);
            IntPtr d2Client = IntPtr.Zero;
            foreach (ProcessModule mod in gameProc.Modules) {
                if (mod.ModuleName.ToLower() == "d2client.dll") d2Client = mod.BaseAddress;
            }

            if (d2Client != IntPtr.Zero) {
                // Check 1.13c RefreshAutomap / DrawAutomap entry points
                ReadAndPrint(hProcess, (IntPtr)((long)d2Client + 0x630E0), 16, "Point A (630E0)");
                ReadAndPrint(hProcess, (IntPtr)((long)d2Client + 0x628C0), 16, "Point B (628C0)");
            }
            CloseHandle(hProcess);
            Thread.Sleep(5000);
        }

        static void ReadAndPrint(IntPtr hProcess, IntPtr addr, int size, string label) {
            byte[] b = new byte[size]; int r;
            if (ReadProcessMemory(hProcess, addr, b, size, out r)) {
                Console.Write(label + ": ");
                foreach (byte bb in b) Console.Write(bb.ToString("X2") + " ");
                Console.WriteLine();
            }
        }
    }
}
