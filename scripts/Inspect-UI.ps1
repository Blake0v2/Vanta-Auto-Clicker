param([string]$ExecutablePath = (Join-Path $PSScriptRoot '..\dist\Vanta Auto Clicker.exe'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
if (Get-Process -Name 'Vanta Auto Clicker' -ErrorAction SilentlyContinue) { throw 'Close Vanta before UI inspection so your current session is not interrupted.' }
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,PresentationFramework,WindowsBase,System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class VantaUiInspection {
    public struct Rect { public int Left, Top, Right, Bottom; }
    public struct Point { public int X, Y; public Point(int x, int y) { X=x; Y=y; } }
    private delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public static IntPtr Find(int processId, string titlePart) {
        IntPtr result = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr unused) {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner != processId) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, 256);
            var kind = new StringBuilder(256); GetClassName(window, kind, 256);
            if (title.ToString().Contains(titlePart) && kind.ToString().StartsWith("HwndWrapper")) result = window;
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

function Wait-Window([int]$ProcessId, [string]$TitlePart) {
    $timeout = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $handle = [VantaUiInspection]::Find($ProcessId,$TitlePart)
        if ($handle -ne [IntPtr]::Zero) { return $handle }
        Start-Sleep -Milliseconds 50
    } while ($timeout.ElapsedMilliseconds -lt 5000)
    throw "Window not found: $TitlePart"
}

function Find-Control($Parent, [string]$Name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name)
    $element = $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
    if ($null -eq $element) { throw "Control not found: $Name" }
    return $element
}

function Wait-Layout([IntPtr]$Handle) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $previous = ''
    $stable = 0
    do {
        $rect = [VantaUiInspection+Rect]::new()
        [VantaUiInspection]::GetWindowRect($Handle,[ref]$rect) | Out-Null
        $size = '{0},{1},{2},{3}' -f $rect.Left,$rect.Top,$rect.Right,$rect.Bottom
        if ($size -eq $previous) { $stable++ } else { $stable = 0 }
        $previous = $size
        if ($stable -ge 5) { return }
        Start-Sleep -Milliseconds 50
    } while ($timer.ElapsedMilliseconds -lt 4000)
    throw 'Window layout did not settle after its transition.'
}

function Inspect-Window([IntPtr]$Handle, [string]$Name) {
    Wait-Layout $Handle
    if (([VantaUiInspection]::GetWindowLong($Handle,-20) -band 0x80000) -eq 0) { throw 'The native window is not per-pixel transparent.' }
    $rect = [VantaUiInspection+Rect]::new()
    [VantaUiInspection]::GetWindowRect($Handle,[ref]$rect) | Out-Null
    $scale = [VantaUiInspection]::GetDpiForWindow($Handle) / 96.0
    # A neutral backdrop makes the real transparent corners visible without
    # capturing any other application's content. No click engine code is loaded.
    $backdrop.Left = ($rect.Left-10)/$scale
    $backdrop.Top = ($rect.Top-10)/$scale
    $backdrop.Width = ($rect.Right-$rect.Left+20)/$scale
    $backdrop.Height = ($rect.Bottom-$rect.Top+20)/$scale
    $backdrop.Show()
    [VantaUiInspection]::SetWindowPos($Handle,[IntPtr](-1),0,0,0,0,0x43) | Out-Null
    [VantaUiInspection]::ShowWindow($Handle,5) | Out-Null
    [VantaUiInspection]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 350
    $points = @(
        [VantaUiInspection+Point]::new($rect.Left+2,$rect.Top+2),
        [VantaUiInspection+Point]::new($rect.Right-3,$rect.Top+2),
        [VantaUiInspection+Point]::new($rect.Left+2,$rect.Bottom-3),
        [VantaUiInspection+Point]::new($rect.Right-3,$rect.Bottom-3)
    )
    foreach ($point in $points) {
        if ([VantaUiInspection]::GetAncestor([VantaUiInspection]::WindowFromPoint($point),2) -eq $Handle) { throw "Square native hit area remains in $Name." }
    }
    $edge = [VantaUiInspection+Point]::new($rect.Left+3,[int](($rect.Top+$rect.Bottom)/2))
    if ([VantaUiInspection]::GetAncestor([VantaUiInspection]::WindowFromPoint($edge),2) -ne $Handle) { throw "Expected opaque side of $Name to receive input." }
    $bitmap = [System.Drawing.Bitmap]::new($rect.Right-$rect.Left+20,$rect.Bottom-$rect.Top+20)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left-10,$rect.Top-10,0,0,$bitmap.Size)
        $bitmap.Save((Join-Path $artifacts "$Name.png"),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    Write-Output "PASS: $Name has four transparent native corners and an opaque body."
}

