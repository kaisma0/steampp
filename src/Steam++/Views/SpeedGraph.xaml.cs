using SteamPP.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SteamPP.Views
{
    public partial class SpeedGraph : UserControl
    {
        private int _lastHistoryCount = 0;

        public static readonly DependencyProperty SpeedHistoryProperty =
            DependencyProperty.Register(nameof(SpeedHistory), typeof(IReadOnlyList<SpeedSample>), typeof(SpeedGraph),
                new PropertyMetadata(null, OnSpeedHistoryChanged));

        public IReadOnlyList<SpeedSample>? SpeedHistory
        {
            get => (IReadOnlyList<SpeedSample>?)GetValue(SpeedHistoryProperty);
            set => SetValue(SpeedHistoryProperty, value);
        }

        public SpeedGraph()
        {
            InitializeComponent();
            SizeChanged += (s, e) => RedrawGraph();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, e) =>
            {
                var history = SpeedHistory;
                if (history != null)
                {
                    _lastHistoryCount = history.Count;
                    RedrawGraph();
                }
            };
            timer.Start();
        }

        private static void OnSpeedHistoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpeedGraph graph)
            {
                graph._lastHistoryCount = 0;
                graph.RedrawGraph();
            }
        }

        private void RedrawGraph()
        {
            GraphCanvas.Children.Clear();

            var history = SpeedHistory;
            if (history == null || history.Count < 2)
                return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0)
                return;

            long maxNetworkSpeed = 1;
            long maxDiskSpeed = 1;

            foreach (var sample in history)
            {
                if (sample.NetworkSpeed > maxNetworkSpeed) maxNetworkSpeed = sample.NetworkSpeed;
                if (sample.DiskSpeed > maxDiskSpeed) maxDiskSpeed = sample.DiskSpeed;
            }

            maxNetworkSpeed = (long)(maxNetworkSpeed * 1.1);
            maxDiskSpeed = (long)(maxDiskSpeed * 1.1);

            double xStep = width / (history.Count - 1);

            var networkPoints = new PointCollection();
            var diskPoints = new PointCollection();

            for (int i = 0; i < history.Count; i++)
            {
                double x = i * xStep;
                double networkY = height - (history[i].NetworkSpeed / (double)maxNetworkSpeed * height);
                double diskY = height - (history[i].DiskSpeed / (double)maxDiskSpeed * height);

                networkPoints.Add(new Point(x, networkY));
                diskPoints.Add(new Point(x, diskY));
            }

            // Network fill (blue)
            var networkFillPoints = new PointCollection(networkPoints);
            networkFillPoints.Add(new Point(width, height));
            networkFillPoints.Add(new Point(0, height));

            var networkFill = new Polygon
            {
                Points = networkFillPoints,
                Fill = new SolidColorBrush(Color.FromArgb(60, 71, 144, 213)),
                Stroke = null
            };
            GraphCanvas.Children.Add(networkFill);

            // Network line (blue)
            var networkLine = new Polyline
            {
                Points = networkPoints,
                Stroke = new SolidColorBrush(Color.FromRgb(71, 144, 213)),
                StrokeThickness = 2
            };
            GraphCanvas.Children.Add(networkLine);

            // Disk line (green)
            var diskLine = new Polyline
            {
                Points = diskPoints,
                Stroke = new SolidColorBrush(Color.FromRgb(90, 179, 96)),
                StrokeThickness = 2
            };
            GraphCanvas.Children.Add(diskLine);
        }
    }
}
