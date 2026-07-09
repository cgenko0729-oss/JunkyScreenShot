using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinForms = System.Windows.Forms;

namespace JunkyScreenShot
{
    /// <summary>
    /// Fullscreen capture overlay. Shows a frozen screenshot with a dark layer on top,
    /// highlights the window under the cursor, supports manual drag selection,
    /// pen annotation on the selection, and a Copy / Save / Pin / Cancel toolbar.
    /// </summary>
    public partial class CaptureOverlay : Window
    {
        private enum CaptureState
        {
            Hover,    // moving the mouse: auto-detect the window under the cursor
            Dragging, // holding the left button: drawing a manual selection rectangle
            Selected  // area confirmed: toolbar is visible
        }

        private const double MinSelectionPixels = 5; // ignore selections smaller than 5 x 5 px

        private BitmapSource _screenshot = null!;
        private double _scale = 1.0;          // physical pixels per WPF DIP
        private IntPtr _ownHwnd = IntPtr.Zero;

        private CaptureState _state = CaptureState.Hover;
        private bool _mouseDown;
        private Point _dragStart;             // DIP, overlay coordinates
        private Rect _selection = Rect.Empty; // DIP, overlay coordinates

        // ---- Pen annotation state ----
        private static readonly Color[] PaletteColors =
        {
            Colors.Red, Colors.Orange, Colors.Gold, Colors.LimeGreen, Colors.Green,
            Colors.Cyan, Colors.DodgerBlue, Colors.Blue, Colors.Purple, Colors.Magenta,
            Colors.HotPink, Colors.Brown, Colors.Black, Colors.Gray, Colors.White
        };
        private static readonly double[] PenSizes = { 2, 4, 8 };

        private bool _penMode;
        private Brush _penBrush = Brushes.Red;
        private double _penThickness = 4;
        private Polyline? _currentStroke;               // stroke being drawn right now
        private readonly List<Polyline> _strokes = new(); // finished strokes, for undo & export
        private readonly List<Button> _sizeButtons = new();
        private readonly List<Button> _colorButtons = new();

        public CaptureOverlay()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => _ownHwnd = new WindowInteropHelper(this).Handle;
            CaptureScreen();
            BuildPenPalette();
        }

        // ---- Screen capture / coordinate helpers ----