$appProcess = $null
$mainHandle = [IntPtr]::Zero
$backdrop = [System.Windows.Window]::new()
$backdrop.WindowStyle = [System.Windows.WindowStyle]::None
$backdrop.ResizeMode = [System.Windows.ResizeMode]::NoResize
$backdrop.ShowInTaskbar = $false
$backdrop.Topmost = $true
$backdrop.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#262626')
$oldDpi = [VantaUiInspection]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    $appProcess = Start-Process -FilePath (Resolve-Path -LiteralPath $ExecutablePath).Path -WindowStyle Hidden -PassThru
    $processHandle = $appProcess.Handle
    $mainHandle = Wait-Window $appProcess.Id 'Vanta Auto Clicker'
    [VantaUiInspection]::ShowWindow($mainHandle,5) | Out-Null
    Start-Sleep -Milliseconds 150
    $uiRoot = [System.Windows.Automation.AutomationElement]::FromHandle($mainHandle)
    $navigation = Find-Control $uiRoot 'View'
    $selection = $navigation.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    $originalView = $selection[0].Current.Name
    $defaultTab = Find-Control $navigation 'Default'
    $defaultTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-Layout $mainHandle
    $advancedTab = Find-Control $navigation 'Advanced'
    $heights = [System.Collections.Generic.List[int]]::new()
    $advancedTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $motionTimer = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $motionRect = [VantaUiInspection+Rect]::new()
        [VantaUiInspection]::GetWindowRect($mainHandle,[ref]$motionRect) | Out-Null
        $heights.Add($motionRect.Bottom-$motionRect.Top)
        Start-Sleep -Milliseconds 20
    } while ($motionTimer.ElapsedMilliseconds -lt 550)
    $distinctHeights = @($heights | Sort-Object -Unique)
    if ([System.Windows.SystemParameters]::ClientAreaAnimation -and -not [System.Windows.SystemParameters]::HighContrast) {
        if ($distinctHeights.Count -lt 3) { throw 'Expected intermediate sizes during the smooth window transition.' }
        Write-Output "PASS: window transition rendered $($distinctHeights.Count) different heights."
    } else { Write-Output 'PASS: window transition respects the Windows reduced-motion setting.' }
    $heights | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'transition-heights.json')
    foreach ($tabName in @('Settings','Default','Advanced','Default','Settings','Default')) {
        (Find-Control $navigation $tabName).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 25
    }
    Wait-Layout $mainHandle
    if ((Find-Control $uiRoot 'Rate or delay amount').Current.IsOffscreen) { throw 'Rapid view changes did not settle on the last selected view.' }
    Write-Output 'PASS: rapid view switching settles on the final selection.'
    $brand = Find-Control $uiRoot 'VANTA'
    $textPattern = $null
    if ($brand.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern,[ref]$textPattern)) {
        $fontName = $textPattern.DocumentRange.GetAttributeValue([System.Windows.Automation.TextPattern]::FontNameAttribute)
        if ([string]$fontName -notmatch 'Paytone One') { throw "Unexpected application font: $fontName" }
        Write-Output 'PASS: the live application exposes Paytone One as its text font.'
    }
    foreach ($view in @('Default','Advanced','Settings')) {
        $item = Find-Control $navigation $view
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 200
        Inspect-Window $mainHandle ('vanta-' + $view.ToLowerInvariant())
    }
    $testButton = Find-Control $uiRoot 'Open test pad'
    $testButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $padHandle = Wait-Window $appProcess.Id 'Test pad'
    Inspect-Window $padHandle 'vanta-test-pad'
    [VantaUiInspection]::PostMessage($padHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    $settingsTab = Find-Control $navigation 'Settings'
    $settingsTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-Layout $mainHandle
    (Find-Control $uiRoot 'Font license').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $licenseHandle = Wait-Window $appProcess.Id 'Font license'
    $licenseRoot = [System.Windows.Automation.AutomationElement]::FromHandle($licenseHandle)
    $licenseText = Find-Control $licenseRoot 'Paytone One license text'
    $licenseValue = $licenseText.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($licenseValue -notmatch 'SIL OPEN FONT LICENSE' -or $licenseValue -notmatch 'Paytone Project Authors') { throw 'The bundled font license is not readable in the standalone app.' }
    Inspect-Window $licenseHandle 'vanta-font-license'
    [VantaUiInspection]::PostMessage($licenseHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 150
    Write-Output 'PASS: the font license is accessible from the standalone executable.'
    [VantaUiInspection]::ShowWindow($mainHandle,6) | Out-Null
    Start-Sleep -Milliseconds 100
    [VantaUiInspection]::ShowWindow($mainHandle,9) | Out-Null
    Start-Sleep -Milliseconds 150
    Inspect-Window $mainHandle 'vanta-restored'
    $original = Find-Control $navigation $originalView
    $original.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Wait-Layout $mainHandle
    Write-Output 'PASS: view switching and the test pad work through the real app UI; original view restored.'
} finally {
    if ($mainHandle -ne [IntPtr]::Zero) { [VantaUiInspection]::PostMessage($mainHandle,0x10,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null }
    if ($appProcess -and -not $appProcess.HasExited) { $appProcess.WaitForExit(5000) | Out-Null }
    $backdrop.Close()
    [VantaUiInspection]::SetThreadDpiAwarenessContext($oldDpi) | Out-Null
}
if ($appProcess.ExitCode -ne 0) { throw 'The inspected app did not exit successfully.' }
