using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using BorderFree.Core.Hotkeys;
using BorderFree.Core.Settings;
using BorderFree.Platform;
using BorderFree.ViewModels;

namespace BorderFree;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _watch;
    private readonly bool _startHidden;
    private TrayIconHost? _tray;
    private nint _hwnd;
    private bool _exitRequested;
    private bool _trayHintShown;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public MainWindow(bool startHidden = false)
    {
        _startHidden = startHidden;
        if (startHidden)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            Opacity = 0;
        }

        InitializeComponent();

        var store = new SettingsStore(SettingsStore.DefaultFilePath);
        var loaded = store.Load();
        _viewModel = new MainViewModel(
            store,
            new WindowController(),
            loaded.Settings,
            loaded.Warning,
            new StartupRegistration(),
            Environment.ProcessPath);
        DataContext = _viewModel;

        try
        {
            _viewModel.RefreshMonitors(DisplayCatalog.ListMonitors());
            _viewModel.RefreshWindows(WindowCatalog.ListVisibleWindows());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportFailure(ex.Message);
        }

        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watch.Tick += (_, _) =>
        {
            if (_viewModel.NeedsWatch)
                _viewModel.OnWatchTick();
        };

        SourceInitialized += OnSourceInitialized;
        Activated += OnActivated;
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void ReportFailure(string message) => _viewModel.ReportFailure(message);

    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Opacity = 1;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    public void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource?)PresentationSource.FromVisual(this);
        if (source == null)
            return;

        _hwnd = source.Handle;
        source.AddHook(WndProc);
        WindowController.TryUseDarkTitleBar(_hwnd);
        _viewModel.AttachHotkeyWindow(_hwnd);
        _watch.Start();
        try
        {
            _tray ??= new TrayIconHost(
                _hwnd,
                () => Dispatcher.BeginInvoke(ShowFromTray),
                () => Dispatcher.BeginInvoke(() => _viewModel.OnHotkey(MainViewModel.HotkeyReapplyId)),
                () => Dispatcher.BeginInvoke(ExitApplication));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tray: setup threw {ex.GetType().Name}: {ex.Message}");
            _tray = null;
        }

        if (_tray != null)
            _tray.Readded += OnTrayReadded;

        AppLog.Write($"Tray available: {TrayAvailable}.");

        if (_startHidden)
        {
            if (TrayAvailable)
            {
                AppLog.Write("Start hidden: hiding to the tray.");
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                // No tray icon to come back from, so stay reachable on the taskbar.
                AppLog.Write("Start hidden: no tray icon, starting minimized to the taskbar instead.");
                Opacity = 1;
                ShowInTaskbar = true;
                WindowState = WindowState.Minimized;
            }
        }
    }

    private bool TrayAvailable => _tray?.IsAdded == true;

    private void OnTrayReadded(bool added)
    {
        if (added || IsVisible)
            return;

        // Explorer restarted and the icon couldn't come back. Don't leave the app hidden with no way in.
        AppLog.Write("Tray: re-add failed while hidden, showing minimized on the taskbar.");
        Opacity = 1;
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
        Show();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastRefreshUtc < TimeSpan.FromSeconds(1))
            return;

        _lastRefreshUtc = DateTime.UtcNow;
        try
        {
            _viewModel.RefreshMonitors(DisplayCatalog.ListMonitors());
            _viewModel.RefreshWindows(WindowCatalog.ListVisibleWindows());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportFailure(ex.Message);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
            return;

        // Closing never quits; Exit on the tray menu does. Only hide when there is a tray icon to come back from.
        e.Cancel = true;
        if (TrayAvailable)
        {
            AppLog.Write("Close: hiding to the tray.");
            Hide();
            ShowInTaskbar = false;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray!.ShowBalloon(
                    "BorderFree is still running",
                    "BorderFree is still running. Find it in the tray (click ^ if hidden). Right-click > Exit to quit.");
            }
        }
        else
        {
            AppLog.Write("Close: no tray icon, minimizing to the taskbar instead of hiding.");
            Opacity = 1;
            ShowInTaskbar = true;
            WindowState = WindowState.Minimized;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _watch.Stop();
        _viewModel.DetachHotkeys();
        _tray?.Dispose();
        _tray = null;
        System.Windows.Application.Current?.Shutdown();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_tray?.HandleMessage(msg, lParam) == true)
        {
            handled = true;
            return 0;
        }

        const int wmHotkey = 0x0312;
        if (msg == wmHotkey)
        {
            handled = true;
            int id = wParam.ToInt32();
            Dispatcher.BeginInvoke(() => _viewModel.OnHotkey(id));
        }

        return 0;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsRecordingHotkey)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _viewModel.CancelHotkeyRecording();
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
            return;

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
            modifiers |= HotkeyModifiers.Windows;

        var name = MapKey(key);
        e.Handled = true;
        if (name == null)
        {
            _viewModel.ReportFailure("That key can't be used as a shortcut.");
            return;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            _viewModel.ReportFailure("Add Ctrl, Alt, Shift, or the Windows key.");
            return;
        }

        _viewModel.CommitHotkey(new HotkeyGesture(modifiers, name));
    }

    private static string? MapKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();
        if (key >= Key.D0 && key <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return "Num" + (key - Key.NumPad0);
        if (key >= Key.F1 && key <= Key.F24)
            return key.ToString();

        return key switch
        {
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Tab => "Tab",
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            _ => null
        };
    }
}
