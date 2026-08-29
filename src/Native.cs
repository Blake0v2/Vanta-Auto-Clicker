using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Vanta
{
    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public INPUTUNION data; }
        [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate; }
        internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern int GetMessage(out MSG message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG message);
        [DllImport("user32.dll")] internal static extern IntPtr DispatchMessage(ref MSG message);
        [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string module);
        [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint milliseconds);
    }

    public sealed class WindowsClickOutput : IClickOutput
    {
        public IntPtr ProtectedWindow { get; set; }
        // Tests can restrict all injected clicks to a dedicated, owned test window.
        public IntPtr RequiredWindow { get; set; }

        public void MoveTo(SequencePoint point)
        {
            int left = NativeMethods.GetSystemMetrics(76), top = NativeMethods.GetSystemMetrics(77);
            int width = NativeMethods.GetSystemMetrics(78), height = NativeMethods.GetSystemMetrics(79);
            if (point.X < left || point.Y < top || point.X >= left + width || point.Y >= top + height)
                throw new InvalidOperationException("A sequence point is outside the current desktop. Recapture it with F6.");
            if (!NativeMethods.SetCursorPos(point.X, point.Y)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not move the cursor.");
        }

        public void Press(ClickButton button)
        {
            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point)) throw new InvalidOperationException("Windows could not read the cursor position.");
            IntPtr root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), 2);
            if (ProtectedWindow != IntPtr.Zero && root == ProtectedWindow) throw new InvalidOperationException("Cursor is over Vanta. Move it to your target and start again.");
            if (RequiredWindow != IntPtr.Zero && root != RequiredWindow) throw new InvalidOperationException("Test stopped because the cursor left the test window.");
            Send(button, false);
        }

        public void Release(ClickButton button) { Send(button, true); }

        private static void Send(ClickButton button, bool up)
        {
            uint flag = button == ClickButton.Left ? (up ? 0x4u : 0x2u) : button == ClickButton.Right ? (up ? 0x10u : 0x8u) : (up ? 0x40u : 0x20u);
            var input = new NativeMethods.INPUT { type = 0, data = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = flag, dwExtraInfo = new UIntPtr(0x56414E54) } } };
            if (NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.INPUT))) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked mouse input. Check the target app's permissions.");
        }
    }

    public enum HotkeyAction { None, Activate, Deactivate, EmergencyStop, CapturePoint }

    // Kept independent of the native hook so press/release and modifier behavior is testable.
    public sealed class HotkeyState
    {
        private readonly HashSet<int> pressed = new HashSet<int>();
        private readonly HashSet<int> swallowed = new HashSet<int>();
        private bool active;
        private int activeKey;
        private HotkeyModifiers activeModifiers;
        public void Seed(int key) { pressed.Add(key); }
        public void Reset() { pressed.Clear(); active = false; }

        public HotkeyAction Process(int key, bool down, int binding, HotkeyModifiers modifiers, bool running, bool suspended, bool captureEnabled, out bool suppress)
        {
            bool repeat = down && pressed.Contains(key);
            if (down) pressed.Add(key); else pressed.Remove(key);
            suppress = !down && swallowed.Remove(key);
            HotkeyModifiers held = Modifiers;
            if (active && (!pressed.Contains(activeKey) || (held & activeModifiers) != activeModifiers))
            {
                active = false;
                return HotkeyAction.Deactivate;
            }
            if (suspended || !down) return HotkeyAction.None;
            if (repeat) { suppress = swallowed.Contains(key); return HotkeyAction.None; }
            HotkeyAction action = HotkeyAction.None;
            if (key == 0x1B && running) action = HotkeyAction.EmergencyStop;
            else if (key == 0x75 && captureEnabled && held == HotkeyModifiers.None) action = HotkeyAction.CapturePoint;
            else if (key == binding && held == modifiers)
            {
                active = true;
                activeKey = key;
                activeModifiers = modifiers;
                action = HotkeyAction.Activate;
            }
            if (action != HotkeyAction.None) { swallowed.Add(key); suppress = true; }
            return action;
        }

        public HotkeyModifiers Modifiers
        {
            get
            {
                HotkeyModifiers result = HotkeyModifiers.None;
                if (pressed.Contains(0x11) || pressed.Contains(0xA2) || pressed.Contains(0xA3)) result |= HotkeyModifiers.Control;
                if (pressed.Contains(0x12) || pressed.Contains(0xA4) || pressed.Contains(0xA5)) result |= HotkeyModifiers.Alt;
                if (pressed.Contains(0x10) || pressed.Contains(0xA0) || pressed.Contains(0xA1)) result |= HotkeyModifiers.Shift;
                if (pressed.Contains(0x5B) || pressed.Contains(0x5C)) result |= HotkeyModifiers.Windows;
                return result;
            }
        }
    }

    public sealed class GlobalHotkeys : IDisposable
    {
        private readonly NativeMethods.HookProc callback;
        private readonly HotkeyState state = new HotkeyState();
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Thread thread;
        private IntPtr hook;
        private uint threadId;
        private Exception error;
        private volatile int binding = 0x77;
        private volatile HotkeyModifiers modifiers;
        public volatile bool Suspended;
        public volatile bool Running;
        public volatile bool CaptureEnabled;
        public event Action<HotkeyAction> ActionReceived;
        public GlobalHotkeys()
        {
            callback = Callback;
            thread = new Thread(Loop) { IsBackground = true, Name = "Vanta global hotkeys" };
            thread.Start();
            if (!ready.WaitOne(5000)) throw new InvalidOperationException("Global hotkeys did not initialize.");
            if (error != null) throw error;
        }

        public void Configure(int key, HotkeyModifiers mods) { binding = key; modifiers = mods; }

        private void Loop()
        {
            try
            {
                threadId = NativeMethods.GetCurrentThreadId();
                for (int key = 8; key < 256; key++) if (NativeMethods.GetAsyncKeyState(key) < 0) state.Seed(key);
                hook = NativeMethods.SetWindowsHookEx(13, callback, NativeMethods.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Global hotkeys could not be registered. Restart Vanta before clicking.");
            }
            catch (Exception ex) { error = ex; }
            finally { ready.Set(); }
            if (error != null) return;
            try
            {
                NativeMethods.MSG message;
                while (NativeMethods.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                {
                    NativeMethods.TranslateMessage(ref message);
                    NativeMethods.DispatchMessage(ref message);
                }
            }
            finally { if (hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(hook); }
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                if ((data.flags & 0x10) == 0) // Ignore injected keyboard events, including other automation.
                {
                    int message = wParam.ToInt32();
                    if (message == 0x100 || message == 0x104 || message == 0x101 || message == 0x105)
                    {
                        bool suppress;
                        var action = state.Process((int)data.vkCode, message == 0x100 || message == 0x104, binding, modifiers, Running, Suspended, CaptureEnabled, out suppress);
                        var handler = ActionReceived;
                        if (action != HotkeyAction.None && handler != null) handler(action);
                        if (suppress) return new IntPtr(1);
                    }
                }
            }
            return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (thread.IsAlive)
            {
                NativeMethods.PostThreadMessage(threadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
                if (!thread.Join(2000)) return;
            }
            ready.Dispose();
        }
    }
}
