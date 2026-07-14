using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("KeyGlow Keeper")]
[assembly: AssemblyDescription("Keeps keyboard backlighting on for compatible Lenovo laptops.")]
[assembly: AssemblyCompany("Banditor")]
[assembly: AssemblyProduct("KeyGlow Keeper")]
[assembly: AssemblyCopyright("Copyright © 2026 Banditor")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: ComVisible(false)]

namespace KeyGlowKeeper
{
    internal static class Program
    {
        private const string MutexName = "Local\\KeyGlowKeeper";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                    return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
            }
        }
    }

    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const string SettingsKeyPath = @"Software\KeyGlowKeeper";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "KeyGlowKeeper";
        private const string GitHubUrl = "https://github.com/Banditor/keyglow-keeper";
        private static readonly bool IsHebrew =
            String.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "he", StringComparison.OrdinalIgnoreCase);

        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem keeperItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem brightnessMenu;
        private readonly ToolStripMenuItem lowBrightnessItem;
        private readonly ToolStripMenuItem highBrightnessItem;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Icon enabledIcon;
        private readonly Icon disabledIcon;
        private readonly LenovoBacklightController controller;

        private bool keeperEnabled;
        private bool tickRunning;
        private bool errorShown;
        private string selectedBrightness;

        public TrayApplicationContext()
        {
            enabledIcon = CreateTrayIcon(true);
            disabledIcon = CreateTrayIcon(false);

            menu = new ContextMenuStrip();
            menu.RightToLeft = IsHebrew ? RightToLeft.Yes : RightToLeft.No;

            keeperItem = new ToolStripMenuItem(Localize("Keep keyboard backlight on", "תאורת מקלדת קבועה"));
            keeperItem.CheckOnClick = false;
            keeperItem.Click += delegate { SetKeeperEnabled(!keeperEnabled); };
            menu.Items.Add(keeperItem);

            ToolStripMenuItem turnOnNowItem = new ToolStripMenuItem(Localize("Turn on now", "הדלק תאורה עכשיו"));
            turnOnNowItem.Click += delegate { SetHardware(true); };
            menu.Items.Add(turnOnNowItem);

            brightnessMenu = new ToolStripMenuItem(Localize("Brightness", "עוצמת תאורה"));
            lowBrightnessItem = new ToolStripMenuItem(Localize("Low", "נמוכה"));
            highBrightnessItem = new ToolStripMenuItem(Localize("High", "גבוהה"));
            lowBrightnessItem.Click += delegate { SetBrightness("Level_1"); };
            highBrightnessItem.Click += delegate { SetBrightness("Level_2"); };
            brightnessMenu.DropDownItems.Add(lowBrightnessItem);
            brightnessMenu.DropDownItems.Add(highBrightnessItem);
            brightnessMenu.Visible = false;
            menu.Items.Add(brightnessMenu);

            menu.Items.Add(new ToolStripSeparator());

            startupItem = new ToolStripMenuItem(Localize("Start with Windows", "הפעל עם Windows"));
            startupItem.CheckOnClick = false;
            startupItem.Click += delegate { SetRunAtStartup(!IsRunAtStartupEnabled()); };
            menu.Items.Add(startupItem);

            ToolStripMenuItem githubItem = new ToolStripMenuItem(Localize("Open GitHub page", "פתח את עמוד GitHub"));
            githubItem.Click += delegate { OpenGitHubPage(); };
            menu.Items.Add(githubItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem(Localize("Exit", "יציאה"));
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(exitItem);

            menu.Opening += delegate
            {
                keeperItem.Checked = keeperEnabled;
                startupItem.Checked = IsRunAtStartupEnabled();
                lowBrightnessItem.Checked = selectedBrightness == "Level_1";
                highBrightnessItem.Checked = selectedBrightness == "Level_2";
            };

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = menu;
            trayIcon.Visible = true;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.Button == MouseButtons.Left)
                    SetKeeperEnabled(!keeperEnabled);
            };

            keeperEnabled = ReadKeeperEnabled();
            selectedBrightness = ReadBrightness();
            EnsureInitialStartupSetting();
            UpdateTrayAppearance();

            try
            {
                controller = new LenovoBacklightController();
                brightnessMenu.Visible = controller.SupportsTwoLevels;
            }
            catch (Exception ex)
            {
                Log("Controller initialization failed", ex);
            }

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 3000;
            timer.Tick += delegate { KeeperTick(); };
            timer.Start();

            if (keeperEnabled)
                SetHardware(true);
        }

        private void KeeperTick()
        {
            if (!keeperEnabled || tickRunning || controller == null)
                return;

            tickRunning = true;
            try
            {
                string currentLevel = controller.GetLevel();
                string expectedLevel = controller.GetEffectiveOnLevel(selectedBrightness);
                if (String.IsNullOrEmpty(currentLevel)
                    || String.Equals(currentLevel, "Off", StringComparison.OrdinalIgnoreCase)
                    || !String.Equals(currentLevel, expectedLevel, StringComparison.OrdinalIgnoreCase))
                    SetHardware(true);
                else if (errorShown)
                {
                    errorShown = false;
                    UpdateTrayAppearance();
                }
            }
            catch (Exception ex)
            {
                HandleHardwareError(ex);
            }
            finally
            {
                tickRunning = false;
            }
        }

        private void SetKeeperEnabled(bool enabled)
        {
            keeperEnabled = enabled;
            WriteKeeperEnabled(enabled);
            UpdateTrayAppearance();

            if (enabled)
                SetHardware(true);
            else
                SetHardware(false);
        }

        private void SetBrightness(string level)
        {
            selectedBrightness = level;
            WriteBrightness(level);
            lowBrightnessItem.Checked = level == "Level_1";
            highBrightnessItem.Checked = level == "Level_2";
            if (keeperEnabled)
                SetHardware(true);
        }

        private void SetHardware(bool turnOn)
        {
            if (controller == null)
                return;

            try
            {
                if (!controller.SetOn(turnOn, selectedBrightness))
                    throw new InvalidOperationException("Lenovo returned a non-success result.");

                errorShown = false;
                UpdateTrayAppearance();
            }
            catch (Exception ex)
            {
                HandleHardwareError(ex);
            }
        }

        private void HandleHardwareError(Exception ex)
        {
            Log("Keyboard backlight operation failed", ex);
            if (!errorShown)
            {
                errorShown = true;
                UpdateTrayAppearance();
            }
        }

        private void UpdateTrayAppearance()
        {
            keeperItem.Checked = keeperEnabled;
            trayIcon.Icon = keeperEnabled && !errorShown ? enabledIcon : disabledIcon;
            trayIcon.Text = keeperEnabled
                ? (errorShown
                    ? Localize("Keyboard backlight: error", "תאורת מקלדת: שגיאה")
                    : Localize("KeyGlow Keeper: active", "תאורת מקלדת קבועה: פעילה"))
                : Localize("KeyGlow Keeper: off", "תאורת מקלדת קבועה: כבויה");
        }

        private static string Localize(string english, string hebrew)
        {
            return IsHebrew ? hebrew : english;
        }

        private static void OpenGitHubPage()
        {
            try
            {
                Process.Start(GitHubUrl);
            }
            catch
            {
            }
        }

        private static bool ReadKeeperEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
            {
                object value = key.GetValue("KeeperEnabled");
                if (value == null)
                {
                    key.SetValue("KeeperEnabled", 1, RegistryValueKind.DWord);
                    return true;
                }

                return Convert.ToInt32(value) != 0;
            }
        }

        private static void WriteKeeperEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                key.SetValue("KeeperEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        }

        private static string ReadBrightness()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
            {
                string value = Convert.ToString(key.GetValue("BrightnessLevel", "Level_2"));
                return value == "Level_1" ? "Level_1" : "Level_2";
            }
        }

        private static void WriteBrightness(string level)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                key.SetValue("BrightnessLevel", level, RegistryValueKind.String);
        }

        private static bool IsRunAtStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                return key != null && key.GetValue(RunValueName) != null;
        }

        private static void EnsureInitialStartupSetting()
        {
            using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
            {
                if (settings.GetValue("StartupConfigured") != null)
                    return;

                SetRunAtStartup(true);
                settings.SetValue("StartupConfigured", 1, RegistryValueKind.DWord);
            }
        }

        private static void SetRunAtStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --startup", RegistryValueKind.String);
                else
                    key.DeleteValue(RunValueName, false);
            }
        }

        private void ExitApplication()
        {
            timer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            menu.Dispose();
            enabledIcon.Dispose();
            disabledIcon.Dispose();
            ExitThread();
        }

        private static Icon CreateTrayIcon(bool enabled)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                Color fill = enabled ? Color.FromArgb(255, 193, 7) : Color.FromArgb(120, 120, 120);
                using (SolidBrush brush = new SolidBrush(fill))
                    graphics.FillEllipse(brush, 7, 3, 18, 18);

                using (Pen pen = new Pen(Color.FromArgb(40, 40, 40), 2.2f))
                {
                    graphics.DrawEllipse(pen, 7, 3, 18, 18);
                    graphics.DrawLine(pen, 12, 21, 12, 25);
                    graphics.DrawLine(pen, 20, 21, 20, 25);
                    graphics.DrawLine(pen, 12, 25, 20, 25);
                    graphics.DrawLine(pen, 13, 28, 19, 28);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static void Log(string message, Exception ex)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KeyGlowKeeper");
                Directory.CreateDirectory(directory);
                string line = DateTime.Now.ToString("s") + " " + message + Environment.NewLine + ex + Environment.NewLine;
                File.AppendAllText(Path.Combine(directory, "keeper.log"), line);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    internal sealed class LenovoBacklightController
    {
        private readonly string addinDirectory;
        private readonly string vantageServiceDirectory;
        private readonly Assembly contractAssembly;
        private readonly Type agentType;
        private readonly object agent;

        public bool SupportsTwoLevels { get; private set; }

        public LenovoBacklightController()
        {
            addinDirectory = FindNewestVersionDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Lenovo", "Vantage", "Addins", "IdeaNotebookAddin"),
                "IdeaNotebookAddin.dll");

            vantageServiceDirectory = FindNewestVersionDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Lenovo", "VantageService"),
                "Lenovo.VantageService.Utilities.dll");

            AppDomain.CurrentDomain.AssemblyResolve += ResolveLenovoAssembly;

            contractAssembly = Assembly.LoadFrom(Path.Combine(addinDirectory, "KeyboardContract.dll"));
            Assembly addinAssembly = Assembly.LoadFrom(Path.Combine(addinDirectory, "IdeaNotebookAddin.dll"));
            agentType = addinAssembly.GetType("IdeaNotebookAddin.IdeaNotebookAgent", true);
            agent = agentType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[0]);

            bool handleAvailable = Convert.ToBoolean(agentType.GetMethod("CheckHandle").Invoke(agent, new object[0]));
            bool backlightSupported = Convert.ToBoolean(agentType.GetMethod("IsSupportBacklight").Invoke(agent, new object[0]));
            if (!handleAvailable || !backlightSupported)
                throw new NotSupportedException("The Lenovo keyboard backlight interface is unavailable.");

            string maximumLevel = GetSettingValue("KeyboardBacklightLevel");
            SupportsTwoLevels = !String.IsNullOrEmpty(maximumLevel)
                && maximumLevel.StartsWith("TwoLevels", StringComparison.OrdinalIgnoreCase);
        }

        public string GetLevel()
        {
            return GetSettingValue("KeyboardBacklightStatus");
        }

        public string GetEffectiveOnLevel(string requestedLevel)
        {
            return SupportsTwoLevels && requestedLevel == "Level_2" ? "Level_2" : "Level_1";
        }

        private string GetSettingValue(string settingName)
        {
            object response = agentType.GetMethod("GetBacklightStatus").Invoke(agent, new object[0]);
            object list = response.GetType().GetProperty("List").GetValue(response, null);
            IEnumerable items = (IEnumerable)list.GetType().GetProperty("Items").GetValue(list, null);

            foreach (object item in items)
            {
                string key = Convert.ToString(item.GetType().GetProperty("key").GetValue(item, null));
                if (!String.Equals(key, settingName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return Convert.ToString(item.GetType().GetProperty("value").GetValue(item, null));
            }

            return null;
        }

        public bool SetOn(bool turnOn, string requestedLevel)
        {
            Type requestType = contractAssembly.GetType("Lenovo.Modern.Contracts.Keyboard.KeyboardSettingsRequest", true);
            Type listType = contractAssembly.GetType("Lenovo.Modern.Contracts.Keyboard.SettingList", true);
            Type settingType = contractAssembly.GetType("Lenovo.Modern.Contracts.Keyboard.Setting", true);

            object request = Activator.CreateInstance(requestType);
            object list = Activator.CreateInstance(listType);
            object setting = Activator.CreateInstance(settingType);

            settingType.GetProperty("key").SetValue(setting, "KeyboardBacklightStatus", null);
            settingType.GetProperty("value").SetValue(
                setting,
                turnOn ? GetEffectiveOnLevel(requestedLevel) : "Off",
                null);

            Type genericListType = typeof(System.Collections.Generic.List<>).MakeGenericType(settingType);
            object items = Activator.CreateInstance(genericListType);
            genericListType.GetMethod("Add").Invoke(items, new object[] { setting });
            listType.GetProperty("Items").SetValue(list, items, null);
            requestType.GetProperty("List").SetValue(request, list, null);

            object response = agentType.GetMethod("SetBacklightStatus").Invoke(agent, new object[] { request });
            string result = Convert.ToString(response.GetType().GetProperty("ErrorCode").GetValue(response, null));
            return String.Equals(result, "Success", StringComparison.OrdinalIgnoreCase);
        }

        private Assembly ResolveLenovoAssembly(object sender, ResolveEventArgs args)
        {
            string fileName = new AssemblyName(args.Name).Name + ".dll";
            string[] directories = new string[] { addinDirectory, vantageServiceDirectory };

            foreach (string directory in directories)
            {
                if (String.IsNullOrEmpty(directory))
                    continue;

                string path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }

            return null;
        }

        private static string FindNewestVersionDirectory(string root, string requiredFile)
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException(root);

            DirectoryInfo best = null;
            foreach (DirectoryInfo directory in new DirectoryInfo(root).GetDirectories())
            {
                if (!File.Exists(Path.Combine(directory.FullName, requiredFile)))
                    continue;

                if (best == null || directory.LastWriteTimeUtc > best.LastWriteTimeUtc)
                    best = directory;
            }

            if (best == null)
                throw new FileNotFoundException(requiredFile + " was not found below " + root);

            return best.FullName;
        }
    }
}
