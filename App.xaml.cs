using System;
using System.Windows;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace JunkyScreenShot
{
    /// <summary>
    /// Application entry point. No main window: the app lives in the system tray,
    /// listens for the F2 global hotkey and opens the capture overlay on demand.
    /// </summary>
    public partial class App : Application
    {
        private const int HotkeyId = 1;

        private WinForms.NotifyIcon? _trayIcon;
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
            base.OnExit(e);
        }

        private void CreateTrayIcon()
        {
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Capture (F2)", null, (_, _) => StartCapture());
            menu.Items.Add("Exit", null, (_, _) => Shutdown());

            _trayIcon = new WinForms.NotifyIcon
            {
                // TODO: replace with a custom .ico later; system icon keeps v1 dependency-free.
                Icon = System.Drawing.SystemIcons.Application,
                Text = "JunkyScreenShot - Press F2 to capture",
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

            if (!NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, 0, NativeMethods.VK_F2))
            {
                // Most likely another app already owns F2. The tray menu still works.
                MessageBox.Show(
                    "Failed to register the F2 global hotkey (maybe used by another app).\n" +
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
                return; // capture mode already active, ignore repeated F2

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
