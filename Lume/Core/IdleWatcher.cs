using System;
using System.Runtime.InteropServices;

namespace VividDesk.Core;

/// <summary>Сколько секунд прошло с последнего ввода пользователя (мышь/клавиатура).</summary>
public static class IdleWatcher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static int IdleSeconds()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return 0;
            uint ticks = (uint)Environment.TickCount;
            return (int)((ticks - info.dwTime) / 1000);
        }
        catch { return 0; }
    }
}
