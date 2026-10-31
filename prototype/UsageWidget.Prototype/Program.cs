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
        var app = new Application { Resources = Theme.Load() };
        if (Array.IndexOf(args, "--qa") is var i and >= 0)
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Startup += (_, _) => { Qa.Run(args.ElementAtOrDefault(i + 1) ?? "qa"); app.Shutdown(); };
            app.Run();
            return;
        }
        app.Run(new PrototypeWindow(startDocked: args.Contains("--docked")));
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}
