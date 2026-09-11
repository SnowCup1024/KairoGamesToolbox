using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace KairosoftGameToolbox.Services;

internal static class PageMotion
{
    public static void Constrain(Microsoft.UI.Xaml.Controls.ScrollViewer viewer, FrameworkElement body)
    {
        viewer.SizeChanged += (_, e) => body.Width = Math.Max(0, Math.Min(1050, e.NewSize.Width - 56));
    }

    public static void Enter(FrameworkElement element)
    {
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var animation = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(180)) };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        var offset = new Microsoft.UI.Xaml.Media.TranslateTransform();
        element.RenderTransform = offset;
        var slide = new DoubleAnimation
        {
            From = 8, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, offset);
        Storyboard.SetTargetProperty(slide, "Y");
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }
}
