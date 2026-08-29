using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Vanta
{
    internal static class UiMotion
    {
        public static bool Enabled { get { return SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; } }

        public static void Fade(FrameworkElement element, double opacity, int milliseconds, Action completed)
        {
            double from = element.Opacity;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = opacity;
            if (!Enabled || milliseconds == 0)
            {
                if (completed != null) completed();
                return;
            }
            var animation = new DoubleAnimation(from, opacity, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            if (completed != null) animation.Completed += (s, e) => completed();
            element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        public static void Reveal(FrameworkElement element, int milliseconds, double offset)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = Enabled ? 0 : 1;
            var transform = new TranslateTransform();
            element.RenderTransform = transform;
            if (!Enabled) return;
            Fade(element, 1, milliseconds, null);
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            }, HandoffBehavior.SnapshotAndReplace);
        }

        public static void Resize(Window window, double width, double height, double top, bool animate)
        {
            Change(window, FrameworkElement.WidthProperty, width, animate);
            Change(window, FrameworkElement.HeightProperty, height, animate);
            if (!Double.IsNaN(top)) Change(window, Window.TopProperty, top, animate);
        }

        private static void Change(Window window, DependencyProperty property, double target, bool animate)
        {
            double from = (double)window.GetValue(property);
            window.BeginAnimation(property, null);
            window.SetValue(property, target);
            if (!animate || !Enabled || Double.IsNaN(from) || Math.Abs(from - target) < 0.5) return;
            window.BeginAnimation(property, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            }, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
