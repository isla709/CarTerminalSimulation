using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TerminalSimulation.Wpf.Helpers;

/// <summary>
/// Provides the shared directional transition used by application tab content.
/// The animation is applied synchronously with selection changes to avoid a
/// settled-frame flash before the incoming page starts moving.
/// </summary>
internal static class TabTransitionAnimator
{
    public static void Animate(FrameworkElement content, double direction, Action? completed = null)
    {
        content.RenderTransformOrigin = new Point(0.5, 0.5);

        // Keep base values at the settled state. FillBehavior.Stop can then
        // release animation clocks without producing an end-of-animation snap.
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(translate);

        content.BeginAnimation(UIElement.OpacityProperty, null);
        content.RenderTransform = transforms;
        content.Opacity = 1;

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(210),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var slide = new DoubleAnimation
        {
            From = Math.Sign(direction) * 34,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(340),
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var settle = new DoubleAnimation
        {
            From = 0.985,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        if (completed is not null)
        {
            slide.Completed += (_, _) => completed();
        }

        content.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle, HandoffBehavior.SnapshotAndReplace);
    }
}
