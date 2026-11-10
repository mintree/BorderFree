using BorderFree.Platform;

namespace BorderFree.ViewModels;

public sealed class WindowRow : ViewModelBase
{
    private string _executableName = string.Empty;
    private string _executablePath = string.Empty;
    private string _title = string.Empty;

    private WindowRow(nint handle) => Handle = handle;

    public nint Handle { get; }

    public string ExecutableName => _executableName;

    public string ExecutablePath => _executablePath;

    public string Title => _title;

    public string DisplayExe =>
        string.IsNullOrWhiteSpace(_executableName) ? "Unknown program" : _executableName;

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(_title) ? "Untitled window" : _title;

    public static WindowRow From(WindowDetails details)
    {
        var row = new WindowRow(details.Handle);
        row.CopyFrom(details);
        return row;
    }

    public void Update(WindowDetails details)
    {
        var exeChanged = !string.Equals(_executableName, details.ExecutableName, StringComparison.Ordinal);
        var pathChanged = !string.Equals(_executablePath, details.ExecutablePath, StringComparison.Ordinal);
        var titleChanged = !string.Equals(_title, details.Title, StringComparison.Ordinal);
        if (!exeChanged && !pathChanged && !titleChanged)
            return;

        CopyFrom(details);
        if (exeChanged)
        {
            OnPropertyChanged(nameof(ExecutableName));
            OnPropertyChanged(nameof(DisplayExe));
        }

        if (pathChanged)
            OnPropertyChanged(nameof(ExecutablePath));
        if (titleChanged)
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    private void CopyFrom(WindowDetails details)
    {
        _executableName = details.ExecutableName;
        _executablePath = details.ExecutablePath;
        _title = details.Title;
    }
}
