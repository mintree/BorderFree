using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using BorderFree.Core.Geometry;
using BorderFree.Core.Hotkeys;
using BorderFree.Core.Layout;
using BorderFree.Core.Settings;
using BorderFree.Core.Startup;
using BorderFree.Platform;

namespace BorderFree.ViewModels;

public enum StatusTone
{
    Info,
    Success,
    Error
}

public sealed class MainViewModel : ViewModelBase
{
    public const int HotkeyNextId = 1;
    public const int HotkeyPreviousId = 2;
    public const int HotkeyReapplyId = 3;

    private readonly SettingsStore _store;
    private readonly WindowController _controller;
    private readonly AppSettings _settings;
    private readonly IStartupStore _startup;
    private readonly string? _executablePath;
    private readonly ObservableCollection<WindowRow> _windows = new();
    private readonly Dictionary<nint, ManagedWindow> _managed = new();
    private readonly HashSet<nint> _autoApplied = new();
    private readonly Dictionary<nint, DateTime> _autoApplyAfter = new();

    private DisplayInfo? _selectedMonitor;
    private WindowRow? _selectedWindow;
    private LayoutPreset _selectedPreset = LayoutPreset.Centered16x9;
    private string _filterText = string.Empty;
    private string _statusText = "Select a window, then a layout.";
    private StatusTone _statusTone = StatusTone.Info;
    private bool _isAdvancedOpen;
    private string? _recordingTarget;
    private bool _suppressProfileLoad;
    private bool _keepSelection;
    private nint _hotkeyWindow;
    private HotkeyGesture _next;
    private HotkeyGesture _previous;
    private HotkeyGesture _reapply;
    private string _size16x9 = "";
    private string _size21x9 = "";
    private string _sizeFull = "";
    private string _sizeLeft = "";
    private string _sizeRight = "";
    private string _customX = "0";
    private string _customY = "0";
    private string _customWidth = "";
    private string _customHeight = "";
    private bool _hasVisibleWindows;
    private bool _startWithWindows;

    public MainViewModel(
        SettingsStore store,
        WindowController controller,
        AppSettings settings,
        string? loadWarning,
        IStartupStore startup,
        string? executablePath)
    {
        _store = store;
        _controller = controller;
        _settings = settings;
        _startup = startup;
        _executablePath = executablePath;
        WindowsView = CollectionViewSource.GetDefaultView(_windows);
        WindowsView.Filter = FilterWindow;

        var repaired = false;
        _next = LoadGesture(settings.Hotkeys.NextPreset, "Ctrl+Alt+F11", ref repaired);
        _previous = LoadGesture(settings.Hotkeys.PreviousPreset, "Ctrl+Alt+F10", ref repaired);
        _reapply = LoadGesture(settings.Hotkeys.Reapply, "Ctrl+Alt+F12", ref repaired);
        if (repaired)
            SaveSettings();

        try
        {
            IsElevated = ProcessElevation.IsCurrentProcessElevated();
        }
        catch (PlatformNotSupportedException)
        {
            IsElevated = false;
        }

        RefreshCommand = new RelayCommand(Refresh);
        ApplyPresetCommand = new RelayCommand(parameter => ApplyPreset(ReadPreset(parameter)));
        ApplyCustomCommand = new RelayCommand(ApplyCustom);
        RestoreCommand = new RelayCommand(RestoreSelected, () => CanRestore);
        BeginRecordHotkeyCommand = new RelayCommand(parameter => BeginRecordHotkey(parameter as string));
        RestartElevatedCommand = new RelayCommand(RestartElevated);

        AlignStartupRegistration();

        if (!string.IsNullOrEmpty(loadWarning))
            SetStatus(loadWarning, StatusTone.Error);
    }

    public ObservableCollection<DisplayInfo> Monitors { get; } = new();

    public ICollectionView WindowsView { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ApplyPresetCommand { get; }

    public RelayCommand ApplyCustomCommand { get; }

    public RelayCommand RestoreCommand { get; }

    public RelayCommand BeginRecordHotkeyCommand { get; }

    public RelayCommand RestartElevatedCommand { get; }

    public bool IsElevated { get; }

    public bool ShowElevationPrompt => !IsElevated;

    public string SettingsPath => _store.FilePath;

    public bool NeedsWatch =>
        (_settings.RememberPerProgram && _settings.AutoApplyOnDetect) || _settings.SafeguardWrongMonitor;

    public DisplayInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (!Set(ref _selectedMonitor, value))
                return;

            if (value != null
                && !string.Equals(_settings.MonitorDeviceName, value.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.MonitorDeviceName = value.DeviceName;
                SaveSettings();
            }

            OnPropertyChanged(nameof(LayoutHeading));
            UpdatePresetSizes();
        }
    }

