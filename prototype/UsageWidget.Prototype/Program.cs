// PROTOTYPE: throwaway. Three structurally different looks for the widget, on the real desktop with real Usage.
// Switch with ← / → (click the widget first) or the right-click menu.
using System.Windows;

namespace UsageWidget.Prototype;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Our own taskbar identity, so the docked button doesn't group with other dotnet.exe apps.
        SetCurrentProcessExplicitAppUserModelID("ClaudeUsageWidget.Prototype");
        new Application().Run(new PrototypeWindow(startDocked: args.Contains("--docked")));
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}
