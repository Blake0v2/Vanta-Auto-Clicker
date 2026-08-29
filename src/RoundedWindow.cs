using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vanta
{
    internal static class RoundedWindow
    {
        // Window.AllowsTransparency removes the rectangular native backing surface.
        // Border.CornerRadius alone does not clip its children, so clip the inner
        // content as well; recompute in WPF units after resizing or a DPI change.
        public static void Attach(Window window, Border shell, FrameworkElement content)
        {
            Action update = () =>
            {
                double radius = Math.Max(0, shell.CornerRadius.TopLeft - shell.BorderThickness.Left);
                content.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, content.ActualWidth), Math.Max(0, content.ActualHeight)), radius, radius);
            };
            content.SizeChanged += (s, e) => update();
            window.Loaded += (s, e) => update();
        }
    }
}