    public WindowRow? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            // Filtering the list asks the ListBox to clear a hidden selection. Keep it.
            if (_keepSelection && value == null && _selectedWindow != null)
                return;

            if (!Set(ref _selectedWindow, value))
                return;

            OnPropertyChanged(nameof(CanRestore));
            RestoreCommand.RaiseCanExecuteChanged();
            if (!_suppressProfileLoad)
                LoadRemembered(value);
        }
    }

    public LayoutPreset SelectedPreset
    {
        get => _selectedPreset;
        private set => Set(ref _selectedPreset, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
        if (!Set(ref _filterText, value))
            return;

        var selected = _selectedWindow;
        _keepSelection = true;
        _suppressProfileLoad = true;
        try
        {
            WindowsView.Refresh();
            if (selected != null && _windows.Contains(selected))
            {
                _selectedWindow = null;
                SelectedWindow = selected;
            }
        }
        finally
        {
            _keepSelection = false;
            _suppressProfileLoad = false;
        }

        OnPropertyChanged(nameof(HasFilterText));
        UpdateWindowListState();
        }
    }

    public bool HasFilterText => !string.IsNullOrWhiteSpace(FilterText);

    public bool HasVisibleWindows
    {
        get => _hasVisibleWindows;
        private set => Set(ref _hasVisibleWindows, value);
    }

    public bool ShowWindowListMessage => !HasVisibleWindows;

    public string WindowListMessage =>
        _windows.Count == 0
            ? "No open windows to place. Start a program, then refresh."
            : "No windows match that filter.";

    public string LayoutHeading =>
        SelectedMonitor == null ? "Layout" : $"Layout on {SelectedMonitor.ShortLabel}";

    public string Size16x9 => _size16x9;

    public string Size21x9 => _size21x9;

    public string SizeFull => _sizeFull;

    public string SizeLeft => _sizeLeft;

    public string SizeRight => _sizeRight;

    public string HotkeySummary =>
        $"{_next.ToSettingString()} next   ·   {_previous.ToSettingString()} previous   ·   {_reapply.ToSettingString()} reapply";

    public string NextHotkeyLabel => _recordingTarget == "next" ? "Press keys…" : _next.ToSettingString();

    public string PreviousHotkeyLabel => _recordingTarget == "previous" ? "Press keys…" : _previous.ToSettingString();

    public string ReapplyHotkeyLabel => _recordingTarget == "reapply" ? "Press keys…" : _reapply.ToSettingString();

    public bool IsRecordingHotkey => _recordingTarget != null;

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public StatusTone StatusTone
    {
        get => _statusTone;
        private set => Set(ref _statusTone, value);
    }

    public bool IsAdvancedOpen
    {
        get => _isAdvancedOpen;
        set => Set(ref _isAdvancedOpen, value);
    }

    public bool RememberPerProgram
    {
        get => _settings.RememberPerProgram;
        set
        {
            if (_settings.RememberPerProgram == value)
                return;
            _settings.RememberPerProgram = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAutoApplyEnabled));
            OnPropertyChanged(nameof(NeedsWatch));
            SaveSettings();
        }
    }

    public bool IsAutoApplyEnabled => RememberPerProgram;

    public bool AutoApplyOnDetect
    {
        get => _settings.AutoApplyOnDetect;
        set
        {
            if (_settings.AutoApplyOnDetect == value)
                return;
            _settings.AutoApplyOnDetect = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsWatch));
            SaveSettings();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
                return;

            try
            {
                if (value)
                {
                    if (string.IsNullOrWhiteSpace(_executablePath))
                        throw new InvalidOperationException("BorderFree couldn't find its own program file.");
                    StartupLaunch.Enable(_startup, _executablePath);
                }
                else
                {
                    StartupLaunch.Disable(_startup);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SetStatus("Couldn't update the Windows startup entry. " + ex.Message, StatusTone.Error);
                OnPropertyChanged();
                return;
            }

            _startWithWindows = StartupLaunch.ReadToggle(_startup);
            _settings.StartWithWindows = _startWithWindows;
            OnPropertyChanged();
            SaveSettings();
            SetStatus(
                _startWithWindows
                    ? "BorderFree will start with Windows, minimized to the tray."
                    : "BorderFree will no longer start with Windows.",
                StatusTone.Info);
        }
    }

    public bool SafeguardWrongMonitor
    {
        get => _settings.SafeguardWrongMonitor;
        set
        {
            if (_settings.SafeguardWrongMonitor == value)
                return;
            _settings.SafeguardWrongMonitor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsWatch));
            SaveSettings();
        }
    }

    public bool UseWorkArea
    {
        get => _settings.UseWorkArea;
        set
        {
            if (_settings.UseWorkArea == value)
                return;
            _settings.UseWorkArea = value;
            OnPropertyChanged();
            UpdatePresetSizes();
            SaveSettings();
        }
    }

    public string CustomX
    {
        get => _customX;
        set => Set(ref _customX, value);
    }

    public string CustomY
    {
        get => _customY;
        set => Set(ref _customY, value);
    }

    public string CustomWidth
    {
        get => _customWidth;
        set => Set(ref _customWidth, value);
    }

    public string CustomHeight
    {
        get => _customHeight;
        set => Set(ref _customHeight, value);
    }

    public bool CanRestore => SelectedWindow != null && _controller.CanRestore(SelectedWindow.Handle);

    public void RefreshMonitors(IReadOnlyList<DisplayInfo> monitors)
    {
        var selectedName = SelectedMonitor?.DeviceName ?? _settings.MonitorDeviceName;
        Monitors.Clear();
        foreach (var monitor in monitors)
            Monitors.Add(monitor);

        var choice = MonitorSelection.Choose(monitors.Select(m => m.ToSnapshot()).ToList(), selectedName);
        SelectedMonitor = choice == null
            ? null
            : Monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, choice.DeviceName, StringComparison.OrdinalIgnoreCase));
        UpdatePresetSizes();
    }

    public void RefreshWindows(IReadOnlyList<WindowDetails> found)
    {
        var selectedHandle = SelectedWindow?.Handle ?? 0;
        _suppressProfileLoad = true;
        try
        {
            var incoming = found.Select(window => window.Handle).ToHashSet();
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (!incoming.Contains(_windows[i].Handle))
                    _windows.RemoveAt(i);
            }

            foreach (var details in found)
            {
                var existing = FindRow(details.Handle);
                if (existing == null)
                    _windows.Add(WindowRow.From(details));
                else
                    existing.Update(details);
            }

            for (int target = 0; target < found.Count; target++)
            {
                int current = IndexOfHandle(found[target].Handle);
                if (current >= 0 && current != target)
                    _windows.Move(current, target);
            }

            SelectedWindow = selectedHandle == 0 ? null : FindRow(selectedHandle);
        }
        finally
        {
            _suppressProfileLoad = false;
        }

        WindowsView.Refresh();
        UpdateWindowListState();
        OnPropertyChanged(nameof(CanRestore));
        RestoreCommand.RaiseCanExecuteChanged();
    }

    public void AttachHotkeyWindow(nint hwnd)
    {
        _hotkeyWindow = hwnd;
        if (!TryRegisterAll(out var error) && !string.IsNullOrEmpty(error))
            SetStatus(error, StatusTone.Error);
    }

    public void DetachHotkeys()
    {
        UnregisterAll();
        _hotkeyWindow = 0;
    }

    public void OnHotkey(int id)
    {
        try
        {
            RefreshMonitors(DisplayCatalog.ListMonitors());
            var target = ResolveHotkeyTarget();
            if (target == null)
            {
                SetStatus("No window to move. Select one in BorderFree, or focus a program and press the shortcut again.", StatusTone.Error);
                return;
            }

            if (id == HotkeyReapplyId)
            {
                Reapply(target);
                return;
            }

            var current = CurrentPresetFor(target);
            var next = id == HotkeyPreviousId ? PresetCycle.Previous(current) : PresetCycle.Next(current);
            if (!TryMonitorFor(target, out var monitor, out var error))
            {
                SetStatus(error, StatusTone.Error);
                return;
            }

            var rect = PlacementPlanner.ForPreset(next, monitor.ToSnapshot(), _settings.UseWorkArea);
            Commit(target, next, custom: null, monitor, rect, activate: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException or System.ComponentModel.Win32Exception)
        {
            SetStatus(ex.Message, StatusTone.Error);
        }
    }

    public void OnWatchTick()
    {
        if (!NeedsWatch)
            return;

        try
        {
            var monitors = DisplayCatalog.ListMonitors();
            if (_settings.RememberPerProgram && _settings.AutoApplyOnDetect)
            {
                var windows = WindowCatalog.ListVisibleWindows();
                AutoApply(windows, monitors);
            }

            if (_settings.SafeguardWrongMonitor)
                CorrectStrays(monitors);

            _controller.ForgetClosed();
            DropClosedHandles();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            SetStatus(ex.Message, StatusTone.Error);
        }
    }

    public void BeginRecordHotkey(string? target)
    {
        if (target is not ("next" or "previous" or "reapply"))
            return;

        if (_recordingTarget == target)
        {
            CancelHotkeyRecording();
            return;
        }

        _recordingTarget = target;
        NotifyHotkeyLabels();
        SetStatus("Press a shortcut. Escape cancels.", StatusTone.Info);
    }

    public void CancelHotkeyRecording()
    {
        if (_recordingTarget == null)
            return;

        _recordingTarget = null;
        NotifyHotkeyLabels();
        SetStatus("Shortcut unchanged.", StatusTone.Info);
    }

    public void CommitHotkey(HotkeyGesture gesture)
    {
        var target = _recordingTarget;
        _recordingTarget = null;
        NotifyHotkeyLabels();
        if (target == null)
            return;

        if (!gesture.IsValid)
        {
            SetStatus("Use Ctrl, Alt, Shift, or the Windows key, plus a letter, number, or function key.", StatusTone.Error);
            return;
        }

        var previous = Gesture(target);
        Assign(target, gesture);
        if (HasDuplicateHotkey())
        {
            Assign(target, previous);
            NotifyHotkeyLabels();
            SetStatus("That shortcut is already used by another BorderFree hotkey.", StatusTone.Error);
            return;
        }

        if (!TryRegisterAll(out var error))
        {
            Assign(target, previous);
            TryRegisterAll(out _);
            NotifyHotkeyLabels();
            SetStatus(error ?? "Couldn't register that shortcut.", StatusTone.Error);
            return;
        }

        NotifyHotkeyLabels();
        SaveSettings();
        SetStatus($"Shortcut updated to {gesture.ToSettingString()}.", StatusTone.Success);
    }

    public void ReportFailure(string message) => SetStatus(message, StatusTone.Error);

    private void AlignStartupRegistration()
    {
        try
        {
            if (_settings.StartWithWindows && !string.IsNullOrWhiteSpace(_executablePath))
                StartupLaunch.Reconcile(_startup, startWithWindows: true, _executablePath);

            _startWithWindows = StartupLaunch.ReadToggle(_startup);
            if (_settings.StartWithWindows == _startWithWindows)
                return;

            _settings.StartWithWindows = _startWithWindows;
            SaveSettings();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _startWithWindows = false;
            SetStatus("Couldn't read the Windows startup entry. " + ex.Message, StatusTone.Error);
        }
    }

    private void Refresh()
    {
        try
        {
            RefreshMonitors(DisplayCatalog.ListMonitors());
            RefreshWindows(WindowCatalog.ListVisibleWindows());
            if (_windows.Count == 0)
                SetStatus("No open windows to place.", StatusTone.Info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus(ex.Message, StatusTone.Error);
        }
    }

    private void ApplyPreset(LayoutPreset preset)
    {
        if (preset == LayoutPreset.Custom)
            return;
        if (!TrySelectedTarget(out var target, out var monitor, out var error))
        {
            SetStatus(error, StatusTone.Error);
            return;
        }

        try
        {
            var rect = PlacementPlanner.ForPreset(preset, monitor.ToSnapshot(), _settings.UseWorkArea);
            Commit(target, preset, custom: null, monitor, rect, activate: true);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            SetStatus(ex.Message, StatusTone.Error);
        }
    }

    private void ApplyCustom()
    {
        if (!TrySelectedTarget(out var target, out var monitor, out var error))
        {
            SetStatus(error, StatusTone.Error);
            return;
        }

        if (!TryParseRect(out var rect, out var parseError))
        {
            SetStatus(parseError, StatusTone.Error);
            return;
        }

        Commit(target, LayoutPreset.Custom, rect, monitor, rect, activate: true);
    }

    private void RestoreSelected()
    {
        if (SelectedWindow == null)
        {
            SetStatus("Choose a window first.", StatusTone.Error);
            return;
        }

        var outcome = _controller.Restore(SelectedWindow.Handle);
        if (!outcome.Success)
        {
            SetStatus(outcome.Message, StatusTone.Error);
            return;
        }

        _managed.Remove(SelectedWindow.Handle);
        OnPropertyChanged(nameof(CanRestore));
        RestoreCommand.RaiseCanExecuteChanged();
        SetStatus($"Restored the frame of {SelectedWindow.DisplayExe}.", StatusTone.Success);
    }

    private void Reapply(WindowDetails target)
    {
        if (_managed.TryGetValue(target.Handle, out var managed)
            && TryFindMonitor(managed.MonitorDeviceName, out var managedMonitor))
        {
            var rect = DesiredRect(managed, managedMonitor);
            Commit(target, managed.Preset, managed.Custom, managedMonitor, rect, activate: false);
            return;
        }

        if (!string.IsNullOrEmpty(target.ExecutableName))
        {
            var profile = ProfileMemory.Recall(_settings, target.ExecutableName);
            if (profile != null && TryMonitorFor(target, out var profileMonitor, out _))
            {
                var rect = PlacementPlanner.ForProfile(profile, profileMonitor.ToSnapshot(), _settings.UseWorkArea);
                var custom = profile.Preset == LayoutPreset.Custom ? rect : (PixelRect?)null;
                Commit(target, profile.Preset, custom, profileMonitor, rect, activate: false);
                return;
            }
        }

        var preset = SelectedWindow?.Handle == target.Handle && SelectedPreset != LayoutPreset.Custom
            ? SelectedPreset
            : LayoutPreset.Centered16x9;
        if (!TryMonitorFor(target, out var monitor, out var error))
        {
            SetStatus(error, StatusTone.Error);
            return;
        }

        var fallback = PlacementPlanner.ForPreset(preset, monitor.ToSnapshot(), _settings.UseWorkArea);
        Commit(target, preset, custom: null, monitor, fallback, activate: false);
    }

    private void Commit(
        WindowDetails target,
        LayoutPreset preset,
        PixelRect? custom,
        DisplayInfo monitor,
        PixelRect rect,
        bool activate)
    {
        var outcome = _controller.ApplyBorderless(target.Handle, rect, activate);
        OnPropertyChanged(nameof(CanRestore));
        RestoreCommand.RaiseCanExecuteChanged();
        if (!outcome.Success)
        {
            SetStatus(outcome.Message, StatusTone.Error);
            return;
        }

        _managed[target.Handle] = new ManagedWindow
        {
            ExecutableName = target.ExecutableName,
            Preset = preset,
            Custom = preset == LayoutPreset.Custom ? rect : custom,
            MonitorDeviceName = monitor.DeviceName
        };
        _autoApplied.Add(target.Handle);
        _autoApplyAfter.Remove(target.Handle);
        SelectedPreset = preset;
        WriteCustomFields(rect);

        if (!string.IsNullOrEmpty(target.ExecutableName))
        {
            ProfileMemory.Remember(_settings, target.ExecutableName, new ProgramProfile
            {
                Preset = preset,
                Custom = preset == LayoutPreset.Custom ? rect : null,
                MonitorDeviceName = monitor.DeviceName
            });
            SaveSettings();
        }

        var row = FindRow(target.Handle);
        if (row != null && SelectedWindow?.Handle != row.Handle)
        {
            _suppressProfileLoad = true;
            try
            {
                SelectedWindow = row;
            }
            finally
            {
                _suppressProfileLoad = false;
            }
        }

        var name = string.IsNullOrEmpty(target.ExecutableName) ? "the window" : target.ExecutableName;
        SetStatus(
            $"Applied {LayoutPresetInfo.DisplayName(preset)} to {name}. {rect.Width}×{rect.Height} at {rect.X}, {rect.Y}.",
            StatusTone.Success);
    }

    private void AutoApply(IReadOnlyList<WindowDetails> windows, IReadOnlyList<DisplayInfo> monitors)
    {
        var now = DateTime.UtcNow;
        foreach (var window in windows)
        {
            if (_autoApplied.Contains(window.Handle))
                continue;
            if (string.IsNullOrEmpty(window.ExecutableName))
                continue;
            if (!AutoApplyPolicy.IsCandidateWindow(window.Bounds))
                continue;
            if (_autoApplyAfter.TryGetValue(window.Handle, out var notBefore) && notBefore > now)
                continue;

            var profile = ProfileMemory.Recall(_settings, window.ExecutableName);
            if (profile == null)
                continue;

            var device = profile.MonitorDeviceName ?? _settings.MonitorDeviceName;
            var choice = MonitorSelection.Choose(monitors.Select(m => m.ToSnapshot()).ToList(), device);
            var monitor = choice == null
                ? null
                : monitors.FirstOrDefault(m =>
                    string.Equals(m.DeviceName, choice.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (monitor == null)
                continue;

            PixelRect rect;
            try
            {
                rect = PlacementPlanner.ForProfile(profile, monitor.ToSnapshot(), _settings.UseWorkArea);
            }
            catch (InvalidOperationException)
            {
                _autoApplyAfter[window.Handle] = now.AddSeconds(30);
                continue;
            }

            var outcome = _controller.ApplyBorderless(window.Handle, rect, activate: false);
            if (!outcome.Success)
            {
                _autoApplyAfter[window.Handle] = now.AddSeconds(10);
                SetStatus(outcome.Message, StatusTone.Error);
                continue;
            }

            _autoApplied.Add(window.Handle);
            _managed[window.Handle] = new ManagedWindow
            {
                ExecutableName = window.ExecutableName,
                Preset = profile.Preset,
                Custom = profile.Preset == LayoutPreset.Custom ? rect : profile.Custom,
                MonitorDeviceName = monitor.DeviceName
            };
            SetStatus($"Applied the remembered layout to {window.ExecutableName}.", StatusTone.Success);
        }
    }

    private void CorrectStrays(IReadOnlyList<DisplayInfo> monitors)
    {
        foreach (var pair in _managed.ToList())
        {
            if (!WindowCatalog.TryGetBounds(pair.Key, out _))
            {
                _managed.Remove(pair.Key);
                continue;
            }

            if (!TryFindExactMonitor(pair.Value.MonitorDeviceName, monitors, out var monitor))
                continue;

            var currentDevice = DisplayCatalog.DeviceNameFromWindow(pair.Key);
            if (!Safeguard.IsOnWrongMonitor(currentDevice, monitor.DeviceName))
                continue;

            var desired = DesiredRect(pair.Value, monitor);
            var outcome = _controller.ApplyBorderless(pair.Key, desired, activate: false);
            if (!outcome.Success)
            {
                SetStatus(outcome.Message, StatusTone.Error);
                continue;
            }

            if (!string.IsNullOrEmpty(pair.Value.ExecutableName))
                SetStatus($"Moved {pair.Value.ExecutableName} back to {monitor.ShortLabel}.", StatusTone.Info);
        }
    }

    private void LoadRemembered(WindowRow? row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.ExecutableName))
            return;

        var profile = ProfileMemory.Recall(_settings, row.ExecutableName);
        if (profile == null)
            return;

        if (!string.IsNullOrWhiteSpace(profile.MonitorDeviceName))
        {
            var monitor = Monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, profile.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (monitor != null)
                SelectedMonitor = monitor;
        }

        SelectedPreset = profile.Preset;
        if (profile.Preset == LayoutPreset.Custom && profile.Custom is { HasArea: true } custom)
            WriteCustomFields(custom);
        else if (SelectedMonitor != null && profile.Preset != LayoutPreset.Custom)
        {
            try
            {
                WriteCustomFields(PlacementPlanner.ForPreset(profile.Preset, SelectedMonitor.ToSnapshot(), _settings.UseWorkArea));
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        SetStatus($"Remembered {LayoutPresetInfo.DisplayName(profile.Preset)} for {row.DisplayExe}. Click the layout to apply it.", StatusTone.Info);
    }

    private WindowDetails? ResolveHotkeyTarget()
    {
        var foreground = WindowCatalog.FromForeground(WindowCatalog.ForegroundHandle(), _hotkeyWindow);
        if (foreground != null)
            return foreground;

        if (SelectedWindow == null)
            return null;

        return WindowCatalog.ListVisibleWindows().FirstOrDefault(window => window.Handle == SelectedWindow.Handle);
    }

    private LayoutPreset CurrentPresetFor(WindowDetails target)
    {
        if (_managed.TryGetValue(target.Handle, out var managed))
            return managed.Preset;

        if (!string.IsNullOrEmpty(target.ExecutableName))
        {
            var profile = ProfileMemory.Recall(_settings, target.ExecutableName);
            if (profile != null)
                return profile.Preset;
        }

        if (SelectedWindow?.Handle == target.Handle)
            return SelectedPreset;

        return LayoutPreset.Custom;
    }

    private bool TrySelectedTarget(out WindowDetails target, out DisplayInfo monitor, out string error)
    {
        target = null!;
        monitor = null!;
        if (SelectedWindow == null)
        {
            error = "Choose a window first.";
            return false;
        }

        var details = WindowCatalog.ListVisibleWindows().FirstOrDefault(window => window.Handle == SelectedWindow.Handle);
        if (details == null)
        {
            error = "That window is no longer open. Refresh the list and choose it again.";
            return false;
        }

        if (SelectedMonitor == null)
        {
            error = "No display was found.";
            return false;
        }

        target = details;
        monitor = SelectedMonitor;
        error = string.Empty;
        return true;
    }

    private bool TryMonitorFor(WindowDetails target, out DisplayInfo monitor, out string error)
    {
        string? device = null;
        if (_managed.TryGetValue(target.Handle, out var managed))
            device = managed.MonitorDeviceName;
        else if (!string.IsNullOrEmpty(target.ExecutableName))
            device = ProfileMemory.Recall(_settings, target.ExecutableName)?.MonitorDeviceName;

        device ??= _settings.MonitorDeviceName ?? SelectedMonitor?.DeviceName;
        if (TryFindMonitor(device, out monitor))
        {
            error = string.Empty;
            return true;
        }

        error = "No display was found.";
        return false;
    }

    private bool TryFindMonitor(string? deviceName, out DisplayInfo monitor) =>
        TryFindMonitor(deviceName, Monitors, out monitor);

    private static bool TryFindExactMonitor(string deviceName, IReadOnlyList<DisplayInfo> monitors, out DisplayInfo monitor)
    {
        var found = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        if (found == null)
        {
            monitor = null!;
            return false;
        }

        monitor = found;
        return true;
    }

    private static bool TryFindMonitor(string? deviceName, IReadOnlyList<DisplayInfo> monitors, out DisplayInfo monitor)
    {
        var choice = MonitorSelection.Choose(monitors.Select(m => m.ToSnapshot()).ToList(), deviceName);
        if (choice == null)
        {
            monitor = null!;
            return false;
        }

        var found = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, choice.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (found == null)
        {
            monitor = null!;
            return false;
        }

        monitor = found;
        return true;
    }

    private PixelRect DesiredRect(ManagedWindow managed, DisplayInfo monitor)
    {
        if (managed.Preset == LayoutPreset.Custom && managed.Custom is { HasArea: true } custom)
            return custom;

        return PlacementPlanner.ForPreset(managed.Preset, monitor.ToSnapshot(), _settings.UseWorkArea);
    }

    private bool TryParseRect(out PixelRect rect, out string error)
    {
        rect = default;
        if (!TryParseCoordinate(CustomX, out var x) || !TryParseCoordinate(CustomY, out var y))
        {
            error = "X and Y have to be whole numbers, from -100000 to 100000.";
            return false;
        }

        if (!TryParseSize(CustomWidth, out var width) || !TryParseSize(CustomHeight, out var height))
        {
            error = "Width and height have to be whole numbers from 1 to 16384.";
            return false;
        }

        rect = new PixelRect(x, y, width, height);
        error = string.Empty;
        return true;
    }

    private static bool TryParseCoordinate(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value is >= -100000 and <= 100000;

    private static bool TryParseSize(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value is >= 1 and <= 16384;

    private void WriteCustomFields(PixelRect rect)
    {
        CustomX = rect.X.ToString(CultureInfo.InvariantCulture);
        CustomY = rect.Y.ToString(CultureInfo.InvariantCulture);
        CustomWidth = rect.Width.ToString(CultureInfo.InvariantCulture);
        CustomHeight = rect.Height.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdatePresetSizes()
    {
        _size16x9 = Describe(LayoutPreset.Centered16x9);
        _size21x9 = Describe(LayoutPreset.Centered21x9);
        _sizeFull = Describe(LayoutPreset.Full32x9);
        _sizeLeft = Describe(LayoutPreset.LeftHalf);
        _sizeRight = Describe(LayoutPreset.RightHalf);
        OnPropertyChanged(nameof(Size16x9));
        OnPropertyChanged(nameof(Size21x9));
        OnPropertyChanged(nameof(SizeFull));
        OnPropertyChanged(nameof(SizeLeft));
        OnPropertyChanged(nameof(SizeRight));
    }

    private string Describe(LayoutPreset preset)
    {
        if (SelectedMonitor == null)
            return "No display";

        try
        {
            var rect = PlacementPlanner.ForPreset(preset, SelectedMonitor.ToSnapshot(), _settings.UseWorkArea);
            return $"{rect.Width}×{rect.Height}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "—";
        }
    }

    private void UpdateWindowListState()
    {
        HasVisibleWindows = WindowsView.Cast<object>().Any();
        OnPropertyChanged(nameof(ShowWindowListMessage));
        OnPropertyChanged(nameof(WindowListMessage));
    }

    private bool FilterWindow(object item)
    {
        if (item is not WindowRow row)
            return false;
        if (string.IsNullOrWhiteSpace(FilterText))
            return true;

        var filter = FilterText.Trim();
        return row.ExecutableName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.DisplayExe.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void DropClosedHandles()
    {
        foreach (var hwnd in _autoApplied.ToList())
        {
            if (!WindowCatalog.TryGetBounds(hwnd, out _))
                _autoApplied.Remove(hwnd);
        }

        foreach (var hwnd in _autoApplyAfter.Keys.ToList())
        {
            if (!WindowCatalog.TryGetBounds(hwnd, out _))
                _autoApplyAfter.Remove(hwnd);
        }
    }

    private bool TryRegisterAll(out string? error)
    {
        error = null;
        UnregisterAll();
        if (_hotkeyWindow == 0)
            return true;

        var gestures = new (int Id, HotkeyGesture Gesture)[]
        {
            (HotkeyNextId, _next),
            (HotkeyPreviousId, _previous),
            (HotkeyReapplyId, _reapply)
        };

        var ok = true;
        foreach (var (id, gesture) in gestures)
        {
            if (!HotkeyRegistrar.TryRegister(_hotkeyWindow, id, gesture, out var registrationError))
            {
                ok = false;
                error ??= registrationError;
            }
        }

        return ok;
    }

    private void UnregisterAll()
    {
        HotkeyRegistrar.Unregister(_hotkeyWindow, HotkeyNextId);
        HotkeyRegistrar.Unregister(_hotkeyWindow, HotkeyPreviousId);
        HotkeyRegistrar.Unregister(_hotkeyWindow, HotkeyReapplyId);
    }

    private void RestartElevated()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            SetStatus("BorderFree couldn't find its own program file.", StatusTone.Error);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas"
            });
            System.Windows.Application.Current?.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SetStatus("Administrator approval was canceled.", StatusTone.Info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Couldn't restart as administrator. " + ex.Message, StatusTone.Error);
        }
    }

    private void SaveSettings()
    {
        _settings.Hotkeys.NextPreset = _next.ToSettingString();
        _settings.Hotkeys.PreviousPreset = _previous.ToSettingString();
        _settings.Hotkeys.Reapply = _reapply.ToSettingString();
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Couldn't save settings. " + ex.Message, StatusTone.Error);
        }
    }

    private void SetStatus(string message, StatusTone tone)
    {
        StatusText = message;
        StatusTone = tone;
    }

    private void NotifyHotkeyLabels()
    {
        OnPropertyChanged(nameof(NextHotkeyLabel));
        OnPropertyChanged(nameof(PreviousHotkeyLabel));
        OnPropertyChanged(nameof(ReapplyHotkeyLabel));
        OnPropertyChanged(nameof(HotkeySummary));
        OnPropertyChanged(nameof(IsRecordingHotkey));
    }

    private bool HasDuplicateHotkey()
    {
        var labels = new[] { _next.ToSettingString(), _previous.ToSettingString(), _reapply.ToSettingString() };
        return labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Length;
    }

    private HotkeyGesture Gesture(string target) => target switch
    {
        "next" => _next,
        "previous" => _previous,
        "reapply" => _reapply,
        _ => default
    };

    private void Assign(string target, HotkeyGesture gesture)
    {
        switch (target)
        {
            case "next":
                _next = gesture;
                break;
            case "previous":
                _previous = gesture;
                break;
            case "reapply":
                _reapply = gesture;
                break;
        }
    }

    private WindowRow? FindRow(nint handle)
    {
        foreach (var row in _windows)
        {
            if (row.Handle == handle)
                return row;
        }

        return null;
    }

    private int IndexOfHandle(nint handle)
    {
        for (int i = 0; i < _windows.Count; i++)
        {
            if (_windows[i].Handle == handle)
                return i;
        }

        return -1;
    }

    private static LayoutPreset ReadPreset(object? parameter) =>
        parameter is LayoutPreset preset ? preset : LayoutPreset.Centered16x9;

    private static HotkeyGesture LoadGesture(string? text, string fallback, ref bool repaired)
    {
        if (HotkeyGesture.TryParse(text, out var gesture))
            return gesture;

        repaired = true;
        HotkeyGesture.TryParse(fallback, out gesture);
        return gesture;
    }

    private sealed class ManagedWindow
    {
        public required string ExecutableName { get; init; }

        public required LayoutPreset Preset { get; init; }

        public PixelRect? Custom { get; init; }

        public required string MonitorDeviceName { get; init; }
    }
}
