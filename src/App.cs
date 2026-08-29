using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("Vanta Auto Clicker")]
[assembly: AssemblyDescription("Precision mouse automation with global hotkeys and cursor sequences")]
[assembly: AssemblyProduct("Vanta Auto Clicker")]
[assembly: AssemblyCompany("Vanta")]
[assembly: AssemblyVersion("1.0.4.0")]
[assembly: AssemblyFileVersion("1.0.4.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Vanta
{
    public static class Program
    {
        [STAThread]
        public static int Main()
        {
            bool created;
            using (var single = new Mutex(true, "Local\\VantaAutoClicker.Desktop", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Vanta Auto Clicker is already open. Look for it in your taskbar.", "Vanta Auto Clicker", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }
                MainController controller = null;
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    controller = new MainController(SettingsStore.DefaultPath, true);
                    app.MainWindow = controller.Window;
                    app.DispatcherUnhandledException += (s, e) =>
                    {
                        controller.Engine.Stop();
                        controller.Notice("Stopped after an unexpected error: " + e.Exception.Message, true);
                        e.Handled = true;
                    };
                    app.Run(controller.Window);
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Vanta could not start.\n\n" + ex.GetBaseException().Message, "Vanta Auto Clicker", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { if (controller != null) controller.Dispose(); }
            }
        }
    }

    public sealed class MainController : IDisposable
    {
        public Window Window { get; private set; }
        public ViewModel Model { get; private set; }
        public ClickEngine Engine { get; private set; }
        private readonly WindowsClickOutput output = new WindowsClickOutput();
        private GlobalHotkeys hotkeys;
        private readonly string settingsPath;
        private readonly bool persist;
        private readonly DispatcherTimer refresh = new DispatcherTimer();
        private readonly DispatcherTimer save = new DispatcherTimer();
        private readonly DispatcherTimer capture = new DispatcherTimer();
        private Button captureHotkey;
        private DateTime startingUntil;
        private int captureSeconds;
        private volatile bool holdMode;
        private bool disposed, closing, changingView, updateInProgress;
        private int viewRevision;
        private TestPad testPad;

        public MainController(string settingsPath, bool persist)
        {
            this.settingsPath = settingsPath;
            this.persist = persist;
            using (var theme = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vanta.Theme.xaml"))
                Application.Current.Resources = (ResourceDictionary)XamlReader.Load(theme);
            // Fail clearly if a build loses the bundled font, rather than silently
            // rendering a substitute that happens to be installed on this PC.
            var font = (FontFamily)Application.Current.FindResource("AppFont");
            GlyphTypeface glyphs;
            if (!new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out glyphs) ||
                !System.Linq.Enumerable.Contains(glyphs.FamilyNames.Values, "Paytone One"))
                throw new InvalidOperationException("The bundled Paytone One font could not be loaded. Please rebuild or download a complete copy of Vanta.");
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vanta.MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            RoundedWindow.Attach(Window, Find<Border>("WindowShell"), Find<Grid>("ShellContent"));
            string warning;
            Model = new ViewModel(SettingsStore.Load(settingsPath, out warning));
            Window.DataContext = Model;
            var icon = LoadImage("Vanta.Logo.png");
            Window.Icon = icon;
            Find<Image>("BrandLogo").Source = icon;
            Engine = new ClickEngine(output);
            Engine.Finished += () => Dispatch(() => { if (!closing) { UpdateRunState(); if (Engine.LastError != null) Notice(Engine.LastError, true); } });
            WireControls();
            hotkeys = new GlobalHotkeys();
            hotkeys.ActionReceived += OnHotkey;
            Model.Changed += ModelChanged;
            refresh.Interval = TimeSpan.FromMilliseconds(100);
            refresh.Tick += (s, e) => UpdateRunState();
            refresh.Start();
            save.Interval = TimeSpan.FromMilliseconds(700);
            save.Tick += (s, e) => { save.Stop(); SavePreferences(); };
            capture.Interval = TimeSpan.FromSeconds(1);
            capture.Tick += (s, e) =>
            {
                captureSeconds--;
                if (captureSeconds <= 0) { capture.Stop(); AddCursorPoint(); Find<Button>("AddPointButton").Content = "+  Capture cursor in 3 seconds"; }
                else Find<Button>("AddPointButton").Content = "Capturing in " + captureSeconds + "…";
            };
            SetView(Model.View == "Advanced" ? 1 : Model.View == "Settings" ? 2 : 0);
            ModelChanged();
            Window.SourceInitialized += (s, e) => output.ProtectedWindow = new WindowInteropHelper(Window).Handle;
            Window.Loaded += (s, e) => UiMotion.Reveal(Find<Grid>("ViewContent"), 220, 6);
            Window.Closing += OnClosing;
            Window.PreviewKeyDown += CaptureKey;
            Window.Deactivated += (s, e) => CancelHotkeyCapture();
            SystemEvents.SessionSwitch += SessionChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            if (warning != null) Notice(warning, true);
        }

        public T Find<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }

        public static BitmapImage LoadImage(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            }
        }

        private void WireControls()
        {
            Find<Border>("TitleBar").MouseLeftButtonDown += (s, e) => { if (e.OriginalSource is TextBlock || e.OriginalSource is Image || e.OriginalSource is Border || e.OriginalSource is Grid) { try { Window.DragMove(); } catch (InvalidOperationException) { } } };
            Find<Button>("CloseButton").Click += (s, e) => Window.Close();
            Find<Button>("MinimizeButton").Click += (s, e) => Window.WindowState = WindowState.Minimized;
            Find<Button>("PinButton").Click += (s, e) => Model.AlwaysOnTop = !Model.AlwaysOnTop;
            Find<Button>("StartButton").Click += (s, e) => { if (Engine.IsRunning) Stop(); else Start(2000); };
            Find<ListBox>("Navigation").SelectionChanged += (s, e) => { if (!changingView) SetView(Find<ListBox>("Navigation").SelectedIndex); };
            Find<Button>("DefaultHotkey").Click += (s, e) => BeginHotkeyCapture((Button)s);
            Find<Button>("AdvancedHotkey").Click += (s, e) => BeginHotkeyCapture((Button)s);
            Find<Button>("AddPointButton").Click += (s, e) =>
            {
                captureSeconds = 3;
                capture.Stop(); capture.Start();
                Find<Button>("AddPointButton").Content = "Capturing in 3…";
                Notice("Move the cursor to your target. Its position will be saved in 3 seconds.", false);
            };
            Find<Button>("RemovePoint").Click += (s, e) => { int i = Find<ListBox>("PointList").SelectedIndex; if (i >= 0) Model.Points.RemoveAt(i); };
            Find<Button>("ClearPoints").Click += (s, e) => Model.Points.Clear();
            Find<Button>("MovePointUp").Click += (s, e) => MovePoint(-1);
            Find<Button>("MovePointDown").Click += (s, e) => MovePoint(1);
            Find<ListBox>("PointList").SelectionChanged += (s, e) => UpdatePointButtons();
            Find<Button>("TestPadButton").Click += (s, e) => OpenTestPad();
            Find<Button>("ExportButton").Click += (s, e) => ExportProfile();
            Find<Button>("ImportButton").Click += (s, e) => ImportProfile();
            Find<Button>("FontLicenseButton").Click += (s, e) => ShowFontLicense();
            Find<Button>("UpdateButton").Click += CheckForUpdates;
            Find<Button>("ResetButton").Click += (s, e) =>
            {
                WithHotkeysSuspended(() =>
                {
                    if (MessageBox.Show(Window, "Reset all options and remove saved sequence points?", "Reset Vanta", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        Model.Load(new ClickSettings());
                        SetView(2);
                        SavePreferences();
                        Notice("Default settings restored. Your start / stop hotkey is F8.", false);
                    }
                });
            };
        }

        private async void CheckForUpdates(object sender, RoutedEventArgs e)
        {
            if (updateInProgress || closing) return;
            updateInProgress = true;
            Button button = Find<Button>("UpdateButton");
            TextBlock status = Find<TextBlock>("UpdateStatus");
            button.IsEnabled = false;
            button.Content = "Checking…";
            status.Text = "Checking the latest GitHub Release…";
            try
            {
                var service = new UpdateService();
                UpdateCheck update = await service.CheckAsync();
                if (!update.IsUpdateAvailable)
                {
                    status.Text = "You have the latest version (" + DisplayVersion(update.CurrentVersion) + ").";
                    button.Content = "Check again";
                    return;
                }

                status.Text = "Vanta " + DisplayVersion(update.LatestVersion) + " is available.";
                MessageBoxResult answer = MessageBox.Show(
                    Window,
                    "Vanta " + DisplayVersion(update.LatestVersion) + " is available.\n\nDownload it from GitHub, verify it, install it, and reopen Vanta now?",
                    "Update Vanta Auto Clicker",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    button.Content = "Check again";
                    return;
                }

                button.Content = "Downloading…";
                status.Text = "Downloading and verifying Vanta Setup…";
                string installer = await service.DownloadAsync(update);
                status.Text = "Starting the verified installer…";
                SavePreferences();
                Engine.Stop();
                Process.Start(new ProcessStartInfo
                {
                    FileName = installer,
                    Arguments = "/update " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " /launch",
                    UseShellExecute = true
                });
                Window.Close();
            }
            catch (Exception ex)
            {
                status.Text = "Update failed. Your current version was not changed.";
                button.Content = "Try again";
                Notice("Update failed: " + ex.GetBaseException().Message, true);
            }
            finally
            {
                updateInProgress = false;
                if (!closing) button.IsEnabled = true;
            }
        }

        private static string DisplayVersion(Version version)
        {
            return version.Major + "." + version.Minor + "." + Math.Max(0, version.Build);
        }

        public void SetView(int index)
        {
            if (index < 0 || index > 2) index = 0;
            CancelHotkeyCapture();
            capture.Stop();
            Find<Button>("AddPointButton").Content = "+  Capture cursor in 3 seconds";
            changingView = true;
            Find<ListBox>("Navigation").SelectedIndex = index;
            changingView = false;
            int revision = ++viewRevision;
            var content = Find<Grid>("ViewContent");
            bool animate = Window.IsLoaded && Window.WindowState != WindowState.Minimized && UiMotion.Enabled;
            if (hotkeys != null) hotkeys.CaptureEnabled = false;
            Action apply = () =>
            {
                if (closing || revision != viewRevision) return;
                Find<Grid>("DefaultView").Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
                Find<Grid>("AdvancedView").Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
                Find<StackPanel>("SettingsView").Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
                double height = Math.Min(index == 0 ? 440 : index == 1 ? 820 : 820, SystemParameters.WorkArea.Height - 24);
                double width = Math.Min(980, SystemParameters.WorkArea.Width - 24);
                double top = Window.Top;
                if (Window.IsLoaded && top + height > SystemParameters.WorkArea.Bottom)
                    top = Math.Max(SystemParameters.WorkArea.Top + 12, SystemParameters.WorkArea.Bottom - height - 12);
                UiMotion.Resize(Window, width, height, top, animate);
                Find<ScrollViewer>("ViewScroll").ScrollToVerticalOffset(0);
                Model.View = index == 0 ? "Default" : index == 1 ? "Advanced" : "Settings";
                if (hotkeys != null) hotkeys.CaptureEnabled = index == 1 && !Engine.IsRunning;
                content.IsHitTestVisible = true;
                UiMotion.Reveal(content, animate ? 220 : 0, animate ? 8 : 0);
            };
            if (!animate) apply();
            else
            {
                content.IsHitTestVisible = false;
                UiMotion.Fade(content, 0, 85, apply);
            }
        }

        private void ModelChanged()
        {
            Window.Topmost = Model.AlwaysOnTop;
            Find<Button>("PinButton").Foreground = Brush(Model.AlwaysOnTop ? "#FFFFFF" : "#929292");
            Find<Button>("PinButton").BorderBrush = Model.AlwaysOnTop ? (Brush)Window.FindResource("AccentOutline") : Brushes.Transparent;
            Find<StackPanel>("EmptySequence").Visibility = Model.Points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            holdMode = Model.ActivationIndex == 1;
            if (hotkeys != null) hotkeys.Configure(Model.HotkeyKey, Model.HotkeyMods);
            UpdatePointButtons();
            UpdateRunState();
            if (persist) { save.Stop(); save.Start(); }
        }

        public void Start(int delayMs)
        {
            if (Engine.IsRunning || captureHotkey != null || capture.IsEnabled) return;
            ClickSettings settings; string error;
            if (!Model.TryRead(true, out settings, out error)) { Notice(error, true); return; }
            try
            {
                HideNotice();
                SavePreferences();
                startingUntil = DateTime.UtcNow.AddMilliseconds(delayMs);
                Engine.Start(settings, delayMs);
                Model.CanEdit = false;
                hotkeys.Running = true;
                hotkeys.CaptureEnabled = false;
                if (Model.MinimizeOnStart) Window.WindowState = WindowState.Minimized;
                UpdateRunState();
            }
            catch (Exception ex) { Notice(ex.Message, true); UpdateRunState(); }
        }

        public void Stop()
        {
            Engine.Stop();
            capture.Stop();
            Find<Button>("AddPointButton").Content = "+  Capture cursor in 3 seconds";
            UpdateRunState();
        }

        private void OnHotkey(HotkeyAction action)
        {
            if (action == HotkeyAction.EmergencyStop || (action == HotkeyAction.Deactivate && holdMode)) Engine.Stop();
            Dispatch(() =>
            {
                if (closing) return;
                if (action == HotkeyAction.EmergencyStop) { Stop(); Notice("Emergency stop. All clicking has been cancelled.", false); }
                else if (action == HotkeyAction.CapturePoint && !Engine.IsRunning) AddCursorPoint();
                else if (action == HotkeyAction.Deactivate && holdMode) Stop();
                else if (action == HotkeyAction.Activate)
                {
                    if (Engine.IsRunning) { if (!holdMode) Stop(); }
                    else Start(holdMode ? 0 : 100);
                }
            });
        }

        private void UpdateRunState()
        {
            if (disposed || closing) return;
            bool active = Engine.IsRunning;
            if (Model.CanEdit == active) Model.CanEdit = !active;
            if (hotkeys != null) { hotkeys.Running = active; hotkeys.CaptureEnabled = Model.View == "Advanced" && !active && captureHotkey == null; }
            bool starting = active && DateTime.UtcNow < startingUntil;
            Find<TextBlock>("StatusLabel").Text = active ? starting ? "Starting… move to your target" : "Clicking" : Engine.Clicks == 0 ? "Ready" : Engine.StopReason;
            Find<System.Windows.Shapes.Ellipse>("StatusDot").Fill = Brush(active ? "#FFFFFF" : "#787878");
            var elapsed = TimeSpan.FromSeconds(Engine.ElapsedSeconds);
            Find<TextBlock>("SessionStats").Text = Engine.Clicks.ToString("N0") + " clicks   ·   " + (elapsed.TotalHours >= 1 ? elapsed.ToString(@"hh\:mm\:ss") : elapsed.ToString(@"mm\:ss"));
            var button = Find<Button>("StartButton");
            button.Content = active ? "■  Stop clicking" : holdMode ? "Hold " + Model.HotkeyText : "Start clicking  →";
            button.Background = Brushes.Black;
            button.Foreground = Brushes.White;
            button.BorderBrush = active ? Brushes.White : (Brush)Window.FindResource("AccentOutline");
            button.IsEnabled = active || (!holdMode && captureHotkey == null && !capture.IsEnabled);
            Find<TextBlock>("ShortcutHint").Text = (holdMode ? "Hold " : "Press ") + Model.HotkeyText + (active ? "  ·  Esc to stop" : " to start  ·  Esc to stop");
            ClickSettings settings; string error;
            if (Model.TryRead(false, out settings, out error))
            {
                string summary = (1000 / settings.IntervalMs).ToString("0.##", CultureInfo.CurrentCulture) + (settings.DoubleClickEnabled ? " pairs / sec" : " clicks / sec");
                if (settings.LimitEnabled) summary += "  ·  limit " + settings.LimitValue.ToString("0.##") + (settings.Limit == LimitMode.Clicks ? " clicks" : "s");
                if (settings.SequenceEnabled) summary += "  ·  " + settings.Points.Count + " points";
                Find<TextBlock>("FeatureSummary").Text = summary;
            }
            else Find<TextBlock>("FeatureSummary").Text = "Check your settings";
        }

        private void BeginHotkeyCapture(Button button)
        {
            if (Engine.IsRunning) return;
            CancelHotkeyCapture();
            captureHotkey = button;
            hotkeys.Suspended = true;
            button.Content = "Press shortcut…";
            button.Focus();
            Notice("Press your new shortcut. Esc cancels. F6 is reserved for capturing points.", false);
        }

        private void CaptureKey(object sender, KeyEventArgs e)
        {
            if (captureHotkey == null) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { CancelHotkeyCapture(); HideNotice(); return; }
            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= HotkeyModifiers.Control;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modifiers |= HotkeyModifiers.Windows;
            if (!ClickSettings.ValidHotkey(virtualKey, modifiers)) return;
            Model.SetHotkey(virtualKey, modifiers);
            CancelHotkeyCapture();
            Notice("Shortcut saved: " + Model.HotkeyText + ". Esc always stops clicking.", false);
        }

        private void CancelHotkeyCapture()
        {
            if (captureHotkey == null) return;
            captureHotkey.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("HotkeyText"));
            captureHotkey = null;
            if (hotkeys != null) hotkeys.Suspended = false;
        }

        public void AddCursorPoint()
        {
            if (Engine.IsRunning || Model.Points.Count >= 1000) return;
            NativeMethods.POINT point;
            if (!NativeMethods.GetCursorPos(out point)) { Notice("Could not read the cursor position.", true); return; }
            if (NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), 2) == output.ProtectedWindow)
            { Notice("Move the cursor outside Vanta, then press F6 to save that position.", true); return; }
            Model.Points.Add(new SequencePoint(point.X, point.Y));
            Find<ListBox>("PointList").SelectedIndex = Model.Points.Count - 1;
            Find<ListBox>("PointList").ScrollIntoView(Model.Points[Model.Points.Count - 1]);
            Notice("Point " + Model.Points.Count + " saved at " + point.X + ", " + point.Y + ".", false);
        }

        private void MovePoint(int direction)
        {
            var list = Find<ListBox>("PointList");
            int index = list.SelectedIndex, target = index + direction;
            if (index < 0 || target < 0 || target >= Model.Points.Count) return;
            Model.Points.Move(index, target);
            list.SelectedIndex = target;
        }

        private void UpdatePointButtons()
        {
            int index = Find<ListBox>("PointList").SelectedIndex;
            Find<Button>("MovePointUp").IsEnabled = index > 0;
            Find<Button>("MovePointDown").IsEnabled = index >= 0 && index < Model.Points.Count - 1;
            Find<Button>("RemovePoint").IsEnabled = index >= 0;
            Find<Button>("ClearPoints").IsEnabled = Model.Points.Count > 0;
        }

        public TestPad OpenTestPad()
        {
            if (testPad == null)
            {
                testPad = new TestPad(Window);
                testPad.Window.Closed += (s, e) => { Engine.Stop(); testPad = null; };
            }
            testPad.Window.Show(); testPad.Window.Activate();
            return testPad;
        }

        private void SavePreferences()
        {
            if (!persist || closing) return;
            ClickSettings settings; string error;
            if (!Model.TryRead(false, out settings, out error)) return;
            try { SettingsStore.Save(settingsPath, settings); }
            catch (Exception ex) { Notice("Preferences could not be saved: " + ex.Message, true); }
        }

        private void ExportProfile()
        {
            ClickSettings settings; string error;
            if (!Model.TryRead(false, out settings, out error)) { Notice(error, true); return; }
            WithHotkeysSuspended(() =>
            {
                var dialog = new SaveFileDialog { Title = "Export Vanta profile", FileName = "Vanta-profile.xml", Filter = "Vanta profile (*.xml)|*.xml", DefaultExt = ".xml" };
                if (dialog.ShowDialog(Window) != true) return;
                try { SettingsStore.Save(dialog.FileName, settings); Notice("Profile exported.", false); }
                catch (Exception ex) { Notice("Could not export the profile: " + ex.Message, true); }
            });
        }

        private void ImportProfile()
        {
            WithHotkeysSuspended(() =>
            {
                var dialog = new OpenFileDialog { Title = "Import Vanta profile", Filter = "Vanta profile (*.xml)|*.xml", CheckFileExists = true };
                if (dialog.ShowDialog(Window) != true) return;
                string warning;
                var loaded = SettingsStore.Load(dialog.FileName, out warning);
                if (warning != null) { Notice("Profile was not imported. " + warning, true); return; }
                Model.Load(loaded);
                SetView(2);
                SavePreferences();
                Notice("Profile imported. Start / stop shortcut: " + Model.HotkeyText + ".", false);
            });
        }

        private void WithHotkeysSuspended(Action action)
        {
            hotkeys.Suspended = true;
            try { action(); } finally { hotkeys.Suspended = false; }
        }

        private void ShowFontLicense()
        {
            WithHotkeysSuspended(() =>
            {
                string license;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vanta.PaytoneOne.License.txt"))
                using (var reader = new StreamReader(stream)) license = reader.ReadToEnd();
                var dialog = new Window
                {
                    Title = "Vanta - Font license", Owner = Window, Width = Math.Min(700, SystemParameters.WorkArea.Width - 40),
                    Height = Math.Min(600, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
                    Background = Brushes.Transparent, Foreground = Brushes.White, FontFamily = Window.FontFamily, FontSize = 12, Icon = Window.Icon
                };
                var content = new Grid { Margin = new Thickness(24) };
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var heading = new TextBlock { Text = "Paytone One", FontSize = 22, Margin = new Thickness(0, 0, 0, 16) };
                content.Children.Add(heading);
                var text = new TextBox { Text = license, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 11 };
                System.Windows.Automation.AutomationProperties.SetName(text, "Paytone One license text");
                Grid.SetRow(text, 1); content.Children.Add(text);
                var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 96, Margin = new Thickness(0, 16, 0, 0) };
                close.Click += (s, e) => dialog.Close();
                Grid.SetRow(close, 2); content.Children.Add(close);
                var clip = new Grid(); clip.Children.Add(content);
                var shell = new Border { Background = Brushes.Black, BorderBrush = Brush("#363636"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(24), Child = clip };
                dialog.Content = shell;
                RoundedWindow.Attach(dialog, shell, clip);
                heading.MouseLeftButtonDown += (s, e) => dialog.DragMove();
                dialog.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) dialog.Close(); };
                dialog.Loaded += (s, e) => UiMotion.Reveal(content, 200, 6);
                dialog.ShowDialog();
            });
        }

        public void Notice(string message, bool error)
        {
            bool wasHidden = Find<Border>("NoticeBorder").Visibility != Visibility.Visible;
            Find<TextBlock>("NoticeText").Text = error ? "Please check: " + message : message;
            Find<TextBlock>("NoticeText").Foreground = Brush("#DDDDDD");
            Find<Border>("NoticeBorder").Background = Brushes.Black;
            Find<Border>("NoticeBorder").BorderBrush = error ? Brushes.White : (Brush)Window.FindResource("AccentOutline");
            Find<Border>("NoticeBorder").Visibility = Visibility.Visible;
            if (wasHidden) UiMotion.Reveal(Find<Border>("NoticeBorder"), 180, 4);
        }
        public void HideNotice() { Find<Border>("NoticeBorder").Visibility = Visibility.Collapsed; }
        public static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
        private void Dispatch(Action action) { if (!closing && !Window.Dispatcher.HasShutdownStarted) Window.Dispatcher.BeginInvoke(action); }
        private void SessionChanged(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff) Engine.Stop(); }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) Engine.Stop(); }
        private void OnClosing(object sender, CancelEventArgs e) { SavePreferences(); closing = true; Dispose(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            closing = true;
            refresh.Stop(); save.Stop(); capture.Stop();
            Engine.Stop();
            SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            if (hotkeys != null) hotkeys.Dispose();
            Engine.Dispose();
            if (testPad != null) { var pad = testPad; testPad = null; pad.Window.Close(); }
        }
    }

    public sealed class TestPad
    {
        public Window Window { get; private set; }
        public Border Target { get; private set; }
        public int Clicks { get; private set; }
        public int Left { get; private set; }
        public int Middle { get; private set; }
        public int Right { get; private set; }
        private readonly TextBlock count, detail;
        private readonly Stopwatch clock = new Stopwatch();
        public TestPad(Window owner)
        {
            Window = new Window { Title = "Vanta · Test pad", Width = 490, Height = 460, MinWidth = 360, MinHeight = 360, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanMinimize, AllowsTransparency = true, Background = Brushes.Transparent, Foreground = Brushes.White, FontFamily = owner.FontFamily, FontSize = 12, Icon = owner.Icon, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, UseLayoutRounding = true };
            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 18), Background = Brushes.Transparent };
            heading.Children.Add(new TextBlock { Text = "Test pad", FontSize = 22, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            var close = new Button { Content = "\uE8BB", Style = (Style)Window.FindResource("Chrome"), FontFamily = new FontFamily("Segoe MDL2 Assets"), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Close and stop clicking" };
            System.Windows.Automation.AutomationProperties.SetName(close, "Close test pad");
            close.Click += (s, e) => Window.Close();
            heading.Children.Add(close);
            heading.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource is Grid || e.OriginalSource is TextBlock) { try { Window.DragMove(); } catch (InvalidOperationException) { } } };
            grid.Children.Add(heading);
            Target = new Border { Background = Brushes.Black, BorderBrush = (Brush)Window.FindResource("AccentOutline"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(20), Cursor = Cursors.Cross };
            Grid.SetRow(Target, 1); grid.Children.Add(Target);
            var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            count = new TextBlock { Text = "0", FontSize = 64, FontWeight = FontWeights.Normal, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            inner.Children.Add(count);
            inner.Children.Add(new TextBlock { Text = "CLICKS RECEIVED", FontSize = 10, Foreground = MainController.Brush("#858585"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 18) });
            detail = new TextBlock { Text = "Left 0    Middle 0    Right 0", Foreground = MainController.Brush("#B8B8B8"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
            inner.Children.Add(detail); Target.Child = inner;
            Target.PreviewMouseUp += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Right) return;
                if (!clock.IsRunning) clock.Start();
                Clicks++;
                if (e.ChangedButton == MouseButton.Left) Left++;
                else if (e.ChangedButton == MouseButton.Middle) Middle++;
                else if (e.ChangedButton == MouseButton.Right) Right++;
                count.Text = Clicks.ToString("N0");
                detail.Text = "Left " + Left + "    Middle " + Middle + "    Right " + Right;
                e.Handled = true;
            };
            var bottom = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            bottom.Children.Add(new TextBlock { Text = "Hover inside the outline and use your start hotkey.\nEsc stops. Close this window to stop too.", Foreground = MainController.Brush("#929292"), FontSize = 11, LineHeight = 19, TextWrapping = TextWrapping.Wrap });
            var reset = new Button { Content = "Reset counter", Margin = new Thickness(0, 14, 0, 0) };
            reset.Click += (s, e) => Reset(); bottom.Children.Add(reset);
            Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
            var content = new Grid(); content.Children.Add(grid);
            var shell = new Border { Background = Brushes.Black, BorderBrush = MainController.Brush("#363636"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(24), Child = content };
            Window.Content = shell;
            RoundedWindow.Attach(Window, shell, content);
            Window.Loaded += (s, e) => UiMotion.Reveal(grid, 220, 6);
        }
        public void Reset() { Clicks = Left = Middle = Right = 0; clock.Reset(); count.Text = "0"; detail.Text = "Left 0    Middle 0    Right 0"; }
    }
}
