using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace JunkyScreenShot
{
    /// <summary>
    /// Application entry point. No main window: the app lives in the system tray,
    /// listens for the F1 global hotkey and opens the capture overlay on demand.
    /// </summary>
    public partial class App : Application
    {
        private const int HotkeyId = 1;

        private WinForms.NotifyIcon? _trayIcon;
        private System.Drawing.Icon? _appIcon;
        private HwndSource? _hotkeyWindow;
        private CaptureOverlay? _overlay;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CreateTrayIcon();
            RegisterGlobalHotkey();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_hotkeyWindow != null)
            {
                NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
                _hotkeyWindow.Dispose();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            _appIcon?.Dispose();
            base.OnExit(e);
        }

        // ---- QuickSave default folder (persisted as a one-line text file in %AppData%) ----

        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JunkyScreenShot", "quicksave_folder.txt");

        /// <summary>Returns the user-chosen QuickSave folder, or Pictures\JunkyScreenShot by default.</summary>
        public static string GetQuickSaveFolder()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string saved = File.ReadAllText(SettingsFilePath).Trim();
                    if (saved.Length > 0)
                        return saved;
                }
            }
            catch
            {
                // Unreadable settings file: fall through to the default folder.
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "JunkyScreenShot");
        }

        public static void SetQuickSaveFolder(string folder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(SettingsFilePath, folder);
        }

        /// <summary>Tray menu action: pick and persist the QuickSave default folder.</summary>
        private void ChooseQuickSaveFolder()
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Choose the default folder for QuickSave screenshots",
                UseDescriptionForTitle = true,
                SelectedPath = GetQuickSaveFolder()
            };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                try
                {
                    SetQuickSaveFolder(dialog.SelectedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to save the setting: " + ex.Message,
                        "JunkyScreenShot", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CreateTrayIcon()
        {
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Capture (F1)", null, (_, _) => StartCapture());
            menu.Items.Add("Set QuickSave Folder...", null, (_, _) => ChooseQuickSaveFolder());
            menu.Items.Add("Exit", null, (_, _) => Shutdown());

            if (Environment.ProcessPath is string processPath)
                _appIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);

            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
                Text = "JunkyScreenShot - Press F1 to capture",
                Visible = true,
                ContextMenuStrip = menu
            };
            _trayIcon.DoubleClick += (_, _) => StartCapture();
        }

        private void RegisterGlobalHotkey()
        {
            // A message-only window (parent = HWND_MESSAGE) receives WM_HOTKEY
            // without ever being visible. Simpler than a hidden WPF window.
            var parameters = new HwndSourceParameters("JunkyScreenShotHotkeyWindow");
            parameters.ParentWindow = new IntPtr(-3); // HWND_MESSAGE
            _hotkeyWindow = new HwndSource(parameters);
            _hotkeyWindow.AddHook(HotkeyWndProc);

            if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, 0, NativeMethods.VK_F1))
            {
                // Most likely another app already owns F1. The tray menu still works.
                MessageBox.Show(
                    "Failed to register the F1 global hotkey (maybe used by another app).\n" +
                    "You can still capture from the tray icon menu.",
                    "JunkyScreenShot", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                StartCapture();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void StartCapture()
        {
            if (_overlay != null)
                return; // capture mode already active, ignore repeated F1

            try
            {
                _overlay = new CaptureOverlay();
                _overlay.Closed += (_, _) => _overlay = null;
                _overlay.Show();
                _overlay.Activate(); // make sure the overlay gets keyboard focus for Esc
            }
            catch (Exception ex)
            {
                _overlay = null;
                MessageBox.Show("Failed to start capture: " + ex.Message,
                    "JunkyScreenShot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
