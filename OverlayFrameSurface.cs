using System.Windows;
using System.Windows.Media;

namespace RemotePlayLauncher;

/// <summary>
/// Full-client hardware-rendered surface used to keep Steam Overlay supplied
/// with complete frames. WPF normally presents only dirty rectangles, which
/// Steam can incorrectly treat as the full output size.
/// </summary>
public sealed class OverlayFrameSurface : FrameworkElement
{
    private static readonly Brush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(18, 17, 24));

    static OverlayFrameSurface()
    {
        BackgroundBrush.Freeze();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
    }
}
