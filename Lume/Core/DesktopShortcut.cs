using System.Diagnostics;
using System.IO;

namespace VividDesk.Core;

/// <summary>Ярлык «Lume» на рабочем столе — создаётся автоматически, если его нет.</summary>
public static class DesktopShortcut
{
    public static void EnsureCreated()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, "Lume.lnk");
            if (File.Exists(lnk)) return;

            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

            // стандартный способ создания .lnk — COM-объект WScript.Shell
            Type shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.Description = "Lume — живые обои для Windows";
            shortcut.IconLocation = exe + ",0";
            shortcut.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("shortcut: " + ex.Message);
        }
    }
}
