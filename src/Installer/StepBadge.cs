using System.Windows.Controls;
using System.Windows.Media;

namespace BigWalkVRInstaller
{
    // numbered setup step, label Tag holds the number shown until done
    public static class StepBadge
    {
        public static void Set(Border badge, TextBlock label, bool done)
        {
            badge.Background = (Brush)badge.FindResource(done ? "GreenFill" : "Hover");
            badge.BorderBrush = (Brush)badge.FindResource(done ? "GreenFillBorder" : "Stroke");
            label.Text = done ? "✓" : (string)label.Tag;
            label.Foreground = (Brush)label.FindResource(done ? "Green" : "TextDim");
        }
    }
}
