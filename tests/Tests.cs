using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Vanta
{
    public static class Tests
    {
        private static int passed, failed;
        private static string artifacts;
        [STAThread]
        public static int Main(string[] args)
        {
            artifacts = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
            Directory.CreateDirectory(artifacts);
            if (args.Contains("--ui")) UiTests(); else UnitTests();
            Console.WriteLine("\n" + passed + " passed; " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }
        private static void Test(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS  " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL  " + name + "\n" + ex); }
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Near(double value, double expected) { Check(Math.Abs(value - expected) < 0.00001, value + " != " + expected); }
        private static ClickSettings Limited(int clicks)
        {
            return new ClickSettings { Amount = 100, DurationPercent = 0, LimitEnabled = true, LimitValue = clicks };
        }

        private static void UnitTests()
        {
            Test("Defaults are valid and never imply automatic start", () => { Check(new ClickSettings().Validate(true) == null, "Invalid defaults"); Check(new ClickSettings().HotkeyKey == 0x77, "Expected F8"); });
            Test("Rate and delay conversion in every unit", () =>
            {
                double[] scales = { 1, 1000, 60000, 3600000 };
                for (int i = 0; i < scales.Length; i++) { var s = new ClickSettings { Unit = (TimeUnit)i, Amount = 2 }; Near(s.IntervalMs, scales[i] / 2); s.Cadence = CadenceMode.Delay; Near(s.IntervalMs, scales[i] * 2); }
            });
            Test("Unsafe and non-finite numeric options rejected", () =>
            {
                foreach (double n in new[] { 0d, -1, Double.NaN, Double.PositiveInfinity, 1001 }) { var s = new ClickSettings { Amount = n }; Check(s.Validate(true) != null, "Accepted " + n); }
                Check(new ClickSettings { DurationPercent = 101 }.Validate(true) != null, "Invalid duration");
                Check(new ClickSettings { VariationPercent = 91 }.Validate(true) != null, "Invalid variation");
                Check(new ClickSettings { LimitEnabled = true, LimitValue = 1.5 }.Validate(true) != null, "Fractional clicks");
                Check(new ClickSettings { Cadence = (CadenceMode)9 }.Validate(true) != null, "Unknown enum");
            });
            Test("Sequence validation distinguishes saved from runnable", () => { var s = new ClickSettings { SequenceEnabled = true }; Check(s.Validate(false) == null, "May save an empty disabled run"); Check(s.Validate(true) != null, "Empty enabled sequence must not run"); s.Points.Add(new SequencePoint(-1920, 32)); Check(s.Validate(true) == null, "Negative multi-monitor coordinate is valid"); });
            Test("Hotkey reservations and modifier validation", () => { Check(!ClickSettings.ValidHotkey(0x1B, HotkeyModifiers.None), "Esc reserved"); Check(!ClickSettings.ValidHotkey(0x75, HotkeyModifiers.None), "F6 reserved"); Check(!ClickSettings.ValidHotkey(0x11, HotkeyModifiers.None), "Modifier alone rejected"); Check(ClickSettings.ValidHotkey(0x55, HotkeyModifiers.Control), "Ctrl U valid"); });
            Test("Settings deep-copy sequence points", () => { var s = new ClickSettings(); s.Points.Add(new SequencePoint(1, 2)); var copy = s.Copy(); s.Points[0].X = 5; Check(copy.Points[0].X == 1, "Shallow copy"); });
            Test("Variation stays within bounds, with a 1 ms floor", () => { Near(ClickEngine.VaryInterval(100, 30, 0), 70); Near(ClickEngine.VaryInterval(100, 30, 1), 130); Near(ClickEngine.VaryInterval(100, 0, 0.4), 100); Near(ClickEngine.VaryInterval(1, 90, 0), 1); });
            Test("Exact click limit and balanced press / release", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { e.Start(Limited(7), 0); Check(e.WaitForStop(2000), "Timeout"); Check(e.Clicks == 7 && o.Downs == 7 && o.Ups == 7 && !o.Held, "Click count mismatch"); Check(e.StopReason == "Click limit reached", "Reason"); } });
            Test("Odd click limit is exact in double click mode", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = Limited(3); s.DoubleClickEnabled = true; s.DoubleClickGapMs = 1; e.Start(s, 0); Check(e.WaitForStop(2000), "Timeout"); Check(e.Clicks == 3 && o.Downs == 3 && o.Ups == 3, "Double click overshot"); } });
            Test("Every mouse button is routed correctly", () => { foreach (ClickButton button in Enum.GetValues(typeof(ClickButton))) { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = Limited(2); s.Button = button; e.Start(s, 0); Check(e.WaitForStop(1000), "Timeout"); Check(o.Buttons.All(b => b == button), "Wrong button"); } } });
            Test("Sequence cycles through ordered points", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = Limited(5); s.SequenceEnabled = true; s.Points.Add(new SequencePoint(1, 1)); s.Points.Add(new SequencePoint(-2, 2)); e.Start(s, 0); Check(e.WaitForStop(2000), "Timeout"); Check(String.Join(",", o.Moves) == "1,-2,1,-2,1", "Wrong sequence order"); } });
            Test("Double click sequence advances once per pair", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = Limited(4); s.DoubleClickEnabled = true; s.DoubleClickGapMs = 1; s.SequenceEnabled = true; s.Points.Add(new SequencePoint(1, 1)); s.Points.Add(new SequencePoint(2, 2)); e.Start(s, 0); Check(e.WaitForStop(2000), "Timeout"); Check(String.Join(",", o.Moves) == "1,2", "Advanced between pair"); } });
            Test("Cancelling the start countdown sends no clicks", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { e.Start(Limited(5), 2000); e.Stop(); Check(e.WaitForStop(500), "Slow countdown cancellation"); Check(o.Downs == 0 && o.Ups == 0, "Clicked during countdown"); } });
            Test("Stop interrupts a long mouse-down and releases it", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = new ClickSettings { Cadence = CadenceMode.Delay, Amount = 60, DurationPercent = 100 }; e.Start(s, 0); Check(o.Pressed.WaitOne(1000), "Did not press"); e.Stop(); Check(e.WaitForStop(500), "Slow stop"); Check(o.Ups == 1 && !o.Held, "Button stuck"); } });
            Test("Stop interrupts a 24 hour interval", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = new ClickSettings { Cadence = CadenceMode.Delay, Unit = TimeUnit.Hours, Amount = 24, DurationPercent = 0 }; e.Start(s, 0); Check(o.Pressed.WaitOne(1000), "No press"); e.Stop(); Check(e.WaitForStop(500), "Slow interval cancellation"); } });
            Test("Time limit interrupts a long held click", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = new ClickSettings { Cadence = CadenceMode.Delay, Amount = 20, DurationPercent = 100, LimitEnabled = true, Limit = LimitMode.Seconds, LimitValue = 1 }; e.Start(s, 0); Check(e.WaitForStop(2500), "Time limit failed"); Check(e.ElapsedSeconds >= 0.95 && e.ElapsedSeconds < 2, "Time limit drift"); Check(o.Ups == 1 && !o.Held, "Time limit stuck button"); Check(e.StopReason == "Time limit reached", "Wrong reason"); } });
            Test("Injection errors are visible and stop the worker", () => { var o = new FakeOutput { ThrowOnPress = true }; using (var e = new ClickEngine(o)) { e.Start(Limited(2), 0); Check(e.WaitForStop(1000), "Timeout"); Check(e.LastError == "injection blocked" && e.Clicks == 0 && !e.IsRunning, "Error not reported"); } });
            Test("A failed release is retried in cleanup", () => { var o = new FakeOutput { FailReleaseOnce = true }; using (var e = new ClickEngine(o)) { e.Start(Limited(2), 0); Check(e.WaitForStop(1000), "Timeout"); Check(o.Ups == 1 && !o.Held && e.LastError != null, "Release cleanup failed"); } });
            Test("A run snapshots its settings", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { var s = Limited(3); e.Start(s, 70); s.LimitValue = 50; s.Button = ClickButton.Right; Check(e.WaitForStop(2000), "Timeout"); Check(e.Clicks == 3 && o.Buttons.All(b => b == ClickButton.Left), "Settings mutated live run"); } });
            Test("Rapid repeated runs reset counters and state", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { for (int i = 0; i < 20; i++) { e.Start(Limited(1), 0); Check(e.WaitForStop(1000), "Timeout"); Check(e.Clicks == 1, "Counter not reset"); } } Check(o.Downs == 20 && o.Ups == 20, "Leaked clicks"); });
            Test("Starting while running is rejected", () => { var o = new FakeOutput(); using (var e = new ClickEngine(o)) { e.Start(Limited(1), 1000); bool rejected = false; try { e.Start(Limited(1), 0); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Concurrent start allowed"); } });
            Test("Disposal stops and releases an active button", () => { var o = new FakeOutput(); var e = new ClickEngine(o); e.Start(new ClickSettings { Cadence = CadenceMode.Delay, Amount = 20, DurationPercent = 100 }, 0); Check(o.Pressed.WaitOne(1000), "No press"); e.Dispose(); Check(!e.IsRunning && !o.Held, "Dispose didn't stop"); e.Dispose(); });
            Test("Hotkey repeat is suppressed without retriggering", () => { var s = new HotkeyState(); bool suppress; Check(s.Process(0x77, true, 0x77, 0, false, false, false, out suppress) == HotkeyAction.Activate && suppress, "Start"); Check(s.Process(0x77, true, 0x77, 0, true, false, false, out suppress) == HotkeyAction.None && suppress, "Repeated activation"); Check(s.Process(0x77, false, 0x77, 0, true, false, false, out suppress) == HotkeyAction.Deactivate && suppress, "Missing release"); });
            Test("Hold stops when any required modifier is released", () => { var s = new HotkeyState(); bool suppress; s.Process(0xA2, true, 0x55, HotkeyModifiers.Control, false, false, false, out suppress); Check(s.Process(0x55, true, 0x55, HotkeyModifiers.Control, false, false, false, out suppress) == HotkeyAction.Activate, "Ctrl U failed"); Check(s.Process(0xA2, false, 0x55, HotkeyModifiers.Control, true, false, false, out suppress) == HotkeyAction.Deactivate, "Modifier release didn't stop"); Check(!suppress, "Modifier key-up must reach target"); });
            Test("Unmatched chords and unrelated keys pass through", () => { var s = new HotkeyState(); bool suppress; s.Process(0xA0, true, 0x77, 0, false, false, false, out suppress); Check(s.Process(0x77, true, 0x77, 0, false, false, false, out suppress) == HotkeyAction.None && !suppress, "Wrong modifiers matched"); });
            Test("Esc only intercepts an active run", () => { var s = new HotkeyState(); bool suppress; Check(s.Process(0x1B, true, 0x77, 0, false, false, false, out suppress) == HotkeyAction.None && !suppress, "Stole idle Esc"); s.Process(0x1B, false, 0x77, 0, false, false, false, out suppress); Check(s.Process(0x1B, true, 0x77, 0, true, false, false, out suppress) == HotkeyAction.EmergencyStop && suppress, "Emergency stop missing"); });
            Test("F6 capture only intercepts when enabled", () => { var s = new HotkeyState(); bool suppress; Check(s.Process(0x75, true, 0x77, 0, false, false, false, out suppress) == HotkeyAction.None, "Captured outside sequence"); s.Process(0x75, false, 0x77, 0, false, false, false, out suppress); Check(s.Process(0x75, true, 0x77, 0, false, false, true, out suppress) == HotkeyAction.CapturePoint, "F6 failed"); });
            Test("Hotkeys are suspended during shortcut entry and dialogs", () => { var s = new HotkeyState(); bool suppress; Check(s.Process(0x77, true, 0x77, 0, false, true, false, out suppress) == HotkeyAction.None && !suppress, "Shortcut capture starts clicking"); });
            Test("Settings round trip atomically, keeping a backup", () => { string path = Path.Combine(artifacts, "roundtrip.xml"); var s = Limited(73); s.SequenceEnabled = true; s.Points.Add(new SequencePoint(-40, 50)); s.HotkeyMods = HotkeyModifiers.Alt; s.HotkeyKey = 0x51; SettingsStore.Save(path, s); s.Amount = 45; SettingsStore.Save(path, s); string warning; var loaded = SettingsStore.Load(path, out warning); Check(warning == null && loaded.Amount == 45 && loaded.Points[0].X == -40 && loaded.HotkeyKey == 0x51 && loaded.HotkeyMods == HotkeyModifiers.Alt && File.Exists(path + ".bak"), "Round trip mismatch"); });
            Test("Corrupt or hostile settings are not executed", () => { string path = Path.Combine(artifacts, "invalid.xml"); File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY xxe SYSTEM 'file:///not-read'>]><ClickSettings>&xxe;</ClickSettings>"); string warning; var s = SettingsStore.Load(path, out warning); Check(warning != null && s.HotkeyKey == 0x77, "DTD not rejected"); });
            Test("View model numeric validation rejects invalid text", () => { var model = new ViewModel(new ClickSettings()); ClickSettings s; string error; model.AmountText = "NaN"; Check(!model.TryRead(true, out s, out error), "Accepted NaN"); model.AmountText = "25"; Check(model.TryRead(true, out s, out error) && s.Amount == 25, "Valid amount rejected"); });
            Test("Updater accepts current and legacy GitHub version tags", () =>
            {
                Version version;
                Check(UpdateService.TryParseVersion("v1.2.3", out version) && version == new Version(1, 2, 3), "Current tag rejected");
                Check(UpdateService.TryParseVersion("v.4.5.6", out version) && version == new Version(4, 5, 6), "Legacy tag rejected");
                Check(!UpdateService.TryParseVersion("v1.2.3-preview", out version), "Prerelease text accepted");
                Check(UpdateService.IsNewer(new Version(1, 0, 4), new Version(1, 0, 3, 9)), "Newer release missed");
                Check(!UpdateService.IsNewer(new Version(1, 0, 3), new Version(1, 0, 3, 9)), "Equal release treated as newer");
            });
            Test("Updater only accepts a valid GitHub SHA-256 digest", () =>
            {
                string hash = new String('a', 64);
                Check(UpdateService.NormalizeDigest("sha256:" + hash.ToUpperInvariant()) == hash, "Valid digest rejected");
                Check(UpdateService.NormalizeDigest("sha1:" + hash) == null, "Wrong algorithm accepted");
                Check(UpdateService.NormalizeDigest("sha256:abcd") == null, "Short digest accepted");
            });
            Test("Updater checksum parser requires the exact setup filename", () =>
            {
                string hash = new String('b', 64);
                string contents = new String('c', 64) + "  Other.exe\n" + hash + " *Vanta.Auto.Clicker.Setup.exe\n";
                Check(UpdateService.ParseChecksum(contents, "Vanta.Auto.Clicker.Setup.exe") == hash, "Setup checksum not found");
                Check(UpdateService.ParseChecksum(contents, "Vanta.AutoClicker.exe") == null, "Checksum from another asset accepted");
                Check(UpdateService.ParseChecksum("not-a-hash  Vanta.Auto.Clicker.Setup.exe", "Vanta.Auto.Clicker.Setup.exe") == null, "Malformed checksum accepted");
            });
            Test("Native INPUT structure has correct platform size", () => Check(Marshal.SizeOf(typeof(NativeMethods.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "INPUT packing wrong"));
        }

        private static void Pump(int milliseconds)
        {
            var until = Stopwatch.StartNew();
            do
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            } while (until.ElapsedMilliseconds < milliseconds);
        }

        private static void Snapshot(Window window, string name)
        {
            var target = Render(window, 1);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            using (var file = File.Create(Path.Combine(artifacts, name + ".png"))) encoder.Save(file);
        }

        private static void ShowView(MainController main, int index)
        {
            main.SetView(index);
            // Allow the outgoing fade and incoming layout animation to finish.
            Pump(450);
        }

        private static void CheckAppFonts(DependencyObject node)
        {
            var text = node as TextBlock;
            if (text != null && text.IsVisible && !String.IsNullOrEmpty(text.Text))
            {
                bool icon = text.Text.Length == 1 && text.Text[0] >= 0xE000 && text.Text[0] <= 0xF8FF;
                GlyphTypeface glyphs;
                Check(new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch).TryGetGlyphTypeface(out glyphs), "Font did not resolve for " + text.Text);
                Check(glyphs.FamilyNames.Values.Contains(icon ? "Segoe MDL2 Assets" : "Paytone One"), "Wrong rendered font for " + text.Text);
                if (icon) Check(glyphs.CharacterToGlyphMap.ContainsKey(text.Text[0]), "Missing icon glyph");
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) CheckAppFonts(VisualTreeHelper.GetChild(node, i));
        }

        private static RenderTargetBitmap Render(Window window, double scale)
        {
            window.UpdateLayout();
            var target = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            target.Render(window);
            return target;
        }

        private static void CheckRoundedCorners(Window window, double scale)
        {
            var image = Render(window, scale);
            int width = image.PixelWidth, height = image.PixelHeight;
            var pixels = new byte[width * height * 4];
            image.CopyPixels(pixels, width * 4, 0);
            foreach (var point in new[] { new Point(2, 2), new Point(width - 3, 2), new Point(2, height - 3), new Point(width - 3, height - 3) })
                Check(pixels[((int)point.Y * width + (int)point.X) * 4 + 3] == 0, "A square backing pixel remains at " + point);
            Check(pixels[((height / 2) * width + 2) * 4 + 3] == 255, "The window edge should remain opaque between corners");
        }

        private static void CheckNeutral(Brush brush, string location)
        {
            var solid = brush as SolidColorBrush;
            if (solid == null || solid.Color.A == 0) return;
            Check(solid.Color.R == solid.Color.G && solid.Color.G == solid.Color.B, "Tinted fill or text at " + location + ": " + solid.Color);
        }

        private static void CheckNeutralSurfaces(DependencyObject node)
        {
            var border = node as Border;
            if (border != null) CheckNeutral(border.Background, border.Name + " background");
            var control = node as Control;
            if (control != null) { CheckNeutral(control.Background, control.Name + " background"); CheckNeutral(control.Foreground, control.Name + " foreground"); }
            var text = node as TextBlock;
            if (text != null) CheckNeutral(text.Foreground, text.Text);
            var shape = node as System.Windows.Shapes.Shape;
            if (shape != null) CheckNeutral(shape.Fill, shape.Name + " fill");
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) CheckNeutralSurfaces(VisualTreeHelper.GetChild(node, i));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        private static void UiTests()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainController main = null;
            NativeMethods.POINT originalCursor;
            NativeMethods.GetCursorPos(out originalCursor);
            try
            {
                Test("Main window loads, opens, and registers real global hook", () =>
                {
                    main = new MainController(Path.Combine(artifacts, "ui-settings.xml"), false);
                    main.Window.Show(); Pump(300);
                    Check(main.Window.IsVisible && main.Window.ActualWidth > 700, "Window not visible");
                    Check(main.Window.Icon != null && !main.Engine.IsRunning, "Icon or idle state missing");
                });
                if (main == null) return;
                Test("Embedded Paytone One renders text while window icons retain their glyphs", () =>
                {
                    GlyphTypeface glyphs;
                    Check(new Typeface(main.Window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out glyphs) && glyphs.FontUri.Scheme == "pack", "Font is not loaded from the executable");
                    for (int view = 0; view < 3; view++) { ShowView(main, view); CheckAppFonts(main.Window); }
                    using (var stream = typeof(MainController).Assembly.GetManifestResourceStream("Vanta.PaytoneOne.License.txt"))
                    using (var reader = new StreamReader(stream)) Check(reader.ReadToEnd().Contains("SIL OPEN FONT LICENSE"), "Font license is missing");
                });
                Test("Rapid view changes finish on the latest selection with usable content", () =>
                {
                    main.SetView(1); Pump(35); main.SetView(2); Pump(35); ShowView(main, 0);
                    var content = main.Find<Grid>("ViewContent");
                    Check(main.Model.View == "Default" && main.Find<ListBox>("Navigation").SelectedIndex == 0 && main.Find<Grid>("DefaultView").IsVisible, "A stale transition replaced the last selection");
                    Check(content.IsHitTestVisible && Math.Abs(content.Opacity - 1) < 0.001, "Content remained faded or disabled after transition");
                    Check(Math.Abs(main.Window.Height - Math.Min(440, SystemParameters.WorkArea.Height - 24)) < 1, "Window did not finish resizing");
                });
                Test("Native window has transparent corners in every view and at 150% render scale", () =>
                {
                    Check(main.Window.AllowsTransparency && (GetWindowLong(new WindowInteropHelper(main.Window).Handle, -20) & 0x80000) != 0, "Per-pixel window transparency is not enabled");
                    for (int view = 0; view < 3; view++)
                    {
                        ShowView(main, view);
                        CheckRoundedCorners(main.Window, 1);
                        CheckRoundedCorners(main.Window, 1.5);
                        var screenCorner = main.Window.PointToScreen(new Point(2, 2));
                        var nativeCorner = new NativeMethods.POINT { X = (int)screenCorner.X, Y = (int)screenCorner.Y };
                        Check(NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(nativeCorner), 2) != new WindowInteropHelper(main.Window).Handle, "Transparent corner still captures native mouse input");
                    }
                });
                Test("Neutral black surfaces and text remain neutral in selected and active states", () =>
                {
                    main.Model.VariationEnabled = main.Model.DoubleEnabled = main.Model.LimitEnabled = main.Model.SequenceEnabled = true;
                    main.Model.Points.Add(new SequencePoint(10, 20));
                    main.Find<ListBox>("PointList").SelectedIndex = 0;
                    for (int view = 0; view < 3; view++) { ShowView(main, view); CheckNeutralSurfaces(main.Window); }
                    main.Notice("Preview notice", false); CheckNeutralSurfaces(main.Window);
                    main.Notice("Preview error", true); CheckNeutralSurfaces(main.Window);
                    main.HideNotice();
                    main.Model.Load(new ClickSettings());
                    main.Start(2000); Pump(100); CheckNeutralSurfaces(main.Window); main.Stop();
                    Check(main.Engine.WaitForStop(700), "Countdown did not stop"); Pump(100);
                });
                Test("Rounded window restores after minimizing", () =>
                {
                    main.Window.WindowState = WindowState.Minimized; Pump(80);
                    main.Window.WindowState = WindowState.Normal; main.Window.Activate(); Pump(150);
                    Check(main.Window.IsVisible, "Window did not restore"); CheckRoundedCorners(main.Window, 1);
                });
                Test("Default view renders with native controls", () => { ShowView(main, 0); Snapshot(main.Window, "vanta-default"); Check(main.Find<Grid>("DefaultView").IsVisible, "Default hidden"); });
                Test("Basic and advanced controls share their values", () => { main.Model.AmountText = "25"; main.Model.DurationText = "69"; ShowView(main, 1); Check(main.Find<TextBox>("DefaultAmount").Text == "25", "Binding failed"); Check(main.Find<Grid>("AdvancedView").IsVisible, "Advanced hidden"); Snapshot(main.Window, "vanta-advanced"); });
                Test("Sequence point list and reordering work", () => { main.Model.Points.Add(new SequencePoint(300, 200)); main.Model.Points.Add(new SequencePoint(400, 250)); var list = main.Find<ListBox>("PointList"); list.SelectedIndex = 1; main.Find<Button>("MovePointUp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(main.Model.Points[0].X == 400, "Move up failed"); main.Find<Button>("RemovePoint").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(main.Model.Points.Count == 1 && main.Model.Points[0].X == 300, "Remove failed"); main.Find<Button>("ClearPoints").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(main.Model.Points.Count == 0, "Clear failed"); });
                Test("Advanced controls stay visible with a long sequence", () =>
                {
                    for (int i = 0; i < 100; i++) main.Model.Points.Add(new SequencePoint(i, i));
                    Pump(100);
                    var card = main.Find<Border>("DoubleClickCard");
                    var bottom = card.TranslatePoint(new Point(0, card.ActualHeight), main.Window);
                    if (main.Window.ActualHeight >= 780) Check(bottom.Y <= main.Window.ActualHeight - 72, "Double click controls are clipped: " + bottom.Y);
                    Check(main.Find<ListBox>("PointList").ActualHeight <= 281, "Sequence list grows without a scroll boundary");
                    main.Model.Points.Clear();
                });
                Test("Settings view renders", () => { ShowView(main, 2); Snapshot(main.Window, "vanta-settings"); Check(main.Find<StackPanel>("SettingsView").IsVisible, "Settings hidden"); });
                Test("Invalid numeric input prevents a run", () => { main.Model.AmountText = "0"; main.Start(0); Check(!main.Engine.IsRunning && main.Find<Border>("NoticeBorder").IsVisible, "Invalid run started"); main.Model.AmountText = "20"; main.HideNotice(); });
                Test("Always on top setting updates the real window", () => { main.Model.AlwaysOnTop = true; Check(main.Window.Topmost, "Pin failed"); main.Model.AlwaysOnTop = false; Check(!main.Window.Topmost, "Unpin failed"); });
                Test("Start button countdown can be cancelled", () => { main.SetView(0); main.Start(2000); Check(main.Engine.IsRunning && !main.Model.CanEdit, "Run state"); main.Stop(); Check(main.Engine.WaitForStop(700), "Countdown didn't stop"); Pump(120); Check(main.Engine.Clicks == 0 && main.Model.CanEdit, "Countdown clicked or UI stayed locked"); });
                Test("Windows SendInput delivers left, middle, and right clicks to owned test pad", () =>
                {
                    var pad = main.OpenTestPad(); Pump(250);
                    CheckRoundedCorners(pad.Window, 1);
                    CheckNeutralSurfaces(pad.Window);
                    CheckAppFonts(pad.Window);
                    IntPtr targetHandle = new WindowInteropHelper(pad.Window).Handle;
                    var center = pad.Target.PointToScreen(new Point(pad.Target.ActualWidth / 2, pad.Target.ActualHeight / 2));
                    foreach (ClickButton button in Enum.GetValues(typeof(ClickButton)))
                    {
                        var output = new WindowsClickOutput { RequiredWindow = targetHandle };
                        output.MoveTo(new SequencePoint((int)center.X, (int)center.Y));
                        var s = Limited(4); s.Amount = 20; s.Button = button;
                        using (var engine = new ClickEngine(output))
                        {
                            engine.Start(s, 0);
                            var timeout = Stopwatch.StartNew(); while (engine.IsRunning && timeout.ElapsedMilliseconds < 2500) Pump(10);
                            Check(engine.WaitForStop(200), "Native test timed out");
                            Check(engine.LastError == null, "Native injection failed: " + engine.LastError);
                            Pump(100);
                        }
                    }
                    Check(pad.Clicks == 12 && pad.Left == 4 && pad.Middle == 4 && pad.Right == 4, "Real clicks not received: " + pad.Clicks + " L=" + pad.Left + " M=" + pad.Middle + " R=" + pad.Right);
                    Snapshot(pad.Window, "vanta-test-pad");
                    pad.Window.Close(); Pump(50);
                });
                Test("Full app runs a limited double click sequence in its test pad", () =>
                {
                    var pad = main.OpenTestPad(); Pump(200);
                    var center = pad.Target.PointToScreen(new Point(pad.Target.ActualWidth / 2, pad.Target.ActualHeight / 2));
                    var s = Limited(5); s.Amount = 12; s.DoubleClickEnabled = true; s.DoubleClickGapMs = 20; s.SequenceEnabled = true;
                    s.Points.Add(new SequencePoint((int)center.X - 15, (int)center.Y)); s.Points.Add(new SequencePoint((int)center.X + 15, (int)center.Y));
                    main.Model.Load(s); main.Start(0);
                    var timeout = Stopwatch.StartNew(); while (main.Engine.IsRunning && timeout.ElapsedMilliseconds < 3000) Pump(10);
                    Check(main.Engine.WaitForStop(200), "Sequence timeout"); Pump(150);
                    Check(main.Engine.LastError == null && main.Engine.Clicks == 5 && pad.Clicks == 5, "Sequence delivery mismatch " + main.Engine.LastError + " " + pad.Clicks);
                    pad.Window.Close(); Pump(50);
                });
                Test("Native guard prevents clicking Vanta's own window", () =>
                {
                    main.Model.Load(Limited(1)); ShowView(main, 0); main.Window.Activate(); Pump(150);
                    var point = main.Window.PointToScreen(new Point(100, 30)); NativeMethods.SetCursorPos((int)point.X, (int)point.Y);
                    main.Start(0); var timeout = Stopwatch.StartNew(); while (main.Engine.IsRunning && timeout.ElapsedMilliseconds < 1000) Pump(10); Pump(100);
                    Check(main.Engine.Clicks == 0 && main.Engine.LastError != null && main.Engine.LastError.Contains("over Vanta"), "Self-click guard failed");
                    main.HideNotice();
                });
                Test("Final default and advanced screenshots use clean settings", () => { main.Model.Load(new ClickSettings()); ShowView(main, 0); Snapshot(main.Window, "vanta-default"); ShowView(main, 1); Snapshot(main.Window, "vanta-advanced"); ShowView(main, 2); Snapshot(main.Window, "vanta-settings"); });
            }
            finally
            {
                if (main != null) { main.Window.Close(); main.Dispose(); }
                NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
                app.Shutdown();
            }
        }

        private sealed class FakeOutput : IClickOutput
        {
            public readonly ManualResetEvent Pressed = new ManualResetEvent(false);
            public int Downs, Ups;
            public bool Held, ThrowOnPress, FailReleaseOnce;
            public readonly List<int> Moves = new List<int>();
            public readonly List<ClickButton> Buttons = new List<ClickButton>();
            public void MoveTo(SequencePoint point) { Moves.Add(point.X); }
            public void Press(ClickButton button) { if (ThrowOnPress) throw new InvalidOperationException("injection blocked"); Downs++; Held = true; Buttons.Add(button); Pressed.Set(); }
            public void Release(ClickButton button) { if (FailReleaseOnce) { FailReleaseOnce = false; throw new InvalidOperationException("release blocked"); } Ups++; Held = false; }
        }
    }
}
