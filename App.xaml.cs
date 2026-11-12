using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace Tricera360;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;

    internal static void ApplyDarkTitleBar(Window window)
    {
        int dark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Share the lock with earlier releases so only one bridge can run at a time.
        _instance = new Mutex(true, @"Local\DS5Windows.ControllerBridge", out _ownsInstance);
        if (!_ownsInstance)
        {
            MessageBox.Show("Tricera360 is already open. Check the Windows notification area.",
                "Tricera360", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagnosticLog.Session.Error(
            "UNHANDLED_EXCEPTION", $"terminating={args.IsTerminating}", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            DiagnosticLog.Session.Error("UNOBSERVED_TASK", error: args.Exception.GetBaseException());
        DiagnosticLog.Session.Info("SESSION_STARTED", $"version={typeof(App).Assembly.GetName().Version} os={Environment.OSVersion.Version}");
        if (e.Args is ["--test-outputs"])
        {
            Shutdown(OutputProbe.Run());
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance)
        {
            DiagnosticLog.Session.Info("SESSION_ENDED", $"exitCode={e.ApplicationExitCode}");
            _instance?.ReleaseMutex();
        }
        _instance?.Dispose();
        base.OnExit(e);
    }
}
