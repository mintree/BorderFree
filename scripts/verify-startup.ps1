# Confirms the BorderFree Run key, then launches that exact command and
# checks that the process is up with no visible main window.
# Prints PASS or FAIL lines. Exit code 0 means every check passed.

$ErrorActionPreference = "Continue"
$failed = $false

function Write-Result([string]$status, [string]$message) {
    Write-Output "$status`: $message"
}

$name = "BorderFree"
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$property = Get-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue
$command = $null
if ($null -ne $property) {
    $command = $property.$name
}

if ([string]::IsNullOrWhiteSpace($command)) {
    Write-Result "FAIL" "Run value BorderFree is missing"
    exit 1
}
Write-Result "PASS" "Run value BorderFree is present"

if ($command -notmatch '^"([^"]+)"\s+--minimized\s*$') {
    Write-Result "FAIL" "command is not a quoted path plus --minimized ($command)"
    exit 1
}

$exe = $Matches[1]
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Result "FAIL" "path does not exist: $exe"
    exit 1
}
Write-Result "PASS" "path exists: $exe"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BorderFreeWindowProbe {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
}
"@

$before = @(Get-Process -Name "BorderFree" -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
Start-Process -FilePath $exe -ArgumentList "--minimized"
$deadline = (Get-Date).AddSeconds(8)
$proc = $null
do {
    Start-Sleep -Milliseconds 400
    $proc = Get-Process -Name "BorderFree" -ErrorAction SilentlyContinue |
        Where-Object { $before -notcontains $_.Id } |
        Select-Object -First 1
} while ($null -eq $proc -and (Get-Date) -lt $deadline)

if ($null -eq $proc) {
    Write-Result "FAIL" "BorderFree process is not running"
    exit 1
}
Write-Result "PASS" "process $($proc.Id) is running"

$script:visibleMain = $false
$script:targetPid = [uint32]$proc.Id
$callback = {
    param($hWnd, $lParam)
    $processId = [uint32]0
    [void][BorderFreeWindowProbe]::GetWindowThreadProcessId($hWnd, [ref]$processId)
    if ($processId -eq $script:targetPid -and [BorderFreeWindowProbe]::IsWindowVisible($hWnd)) {
        $builder = New-Object System.Text.StringBuilder 512
        [void][BorderFreeWindowProbe]::GetWindowText($hWnd, $builder, $builder.Capacity)
        if ($builder.ToString() -eq "BorderFree") {
            $script:visibleMain = $true
        }
    }
    return $true
}
[void][BorderFreeWindowProbe]::EnumWindows($callback, [IntPtr]::Zero)

if ($script:visibleMain) {
    Write-Result "FAIL" "main window is visible"
    $failed = $true
} else {
    Write-Result "PASS" "no visible main window"
}

if ($failed) { exit 1 }
exit 0
