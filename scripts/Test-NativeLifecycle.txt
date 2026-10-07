#requires -Version 7.2
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\PhantomFudge.App\bin\Release\net10.0-windows\PhantomFudge.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the native preview first.' }
if (Get-Process -Name PhantomFudge -ErrorAction SilentlyContinue) {
    throw 'A PhantomFudge process is already running. This test will not touch an existing instance.'
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PhantomLifecycleProbe {
    private delegate bool WindowCallback(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    public static IntPtr FindVisible(int processId) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, param) => {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == processId && IsWindowVisible(hwnd)) { result = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

function Start-OwnInstance {
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.WorkingDirectory = $root
    $info.Environment.Remove('PHANTOM_TRANSCRIPTION_TOKEN') | Out-Null
    return [Diagnostics.Process]::Start($info)
}

function Wait-Condition([scriptblock]$Condition, [string]$Failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw $Failure }
        Start-Sleep -Milliseconds 50
    }
}

$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
try {
    $first = Start-OwnInstance
    $owned.Add($first)
    $state = @{ Handle = [IntPtr]::Zero }
    Wait-Condition {
        $state.Handle = [PhantomLifecycleProbe]::FindVisible($first.Id)
        $state.Handle -ne [IntPtr]::Zero
    } 'The blank native preview did not show.'
    $handle = $state.Handle
    if (-not [PhantomLifecycleProbe]::PostMessage($handle, 0x112, [IntPtr]0xF020, [IntPtr]::Zero)) {
        throw 'Could not request minimize.'
    }
    Wait-Condition { [PhantomLifecycleProbe]::IsIconic($handle) } 'Minimize did not occur.'
    $second = Start-OwnInstance
    $owned.Add($second)
    if (-not $second.WaitForExit(10000) -or $second.ExitCode -ne 0) { throw 'Second-launch recovery failed.' }
    Wait-Condition {
        [PhantomLifecycleProbe]::IsWindowVisible($handle) -and -not [PhantomLifecycleProbe]::IsIconic($handle)
    } 'Second launch did not restore the minimized preview.'
    if (-not [PhantomLifecycleProbe]::PostMessage($handle, 0x312, [IntPtr]1, [IntPtr]::Zero)) {
        throw 'Could not invoke the registered hide command.'
    }
    Wait-Condition { -not [PhantomLifecycleProbe]::IsWindowVisible($handle) } 'Hide command did not hide.'
    $third = Start-OwnInstance
    $owned.Add($third)
    if (-not $third.WaitForExit(10000) -or $third.ExitCode -ne 0) { throw 'Hidden-window recovery failed.' }
    Wait-Condition { [PhantomLifecycleProbe]::IsWindowVisible($handle) } 'Second launch did not reveal the hidden preview.'
    if (-not [PhantomLifecycleProbe]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)) {
        throw 'Could not request native close.'
    }
    if (-not $first.WaitForExit(10000) -or $first.ExitCode -ne 0) {
        throw 'Native close did not complete cleanly.'
    }
    Write-Output 'PASS blank-window minimize/relaunch/hide/relaunch/native-close. No notes or audio captured.'
}
finally {
    foreach ($process in $owned) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        $process.Dispose()
    }
}
