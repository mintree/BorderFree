using System.Threading;
using BorderFree.Core.Startup;

namespace BorderFree;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\BorderFree.SingleInstance";
    private const string ActivateName = @"Local\BorderFree.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private Thread? _activateThread;
    private volatile bool _exit;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var created = false;
        try
        {
            _mutex = new Mutex(true, MutexName, out created);
        }
        catch (AbandonedMutexException)
        {
            created = true;
            try
            {
                _mutex = Mutex.OpenExisting(MutexName);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // The crashed owner's handle is already gone. Continue as the only instance.
            }
        }

        if (!created)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        _activateThread = new Thread(WaitForActivate)
        {
            IsBackground = true,
            Name = "BorderFree.Activate"
        };
        _activateThread.Start();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var startHidden = LaunchArguments.ShouldStartHidden(
            LaunchArguments.RequestsMinimized(e.Args),
            startWithWindows: false);
        var window = new MainWindow(startHidden);
        MainWindow = window;
        window.Show();
        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _exit = true;
        try
        {
            _activate?.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        _activate?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void WaitForActivate()
    {
        while (!_exit)
        {
            try
            {
                _activate?.WaitOne();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_exit)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is global::BorderFree.MainWindow shell)
                    shell.ShowFromTray();
            });
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ActivateName);
            existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (MainWindow is global::BorderFree.MainWindow shell)
            shell.ReportFailure(e.Exception.Message);
    }
}
