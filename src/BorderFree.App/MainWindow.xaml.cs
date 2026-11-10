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
    private nint _hwnd;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        var store = new SettingsStore(SettingsStore.DefaultFilePath);
        var loaded = store.Load();
        _viewModel = new MainViewModel(store, new WindowController(), loaded.Settings, loaded.Warning);
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
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void ReportFailure(string message) => _viewModel.ReportFailure(message);

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

    private void OnClosed(object? sender, EventArgs e)
    {
        _watch.Stop();
        _viewModel.DetachHotkeys();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
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
