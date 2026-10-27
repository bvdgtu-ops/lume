using System.Runtime.InteropServices;
using System.Text;

namespace VividDesk.Core;

/// <summary>Весь нативный WinAPI, нужный для крепления окон к рабочему столу и детекции полноэкранных приложений.</summary>
internal static class WinApi
{
    public const int GWL_STYLE = -16;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// Скругляет углы окна средствами DWM (Windows 11). На Windows 10 функция
    /// просто не применяется (атрибут не поддерживается) — окна остаются прямоугольными.
    /// </summary>
    public static void EnableRoundedCorners(System.Windows.Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}

/// <summary>
/// Слой рабочего стола за иконками. Windows прячет обои в Progman; сообщение 0x052C заставляет
/// explorer создать пару WorkerW: один держит иконки (SHELLDLL_DefView), второй (следующий
/// за ним в z-порядке) — слой для живых обоев. Возвращаем HWND именно второго.
/// </summary>
internal static class DesktopLayer
{
    private static IntPtr _workerW;

    /// <summary>Возвращает HWND слоя обоев (WorkerW за иконками; fallback — Progman).</summary>
    public static IntPtr Get()
    {
        if (_workerW != IntPtr.Zero && WinApi.IsWindow(_workerW))
            return _workerW;

        IntPtr progman = WinApi.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        // 0x052C: «создай WorkerW для анимированных обоев» — то же сообщение использует Wallpaper Engine/Lively
        WinApi.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        IntPtr holder = IntPtr.Zero; // окно, содержащее SHELLDLL_DefView (Progman либо первый WorkerW)
        WinApi.EnumWindows((top, _) =>
        {
            if (WinApi.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                holder = top;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        IntPtr workerw = IntPtr.Zero;
        if (holder != IntPtr.Zero)
        {
            // основной вариант структуры: WorkerW — top-level окно, СЛЕДУЮЩЕЕ за держателем DefView
            workerw = WinApi.FindWindowEx(IntPtr.Zero, holder, "WorkerW", null);
            // редкий вариант: WorkerW-сиблинг внутри того же родителя, после DefView
            if (workerw == IntPtr.Zero)
                workerw = WinApi.FindWindowEx(holder, WinApi.FindWindowEx(holder, IntPtr.Zero, "SHELLDLL_DefView", null), "WorkerW", null);
        }

        if (workerw == IntPtr.Zero)
            workerw = progman; // классический рабочий стол без WorkerW: иконки прямо в Progman

        _workerW = workerw;
        return workerw;
    }
}
