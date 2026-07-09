using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace JunkyScreenShot
{
    /// <summary>
    /// Always-on-top borderless window showing a pinned screenshot.
    /// Drag with the left button, double-click to close, mouse wheel to resize.
    /// </summary>
    public partial class PinWindow : Window
    {
        private const double MinScale = 0.2;
        private const double MaxScale = 5.0;

        private readonly double _originalWidth;  // DIP size at 100%
        private readonly double _originalHeight;
        private double _zoom = 1.0;

        public PinWindow(BitmapSource image, Point screenPositionDip, Size sizeDip)
        {
            InitializeComponent();
            PinnedImage.Source = image;

            // Display at the same on-screen size as the original selection.
            _originalWidth = Math.Max(1, sizeDip.Width);
            _originalHeight = Math.Max(1, sizeDip.Height);
            PinnedImage.Width = _originalWidth;
            PinnedImage.Height = _originalHeight;

            Left = screenPositionDip.X;
            Top = screenPositionDip.Y;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ClickCount == 2)
            {
                Close(); // double-click closes the pinned image
                return;
            }
            DragMove();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            // Optional feature: wheel up enlarges, wheel down shrinks.
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            _zoom = Math.Clamp(_zoom * factor, MinScale, MaxScale);
            PinnedImage.Width = _originalWidth * _zoom;
            PinnedImage.Height = _originalHeight * _zoom;
        }
    }
}
