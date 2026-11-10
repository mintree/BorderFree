using System.Diagnostics;
using System.Threading;
using BorderFree.Core.Instances;
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
        AppLog.Write($"Startup: BorderFree {CurrentVersionText()} at {Environment.ProcessPath}, args [{string.Join(' ', e.Args)}].");

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

        if (!created && _mutex != null && TryReplaceStaleInstances(_mutex))
            created = true;

        if (!created)
        {
            AppLog.Write("Startup: another BorderFree is already running. Asking it to show its window and exiting.");
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

    /// <summary>
    /// An older copy (for example v1.1.0 left hidden by X) must not swallow the launch of a newer download.
    /// Close other BorderFree processes that are older or run from another path, then take the mutex.
    /// </summary>
    private static bool TryReplaceStaleInstances(Mutex mutex)
    {
        var currentPath = Environment.ProcessPath;
        var currentVersion = ReadVersion(currentPath) ?? typeof(App).Assembly.GetName().Version;
        var killedAny = false;
        Process[] others;
        try
        {
            // Downloads keep their release name, so v1.1.1 runs as "BorderFree-v1.1.1-win-x64", not "BorderFree".
            // Match by name prefix, then confirm the product name from the file.
            int selfId = Environment.ProcessId;
            others = Process.GetProcesses()
                .Where(p =>
                {
                    bool match = p.Id != selfId && p.ProcessName.StartsWith("BorderFree", StringComparison.OrdinalIgnoreCase);
                    if (!match)
                        p.Dispose();
                    return match;
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Startup: couldn't list processes: {ex.Message}");
            return false;
        }

        foreach (var other in others)
        {
            using (other)
            {
                string? otherPath = null;
                try
                {
                    otherPath = other.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    AppLog.Write($"Startup: can't read the path of PID {other.Id} ({ex.Message}).");
                }

                if (otherPath != null && !IsBorderFree(otherPath))
                {
                    AppLog.Write($"Startup: PID {other.Id} at {otherPath} isn't BorderFree, leaving it alone.");
                    continue;
                }

                var otherVersion = ReadVersion(otherPath);
                var action = StaleInstancePolicy.Decide(currentPath, currentVersion, otherPath, otherVersion);
                AppLog.Write($"Startup: found PID {other.Id} {otherVersion?.ToString() ?? "?"} at {otherPath ?? "?"} -> {action}.");
                if (action != ExistingInstanceAction.Replace)
                    continue;

                try
                {
                    other.Kill();
                    other.WaitForExit(3000);
                    killedAny = true;
                    AppLog.Write($"Startup: closed older instance PID {other.Id}.");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    AppLog.Write($"Startup: couldn't close PID {other.Id}: {ex.Message}");
                }
            }
        }

        if (!killedAny)
            return false;

        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(3)))
            {
                AppLog.Write("Startup: took over the single-instance lock.");
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            // The closed instance owned it; this thread owns it now.
            AppLog.Write("Startup: took over the abandoned single-instance lock.");
            return true;
        }

        AppLog.Write("Startup: the single-instance lock didn't free up within 3 s.");
        return false;
    }

    private static bool IsBorderFree(string path)
    {
        try
        {
            var product = FileVersionInfo.GetVersionInfo(path).ProductName;
            return string.IsNullOrEmpty(product) || string.Equals(product, "BorderFree", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static Version? ReadVersion(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart != 0 || info.FileMinorPart != 0 || info.FileBuildPart != 0)
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            return StaleInstancePolicy.ParseVersion(info.FileVersion);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string CurrentVersionText() =>
        ReadVersion(Environment.ProcessPath)?.ToString()
        ?? typeof(App).Assembly.GetName().Version?.ToString()
        ?? "unknown";

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
