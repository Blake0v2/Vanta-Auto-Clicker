using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Vanta.Installer
{
    internal static class Program
    {
        internal const string AppName = "Vanta Auto Clicker";
        internal const string AppFileName = "Vanta Auto Clicker.exe";
        internal const string UninstallerFileName = "Uninstall Vanta Auto Clicker.exe";
        internal const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VantaAutoClicker";
        internal const string AppResource = "Vanta.Installer.App.exe";
        internal const string GuideResource = "Vanta.Installer.QuickStart.txt";
        internal const string LicenseResource = "Vanta.Installer.PaytoneLicense.txt";

        internal static int Result;

        internal static string InstallDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); }
        }

        internal static string AppPath
        {
            get { return Path.Combine(InstallDirectory, AppFileName); }
        }

        internal static string ShortVersion
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (HasArgument(args, "/verify-payloads"))
                    return InstallerCore.VerifyPayloads() ? 0 : 1;

                int renderIndex = IndexOfArgument(args, "/test-render");
                if (renderIndex >= 0)
                {
                    if (renderIndex + 1 >= args.Length) return 2;
                    using (InstallForm form = new InstallForm())
                    using (Bitmap bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        form.Show();
                        Application.DoEvents();
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
                        bitmap.Save(Path.GetFullPath(args[renderIndex + 1]), ImageFormat.Png);
                        form.Close();
                    }
                    return 0;
                }

                int testExtractIndex = IndexOfArgument(args, "/test-extract");
                if (testExtractIndex >= 0)
                {
                    if (testExtractIndex + 1 >= args.Length) return 2;
                    InstallerCore.Install(args[testExtractIndex + 1], false, false, false);
                    return 0;
                }

                int workerIndex = IndexOfArgument(args, "/uninstall-worker");
                if (workerIndex >= 0)
                {
                    if (workerIndex + 2 >= args.Length) return 2;
                    string root = args[workerIndex + 1];
                    int parentId;
                    if (!Int32.TryParse(args[workerIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentId)) return 2;
                    bool removeSettings = HasArgument(args, "/remove-settings");
                    bool quietWorker = HasArgument(args, "/silent");
                    return InstallerCore.UninstallWorker(root, parentId, removeSettings, quietWorker);
                }

                int updateIndex = IndexOfArgument(args, "/update");
                if (updateIndex >= 0)
                {
                    if (updateIndex + 1 >= args.Length) return 2;
                    int parentId;
                    if (!Int32.TryParse(args[updateIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentId) || parentId <= 0) return 2;
                    return InstallerCore.ApplyUpdate(parentId, HasArgument(args, "/launch"));
                }

                bool uninstall = HasArgument(args, "/uninstall") ||
                    Path.GetFileName(Application.ExecutablePath).StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase);
                bool silent = HasArgument(args, "/silent");

                if (silent)
                {
                    if (uninstall)
                        return InstallerCore.BeginUninstall(false, true);

                    InstallerCore.Install(InstallDirectory, true, true, true);
                    return 0;
                }

                Application.Run(uninstall ? (Form)new UninstallForm() : new InstallForm());
                return Result;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.GetBaseException().Message,
                    AppName + " Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        internal static bool HasArgument(string[] args, string wanted)
        {
            return IndexOfArgument(args, wanted) >= 0;
        }

        private static int IndexOfArgument(string[] args, string wanted)
        {
            for (int index = 0; index < args.Length; index++)
                if (String.Equals(args[index], wanted, StringComparison.OrdinalIgnoreCase)) return index;
            return -1;
        }
    }

    internal static class InstallerCore
    {
        private const int MoveFileDelayUntilReboot = 4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        internal static bool IsInstalled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.UninstallRegistryPath))
                    return key != null && File.Exists(Program.AppPath);
            }
        }

        internal static bool IsAppRunning()
        {
            try
            {
                return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Program.AppFileName)).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool VerifyPayloads()
        {
            string[] resources = { Program.AppResource, Program.GuideResource, Program.LicenseResource, "Vanta.Installer.Logo.png", "Vanta.Installer.Paytone.ttf" };
            Assembly assembly = Assembly.GetExecutingAssembly();
            for (int index = 0; index < resources.Length; index++)
            {
                using (Stream stream = assembly.GetManifestResourceStream(resources[index]))
                {
                    if (stream == null || stream.Length == 0) return false;
                    if (resources[index] == Program.AppResource && (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')) return false;
                }
            }
            return true;
        }

        internal static void Install(string installDirectory, bool createShortcuts, bool createDesktopShortcut, bool registerUninstall)
        {
            if (IsAppRunning()) throw new InvalidOperationException("Close Vanta Auto Clicker before installing or updating it.");

            string root = Path.GetFullPath(installDirectory);
            Directory.CreateDirectory(root);
            FileAttributes rootAttributes = File.GetAttributes(root);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The install folder cannot be a symbolic link or junction.");

            string appPath = Path.Combine(root, Program.AppFileName);
            string uninstallerPath = Path.Combine(root, Program.UninstallerFileName);
            WriteResource(Program.AppResource, appPath);
            WriteResource(Program.GuideResource, Path.Combine(root, "QUICKSTART.txt"));
            WriteResource(Program.LicenseResource, Path.Combine(root, "Paytone-One-OFL.txt"));
            CopyFileAtomic(Application.ExecutablePath, uninstallerPath);

            if (createShortcuts)
            {
                CreateStartMenuShortcuts(appPath, uninstallerPath);
                SetDesktopShortcut(appPath, createDesktopShortcut);
            }

            if (registerUninstall)
                RegisterUninstall(root, appPath, uninstallerPath, createDesktopShortcut);
        }

        internal static int ApplyUpdate(int parentId, bool launchAfterInstall)
        {
            if (parentId == Process.GetCurrentProcess().Id)
                throw new InvalidOperationException("The update request did not identify the running Vanta app.");

            try
            {
                using (Process parent = Process.GetProcessById(parentId))
                {
                    bool alreadyExited;
                    try { alreadyExited = parent.HasExited; }
                    catch (InvalidOperationException) { alreadyExited = true; }
                    if (!alreadyExited)
                    {
                        string processName = null;
                        try { processName = parent.ProcessName; }
                        catch (InvalidOperationException) { }
                        if (processName != null && !String.Equals(processName, Path.GetFileNameWithoutExtension(Program.AppFileName), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The update request did not come from Vanta Auto Clicker.");
                        if (processName != null && !parent.WaitForExit(30000))
                            throw new InvalidOperationException("Vanta did not close in time. Open it again and retry the update.");
                    }
                }
            }
            catch (ArgumentException)
            {
                // The app already finished closing before Setup reached it.
            }

            if (IsAppRunning())
                throw new InvalidOperationException("Another Vanta Auto Clicker window is still open.");

            bool desktopShortcut = GetDesktopShortcutPreference();
            Install(Program.InstallDirectory, true, desktopShortcut, true);

            if (launchAfterInstall)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Program.AppPath,
                    WorkingDirectory = Program.InstallDirectory,
                    UseShellExecute = true
                });
            }

            ScheduleDownloadedUpdaterDeletion();
            return 0;
        }

        private static bool GetDesktopShortcutPreference()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.UninstallRegistryPath))
            {
                if (key != null)
                {
                    object value = key.GetValue("DesktopShortcut");
                    if (value is int) return (int)value != 0;
                }
            }
            return File.Exists(DesktopShortcutPath);
        }

        private static void ScheduleDownloadedUpdaterDeletion()
        {
            try
            {
                string updater = Path.GetFullPath(Application.ExecutablePath);
                string updatesRoot = Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Program.AppName, "Updates")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (updater.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase))
                    MoveFileEx(updater, null, MoveFileDelayUntilReboot);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void WriteResource(string resourceName, string destination)
        {
            string temporary = destination + ".installing";
            try
            {
                using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (input == null) throw new InvalidOperationException("The installer payload is incomplete: " + resourceName);
                    using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
                ReplaceFile(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void CopyFileAtomic(string source, string destination)
        {
            string temporary = destination + ".installing";
            try
            {
                File.Copy(source, temporary, true);
                ReplaceFile(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void ReplaceFile(string temporary, string destination)
        {
            if (File.Exists(destination))
            {
                string backup = destination + ".old";
                try
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Replace(temporary, destination, backup, true);
                }
                finally
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }
            }
            else
            {
                File.Move(temporary, destination);
            }
        }

        private static void RegisterUninstall(string root, string appPath, string uninstallerPath, bool desktopShortcut)
        {
            long size = DirectorySize(root) / 1024;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.UninstallRegistryPath))
            {
                if (key == null) throw new InvalidOperationException("Windows could not create the Installed apps entry.");
                key.SetValue("DisplayName", Program.AppName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", Program.ShortVersion, RegistryValueKind.String);
                key.SetValue("Publisher", "Vanta", RegistryValueKind.String);
                key.SetValue("DisplayIcon", appPath + ",0", RegistryValueKind.String);
                key.SetValue("InstallLocation", root, RegistryValueKind.String);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), RegistryValueKind.String);
                key.SetValue("UninstallString", Quote(uninstallerPath) + " /uninstall", RegistryValueKind.String);
                key.SetValue("QuietUninstallString", Quote(uninstallerPath) + " /uninstall /silent", RegistryValueKind.String);
                key.SetValue("URLInfoAbout", "https://github.com/Blake0v2/Vanta-Auto-Clicker", RegistryValueKind.String);
                key.SetValue("EstimatedSize", Math.Min(size, Int32.MaxValue), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("DesktopShortcut", desktopShortcut ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        private static long DirectorySize(string root)
        {
            long total = 0;
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
            }
            return total;
        }

        private static void CreateStartMenuShortcuts(string appPath, string uninstallerPath)
        {
            string directory = StartMenuDirectory;
            Directory.CreateDirectory(directory);
            CreateShortcut(Path.Combine(directory, Program.AppName + ".lnk"), appPath, Path.GetDirectoryName(appPath), appPath, "Open " + Program.AppName);
            CreateShortcut(Path.Combine(directory, "Uninstall " + Program.AppName + ".lnk"), uninstallerPath, Path.GetDirectoryName(uninstallerPath), appPath, "Remove " + Program.AppName);
        }

        private static void SetDesktopShortcut(string appPath, bool enabled)
        {
            string shortcutPath = DesktopShortcutPath;
            if (enabled)
                CreateShortcut(shortcutPath, appPath, Path.GetDirectoryName(appPath), appPath, "Open " + Program.AppName);
            else if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string description)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("Windows Script Host is unavailable, so shortcuts could not be created.");
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static int BeginUninstall(bool removeSettings, bool quiet)
        {
            if (IsAppRunning())
            {
                if (!quiet)
                    MessageBox.Show("Close Vanta Auto Clicker, then try again.", "Uninstall " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 2;
            }

            string expectedRoot = Path.GetFullPath(Program.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string helper = Path.Combine(Path.GetTempPath(), "VantaUninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, helper, true);

            string arguments = "/uninstall-worker " + Quote(expectedRoot) + " " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            if (removeSettings) arguments += " /remove-settings";
            if (quiet) arguments += " /silent";
            Process.Start(new ProcessStartInfo
            {
                FileName = helper,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return 0;
        }

        internal static int UninstallWorker(string requestedRoot, int parentId, bool removeSettings, bool quiet)
        {
            try
            {
                try { Process.GetProcessById(parentId).WaitForExit(15000); }
                catch (ArgumentException) { }

                string root = Path.GetFullPath(requestedRoot).TrimEnd(Path.DirectorySeparatorChar);
                string expected = Path.GetFullPath(Program.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar);
                if (!String.Equals(root, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The uninstall location was not recognized.");

                Exception lastError = null;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(root)) Directory.Delete(root, true);
                        lastError = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Thread.Sleep(350);
                    }
                }
                if (lastError != null) throw lastError;

                RemoveShortcuts();
                Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallRegistryPath, false);
                if (removeSettings) RemoveSettings();
                MoveFileEx(Application.ExecutablePath, null, MoveFileDelayUntilReboot);

                if (!quiet)
                    MessageBox.Show(Program.AppName + " was removed.", "Uninstall complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                    MessageBox.Show("Vanta could not be removed.\n\n" + ex.GetBaseException().Message, "Uninstall " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void RemoveShortcuts()
        {
            string desktop = DesktopShortcutPath;
            if (File.Exists(desktop)) File.Delete(desktop);

            string startMenu = StartMenuDirectory;
            if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
        }

        private static void RemoveSettings()
        {
            string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.AppName);
            string expected = Path.GetFullPath(settings).TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(expected)) Directory.Delete(expected, true);
        }

        internal static string StartMenuDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName); }
        }

        internal static string DesktopShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk"); }
        }

        internal static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class VantaUi
    {
        internal static readonly Color Black = Color.FromArgb(0, 0, 0);
        internal static readonly Color Surface = Color.FromArgb(12, 12, 12);
        internal static readonly Color White = Color.FromArgb(242, 242, 242);
        internal static readonly Color Muted = Color.FromArgb(155, 155, 155);
        internal static readonly Color Blue = Color.FromArgb(139, 213, 255);
        private static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        private static IntPtr FontMemory;
        private static IntPtr FontResource;
        private static bool FontLoaded;

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, ref uint fontCount);

        [DllImport("gdi32.dll")]
        private static extern bool RemoveFontMemResourceEx(IntPtr handle);

        static VantaUi()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vanta.Installer.Paytone.ttf"))
                {
                    if (stream == null) return;
                    byte[] bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) break;
                        offset += read;
                    }
                    FontMemory = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, FontMemory, bytes.Length);
                    uint fontCount = 0;
                    FontResource = AddFontMemResourceEx(FontMemory, (uint)bytes.Length, IntPtr.Zero, ref fontCount);
                    Fonts.AddMemoryFont(FontMemory, bytes.Length);
                    FontLoaded = fontCount > 0 && Fonts.Families.Length > 0;
                }
                AppDomain.CurrentDomain.ProcessExit += delegate
                {
                    Fonts.Dispose();
                    if (FontResource != IntPtr.Zero) RemoveFontMemResourceEx(FontResource);
                    if (FontMemory != IntPtr.Zero) Marshal.FreeCoTaskMem(FontMemory);
                };
            }
            catch { FontLoaded = false; }
        }

        internal static Font Heading(float size)
        {
            return FontLoaded ? new Font(Fonts.Families[0], size, FontStyle.Regular, GraphicsUnit.Point) : new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point);
        }

        internal static PictureBox Logo(int size)
        {
            PictureBox picture = new PictureBox { Size = new Size(size, size), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Black };
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vanta.Installer.Logo.png"))
                using (Image original = Image.FromStream(stream))
                    picture.Image = new Bitmap(original);
            }
            catch { }
            return picture;
        }

        internal static Button Button(string text, bool accent)
        {
            Button button = new Button
            {
                Text = text,
                Height = 42,
                Width = accent ? 166 : 98,
                FlatStyle = FlatStyle.Flat,
                BackColor = accent ? Surface : Black,
                ForeColor = White,
                Font = Heading(10.5f),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = accent ? Blue : Color.FromArgb(65, 65, 65);
            button.FlatAppearance.BorderSize = accent ? 2 : 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(24, 24, 24);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(34, 34, 34);
            return button;
        }

        internal static Label Label(string text, int x, int y, int width, int height, float size, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        internal static void PrepareForm(Form form, Size size, string title)
        {
            form.Text = title;
            form.ClientSize = size;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.BackColor = Black;
            form.ForeColor = White;
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
        }
    }

    internal sealed class InstallForm : Form
    {
        private readonly Label Description;
        private readonly Label Status;
        private readonly CheckBox DesktopShortcut;
        private readonly ProgressBar Progress;
        private readonly Button Primary;
        private readonly Button Secondary;
        private bool Installed;

        internal InstallForm()
        {
            VantaUi.PrepareForm(this, new Size(620, 438), Program.AppName + " Setup");

            PictureBox logo = VantaUi.Logo(76);
            logo.Location = new Point(32, 24);
            Controls.Add(logo);

            Label title = VantaUi.Label(Program.AppName, 128, 25, 450, 47, 27f, VantaUi.White);
            title.Font = VantaUi.Heading(27f);
            Controls.Add(title);
            Controls.Add(VantaUi.Label("SETUP  ·  VERSION " + Program.ShortVersion, 131, 73, 430, 22, 8f, VantaUi.Muted));
            Controls.Add(new Panel { Location = new Point(0, 116), Size = new Size(620, 1), BackColor = Color.FromArgb(39, 39, 39) });

            Label heading = VantaUi.Label(InstallerCore.IsInstalled ? "Update or repair Vanta" : "Install Vanta for Windows", 38, 143, 540, 32, 15f, VantaUi.White);
            heading.Font = VantaUi.Heading(15f);
            Controls.Add(heading);

            Description = VantaUi.Label(
                InstallerCore.IsInstalled
                    ? "Replace the installed app with this copy. Your settings stay exactly where they are."
                    : "Add Vanta to the Start menu and Windows Installed apps. No administrator access is needed.",
                38, 181, 542, 48, 10f, VantaUi.Muted);
            Controls.Add(Description);

            Controls.Add(VantaUi.Label("INSTALL LOCATION", 38, 239, 200, 18, 7.5f, VantaUi.Muted));
            Controls.Add(VantaUi.Label(Program.InstallDirectory, 38, 260, 542, 28, 9f, Color.FromArgb(205, 205, 205)));

            DesktopShortcut = new CheckBox
            {
                Text = "Create a desktop shortcut",
                Location = new Point(38, 296),
                Size = new Size(260, 28),
                Checked = true,
                ForeColor = Color.FromArgb(210, 210, 210),
                BackColor = VantaUi.Black,
                Font = new Font("Segoe UI", 9f),
                UseVisualStyleBackColor = false
            };
            Controls.Add(DesktopShortcut);

            Status = VantaUi.Label("Ready to install for your Windows account.", 38, 331, 542, 24, 8.5f, VantaUi.Muted);
            Controls.Add(Status);
            Progress = new ProgressBar { Location = new Point(38, 354), Size = new Size(542, 4), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28, Visible = false };
            Controls.Add(Progress);

            Secondary = VantaUi.Button("Cancel", false);
            Secondary.Location = new Point(308, 378);
            Secondary.Click += delegate { Close(); };
            Controls.Add(Secondary);

            Primary = VantaUi.Button(InstallerCore.IsInstalled ? "Update Vanta" : "Install Vanta", true);
            Primary.Location = new Point(414, 378);
            Primary.Click += PrimaryClick;
            Controls.Add(Primary);

            AcceptButton = Primary;
            CancelButton = Secondary;
        }

        private void PrimaryClick(object sender, EventArgs e)
        {
            if (Installed)
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = Program.AppPath, WorkingDirectory = Program.InstallDirectory, UseShellExecute = true });
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.GetBaseException().Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            try
            {
                Primary.Enabled = false;
                Secondary.Enabled = false;
                DesktopShortcut.Enabled = false;
                Status.Text = "Installing Vanta…";
                Progress.Visible = true;
                Refresh();
                Application.DoEvents();

                InstallerCore.Install(Program.InstallDirectory, true, DesktopShortcut.Checked, true);

                Installed = true;
                Progress.Visible = false;
                Description.Text = "Vanta is installed. Find it in the Start menu or remove it later from Windows Installed apps.";
                Status.Text = "Installation complete.";
                Primary.Text = "Open Vanta";
                Primary.Enabled = true;
                Secondary.Text = "Close";
                Secondary.Enabled = true;
                Program.Result = 0;
            }
            catch (Exception ex)
            {
                Progress.Visible = false;
                Status.Text = "Installation did not finish.";
                Primary.Enabled = true;
                Secondary.Enabled = true;
                DesktopShortcut.Enabled = true;
                MessageBox.Show(ex.GetBaseException().Message, Program.AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Program.Result = 1;
            }
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly CheckBox RemoveSettings;

        internal UninstallForm()
        {
            VantaUi.PrepareForm(this, new Size(520, 332), "Uninstall " + Program.AppName);

            PictureBox logo = VantaUi.Logo(64);
            logo.Location = new Point(30, 24);
            Controls.Add(logo);

            Label title = VantaUi.Label("Remove Vanta?", 113, 29, 365, 39, 23f, VantaUi.White);
            title.Font = VantaUi.Heading(23f);
            Controls.Add(title);
            Controls.Add(VantaUi.Label("This removes the app and its Start menu shortcuts.", 115, 69, 360, 26, 9f, VantaUi.Muted));
            Controls.Add(new Panel { Location = new Point(0, 112), Size = new Size(520, 1), BackColor = Color.FromArgb(39, 39, 39) });
            Controls.Add(VantaUi.Label("Your saved click settings can stay on this computer for a future install.", 34, 139, 450, 46, 10f, VantaUi.Muted));

            RemoveSettings = new CheckBox
            {
                Text = "Also remove my saved settings and cursor sequences",
                Location = new Point(34, 197),
                Size = new Size(430, 30),
                Checked = false,
                ForeColor = Color.FromArgb(210, 210, 210),
                BackColor = VantaUi.Black,
                Font = new Font("Segoe UI", 9f),
                UseVisualStyleBackColor = false
            };
            Controls.Add(RemoveSettings);

            Button cancel = VantaUi.Button("Cancel", false);
            cancel.Location = new Point(214, 259);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            Button remove = VantaUi.Button("Uninstall Vanta", true);
            remove.Location = new Point(320, 259);
            remove.Click += delegate
            {
                int result = InstallerCore.BeginUninstall(RemoveSettings.Checked, false);
                Program.Result = result;
                if (result == 0) Application.Exit();
            };
            Controls.Add(remove);
            AcceptButton = remove;
            CancelButton = cancel;
        }
    }
}
