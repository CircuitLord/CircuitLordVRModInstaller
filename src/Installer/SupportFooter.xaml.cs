using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BigWalkVRInstaller
{
    public partial class SupportFooter : UserControl
    {
        static readonly string[] ConfettiColors = { "Support", "AmberHover", "Green", "Accent", "Amber", "SupportHover" };
        static readonly Duration BurstDuration = TimeSpan.FromSeconds(0.8);
        readonly Random _random = new Random();

        public SupportFooter() => InitializeComponent();

        void Support_Click(object sender, RoutedEventArgs e)
        {
            var origin = Mouse.GetPosition(Confetti);
            for (var i = 0; i < 22; i++) SpawnConfetti(origin, (Brush)FindResource(ConfettiColors[i % ConfettiColors.Length]));
        }

        void SpawnConfetti(Point origin, Brush color)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = 40 + _random.NextDouble() * 50;
            var move = new TranslateTransform();
            var spin = new RotateTransform();
            var piece = new Rectangle
            {
                Width = 5, Height = 8, RadiusX = 1, RadiusY = 1, Fill = color,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { spin, move } },
            };
            Canvas.SetLeft(piece, origin.X - 2.5);
            Canvas.SetTop(piece, origin.Y - 4);
            Confetti.Children.Add(piece);

            var fade = Burst(1, 0);
            fade.Completed += (sender, e) => Confetti.Children.Remove(piece);
            move.BeginAnimation(TranslateTransform.XProperty, Burst(0, Math.Cos(angle) * distance));
            move.BeginAnimation(TranslateTransform.YProperty, Burst(0, Math.Sin(angle) * distance - 30));
            spin.BeginAnimation(RotateTransform.AngleProperty, Burst(0, _random.NextDouble() * 720 - 360));
            piece.BeginAnimation(OpacityProperty, fade);
        }

        static DoubleAnimation Burst(double from, double to) =>
            new DoubleAnimation(from, to, BurstDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
    }
}