        /// <summary>Grabs the primary screen and sizes the overlay to cover it.</summary>
        private void CaptureScreen()
        {
            var bounds = WinForms.Screen.PrimaryScreen?.Bounds
                         ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

            using (var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height))
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size);
                }

                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    _screenshot = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    NativeMethods.DeleteObject(hBitmap); // avoid GDI handle leak
                }
            }
            if (_screenshot.CanFreeze)
                _screenshot.Freeze();

            // Window size is in DIPs; the bitmap is physical pixels. Keep the ratio for conversions.
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            _scale = bounds.Width / Width;

            ScreenshotImage.Source = _screenshot;
            UpdateDimLayer(Rect.Empty);
        }

        /// <summary>Redraws the dark layer, cutting a bright hole over the selection.</summary>
        private void UpdateDimLayer(Rect holeDip)
        {
            var full = new RectangleGeometry(new Rect(0, 0, Width, Height));
            DimPath.Data = holeDip.IsEmpty || holeDip.Width <= 0 || holeDip.Height <= 0
                ? full
                : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(holeDip));
        }

        /// <summary>Shows the blue border + size label for the given DIP rectangle.</summary>
        private void ShowSelection(Rect dip)
        {
            _selection = dip;

            Canvas.SetLeft(SelectionBorder, dip.X);
            Canvas.SetTop(SelectionBorder, dip.Y);
            SelectionBorder.Width = dip.Width;
            SelectionBorder.Height = dip.Height;
            SelectionBorder.Visibility = Visibility.Visible;

            SizeText.Text = $"{Math.Round(dip.Width * _scale)} x {Math.Round(dip.Height * _scale)} px";
            SizeLabel.Visibility = Visibility.Visible;
            SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelY = dip.Y - SizeLabel.DesiredSize.Height - 4;
            if (labelY < 0)
                labelY = dip.Y + 4; // no room above: put it inside the selection
            Canvas.SetLeft(SizeLabel, Math.Max(0, dip.X));
            Canvas.SetTop(SizeLabel, labelY);

            UpdateDimLayer(dip);
        }

        private void HideSelection()
        {
            _selection = Rect.Empty;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            UpdateDimLayer(Rect.Empty);
        }

        // ---- Window auto detection ----

        /// <summary>
        /// Finds the top-level window under the given physical-pixel point, walking
        /// EnumWindows in z-order (top to bottom). Skips our own overlay, minimized
        /// windows and cloaked (invisible UWP) windows.
        /// </summary>
        private Rect FindWindowRectAt(int x, int y)
        {
            Rect result = Rect.Empty;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (hwnd == _ownHwnd)
                    return true;
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                    return true;
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED,
                        out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                // Prefer DWM extended frame bounds (excludes the drop shadow).
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                        out NativeMethods.RECT rect, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
                {
                    if (!NativeMethods.GetWindowRect(hwnd, out rect))
                        return true;
                }

                if (rect.Width <= 0 || rect.Height <= 0 || !rect.Contains(x, y))
                    return true;

                result = new Rect(rect.Left, rect.Top, rect.Width, rect.Height); // physical pixels
                return false; // first hit in z-order wins, stop enumerating
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>Detects and highlights the window under the cursor (hover mode).</summary>
        private void HighlightWindowAt(Point dipPos)
        {
            var pixelRect = FindWindowRectAt((int)(dipPos.X * _scale), (int)(dipPos.Y * _scale));
            if (pixelRect.IsEmpty)
            {
                HideSelection();
                return;
            }

            // Physical pixels -> overlay DIPs, clamped to the overlay bounds.
            var dip = new Rect(pixelRect.X / _scale, pixelRect.Y / _scale,
                               pixelRect.Width / _scale, pixelRect.Height / _scale);
            dip.Intersect(new Rect(0, 0, Width, Height));
            if (dip.IsEmpty || dip.Width <= 0 || dip.Height <= 0)
            {
                HideSelection();
                return;
            }
            ShowSelection(dip);
        }

        // ---- Mouse / keyboard interaction ----

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var pos = e.GetPosition(this);
            // Note: clicks on the toolbar/palette buttons are handled by the buttons and never reach here.

            if (_state == CaptureState.Selected && _penMode)
            {
                // Pen tool: start a new stroke inside the selection.
                if (_selection.Contains(pos))
                {
                    StartStroke(pos);
                    CaptureMouse();
                }
                return; // never restart the selection while the pen is active
            }

            if (_state == CaptureState.Selected)
            {
                // Start a new selection: drop toolbar and any strokes from the old one.
                Toolbar.Visibility = Visibility.Collapsed;
                SetPenMode(false);
                ClearStrokes();
            }

            _mouseDown = true;
            _dragStart = pos;
            _state = CaptureState.Hover; // becomes Dragging once the mouse actually moves
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var pos = e.GetPosition(this);

            if (_currentStroke != null)
            {
                _currentStroke.Points.Add(pos);
                return;
            }

            if (_mouseDown)
            {
                // Small jitter is still a "click"; real movement switches to drag mode.
                if (_state != CaptureState.Dragging &&
                    (Math.Abs(pos.X - _dragStart.X) > 3 || Math.Abs(pos.Y - _dragStart.Y) > 3))
                {
                    _state = CaptureState.Dragging;
                }
                if (_state == CaptureState.Dragging)
                    ShowSelection(new Rect(_dragStart, pos));
            }
            else if (_state == CaptureState.Hover)
            {
                HighlightWindowAt(pos);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_currentStroke != null)
            {
                EndStroke();
                ReleaseMouseCapture();
                return;
            }

            if (!_mouseDown)
                return;
            _mouseDown = false;
            ReleaseMouseCapture();

            if (_state == CaptureState.Dragging)
            {
                var rect = new Rect(_dragStart, e.GetPosition(this));
                if (rect.Width * _scale >= MinSelectionPixels && rect.Height * _scale >= MinSelectionPixels)
                {
                    ConfirmSelection(rect);
                }
                else
                {
                    _state = CaptureState.Hover; // too tiny, go back to hover mode
                    HideSelection();
                }
            }
            else if (!_selection.IsEmpty)
            {
                // Plain click: confirm the currently highlighted (auto-detected) window area.
                ConfirmSelection(_selection);
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_penMode)
                SetPenMode(false); // right click first leaves pen mode...
            else
                Close();           // ...then cancels capture, like Esc
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
                return;
            }
            if (_state != CaptureState.Selected)
                return;
            // Ctrl+C copies the confirmed selection, same as the Copy button.
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                CopySelectionAndClose();
            }
            // Ctrl+Z removes the last pen stroke.
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                UndoStroke();
            }
        }

        // ---- Selection confirmation & toolbar ----

        private void ConfirmSelection(Rect dip)
        {
            dip.Intersect(new Rect(0, 0, Width, Height));
            _state = CaptureState.Selected;
            ShowSelection(dip);
            // Keep strokes visually inside the selection.
            StrokeLayer.Clip = new RectangleGeometry(dip);
            ShowToolbar();
        }

        private void ShowToolbar()
        {
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = Toolbar.DesiredSize.Width;
            double h = Toolbar.DesiredSize.Height;

            // Prefer below-right of the selection; flip above if there is no room.
            double x = Math.Min(Math.Max(0, _selection.Right - w), Width - w);
            double y = _selection.Bottom + 8;
            if (y + h > Height)
                y = _selection.Y - h - 8;
            if (y < 0)
                y = Math.Max(0, _selection.Bottom - h - 8); // huge selection: put it inside

            Canvas.SetLeft(Toolbar, x);
            Canvas.SetTop(Toolbar, y);
            PositionPenPalette();
        }

        // ---- Pen tool ----

        /// <summary>Creates the size dots and color swatches inside the palette panel.</summary>
        private void BuildPenPalette()
        {
            foreach (double size in PenSizes)
            {
                var button = new Button
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(2),
                    Padding = new Thickness(0),
                    Background = Brushes.White,
                    Content = new Ellipse { Width = size + 3, Height = size + 3, Fill = Brushes.Black }
                };
                double captured = size;
                button.Click += (_, _) => { _penThickness = captured; MarkSelected(_sizeButtons, button); };
                _sizeButtons.Add(button);
                PalettePanel.Children.Add(button);
            }

            PalettePanel.Children.Add(new Border
            {
                Width = 1,
                Background = Brushes.Gray,
                Margin = new Thickness(4, 2, 4, 2)
            });

            foreach (var color in PaletteColors)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var button = new Button
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(2),
                    Background = brush,
                    BorderBrush = Brushes.DarkGray
                };
                button.Click += (_, _) =>
                {
                    _penBrush = brush;
                    PenButton.Foreground = brush; // show the active color on the Pen button
                    MarkSelected(_colorButtons, button);
                };
                _colorButtons.Add(button);
                PalettePanel.Children.Add(button);
            }

            // Defaults: medium size, red.
            MarkSelected(_sizeButtons, _sizeButtons[1]);
            MarkSelected(_colorButtons, _colorButtons[0]);
        }

        /// <summary>Thick blue border on the chosen palette button, thin on the others.</summary>
        private static void MarkSelected(List<Button> group, Button chosen)
        {
            foreach (var b in group)
            {
                b.BorderBrush = b == chosen ? Brushes.DodgerBlue : Brushes.DarkGray;
                b.BorderThickness = b == chosen ? new Thickness(2) : new Thickness(1);
            }
        }

        private void SetPenMode(bool on)
        {
            _penMode = on;
            PenPalette.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                PenButton.Background = Brushes.LightSkyBlue;
                PenButton.Foreground = _penBrush;
            }
            else
            {
                // Restore the default button look by clearing the local values.
                PenButton.ClearValue(BackgroundProperty);
                PenButton.ClearValue(ForegroundProperty);
            }
            Cursor = on ? Cursors.Pen : Cursors.Cross;
            if (on)
                PositionPenPalette();
        }

        private void PositionPenPalette()
        {
            if (PenPalette.Visibility != Visibility.Visible)
                return;
            PenPalette.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double x = Math.Min(Canvas.GetLeft(Toolbar), Width - PenPalette.DesiredSize.Width);
            double y = Canvas.GetTop(Toolbar) + Toolbar.DesiredSize.Height + 4;
            if (y + PenPalette.DesiredSize.Height > Height)
                y = Canvas.GetTop(Toolbar) - PenPalette.DesiredSize.Height - 4; // no room below: above
            Canvas.SetLeft(PenPalette, Math.Max(0, x));
            Canvas.SetTop(PenPalette, Math.Max(0, y));
        }

        private void StartStroke(Point pos)
        {
            _currentStroke = new Polyline
            {
                Stroke = _penBrush,
                StrokeThickness = _penThickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _currentStroke.Points.Add(pos);
            // A tiny second point so a single click still leaves a visible dot.
            _currentStroke.Points.Add(new Point(pos.X + 0.1, pos.Y + 0.1));
            StrokeLayer.Children.Add(_currentStroke);
        }

        private void EndStroke()
        {
            if (_currentStroke != null)
                _strokes.Add(_currentStroke);
            _currentStroke = null;
        }

        private void UndoStroke()
        {
            if (_strokes.Count == 0)
                return;
            var last = _strokes[^1];
            _strokes.RemoveAt(_strokes.Count - 1);
            StrokeLayer.Children.Remove(last);
        }

        private void ClearStrokes()
        {
            _strokes.Clear();
            _currentStroke = null;
            StrokeLayer.Children.Clear();
        }

        private void PenButton_Click(object sender, RoutedEventArgs e)
        {
            SetPenMode(!_penMode);
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            UndoStroke();
        }

        // ---- Image export ----

        /// <summary>
        /// Crops the frozen screenshot to the current selection and, if the user drew
        /// anything, renders the pen strokes on top (physical pixels).
        /// </summary>
        private BitmapSource GetSelectedImage()
        {
            int x = Math.Max(0, (int)Math.Round(_selection.X * _scale));
            int y = Math.Max(0, (int)Math.Round(_selection.Y * _scale));
            int w = Math.Max(1, Math.Min(_screenshot.PixelWidth - x, (int)Math.Round(_selection.Width * _scale)));
            int h = Math.Max(1, Math.Min(_screenshot.PixelHeight - y, (int)Math.Round(_selection.Height * _scale)));
            var cropped = new CroppedBitmap(_screenshot, new Int32Rect(x, y, w, h));

            if (_strokes.Count == 0)
                return cropped;

            // Composite: cropped screenshot + strokes, both in physical pixel coordinates.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(cropped, new Rect(0, 0, w, h));
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                foreach (var stroke in _strokes)
                {
                    var pen = new Pen(stroke.Stroke, stroke.StrokeThickness * _scale)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    var points = stroke.Points
                        .Select(p => new Point((p.X - _selection.X) * _scale, (p.Y - _selection.Y) * _scale))
                        .ToList();
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(points[0], false, false);
                        g.PolyLineTo(points.Skip(1).ToList(), true, true);
                    }
                    dc.DrawGeometry(null, pen, geometry);
                }
                dc.Pop();
            }

            var rendered = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            return rendered;
        }

        // ---- Toolbar actions ----

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CopySelectionAndClose();
        }

        /// <summary>Copies the current selection to the clipboard and leaves capture mode.
        /// Shared by the Copy button and the Ctrl+C shortcut.</summary>
        private void CopySelectionAndClose()
        {
            try
            {
                Clipboard.SetImage(GetSelectedImage());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to copy to clipboard: " + ex.Message, "JunkyScreenShot");
            }
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".png",
                Filter = "PNG Image|*.png"
            };
            if (dialog.ShowDialog(this) != true)
                return; // user cancelled the dialog: stay in capture mode

            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(GetSelectedImage()));
                using (var stream = File.Create(dialog.FileName))
                {
                    encoder.Save(stream);
                }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to save file: " + ex.Message, "JunkyScreenShot");
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // Overlay sits at (0,0) of the primary screen, so selection DIPs == screen DIPs.
            var pin = new PinWindow(GetSelectedImage(),
                new Point(Left + _selection.X, Top + _selection.Y), _selection.Size);
            pin.Show();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
