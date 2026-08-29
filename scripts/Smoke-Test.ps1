param([int]$ExistingProcessId = 0, [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = if ($ExecutablePath) { (Resolve-Path -LiteralPath $ExecutablePath).Path } else { Join-Path $root 'dist\Vanta Auto Clicker.exe' }
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class VantaSmokeWindows {
    private delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static IntPtr Find(int processId) {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr unused) {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner != processId) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, 256);
            var kind = new StringBuilder(256); GetClassName(window, kind, 256);
            if (title.ToString() == "Vanta Auto Clicker" && kind.ToString().StartsWith("HwndWrapper")) result = window;
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
if ($ExistingProcessId -gt 0) {
    $appProcess = Get-Process -Id $ExistingProcessId
    if ($appProcess.Path -ne (Resolve-Path -LiteralPath $exe).Path) { throw 'Process is not the expected Vanta executable.' }
} else {
    $appProcess = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
}
if (-not $appProcess.WaitForInputIdle(5000)) { throw 'Packaged app did not become ready.' }
$processHandle = $appProcess.Handle
$window = [VantaSmokeWindows]::Find($appProcess.Id)
$timeout = [System.Diagnostics.Stopwatch]::StartNew()
while ($window -eq [IntPtr]::Zero -and $timeout.ElapsedMilliseconds -lt 5000 -and -not $appProcess.HasExited) {
    Start-Sleep -Milliseconds 50
    $window = [VantaSmokeWindows]::Find($appProcess.Id)
}
if ($window -eq [IntPtr]::Zero) { throw 'Expected WPF main window was not found.' }
if (-not [VantaSmokeWindows]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)) { throw 'Could not request a graceful close.' }
if (-not $appProcess.WaitForExit(5000)) { throw 'Packaged app did not shut down cleanly.' }
if ($null -eq $appProcess.ExitCode) { throw 'The process exit code could not be read.' }
if ($appProcess.ExitCode -ne 0) { throw "Packaged app failed with exit code $($appProcess.ExitCode)." }
Write-Output 'PASS: packaged EXE created its actual WPF window and closed with exit code 0.'
