using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Owmeta.Display
{
    public partial class DataFreshnessIndicator : UserControl
    {
        private DateTime _lastUpdateTime;
        private readonly DispatcherTimer _updateTimer;

        // Colors for different freshness levels
        private static readonly SolidColorBrush FreshBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));    // Green - < 1 min
        private static readonly SolidColorBrush StaleBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));    // Yellow/Amber - 1-3 min
        private static readonly SolidColorBrush OldBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));      // Red - > 3 min
        private static readonly SolidColorBrush AnalyzingBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6)); // Blue

        static DataFreshnessIndicator()
        {
            // Freeze brushes for performance
            FreshBrush.Freeze();
            StaleBrush.Freeze();
            OldBrush.Freeze();
            AnalyzingBrush.Freeze();
        }

        public DataFreshnessIndicator()
        {
            InitializeComponent();

            _lastUpdateTime = DateTime.MinValue;

            // Timer to update the display every 10 seconds
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _updateTimer.Tick += (s, e) => UpdateDisplay();
            _updateTimer.Start();

            // Handle timer lifecycle with control visibility
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Restart timer when control is reloaded
            if (!_updateTimer.IsEnabled)
            {
                _updateTimer.Start();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _updateTimer.Stop();
        }

        public void MarkUpdated()
        {
            _lastUpdateTime = DateTime.Now;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_lastUpdateTime == DateTime.MinValue)
            {
                StatusText.Text = "No data yet";
                StatusDot.Fill = OldBrush;
                return;
            }

            var elapsed = DateTime.Now - _lastUpdateTime;

            if (elapsed.TotalSeconds < 10)
            {
                StatusText.Text = "Updated: Just now";
                StatusDot.Fill = FreshBrush;
            }
            else if (elapsed.TotalSeconds < 60)
            {
                StatusText.Text = $"Updated: {(int)elapsed.TotalSeconds}s ago";
                StatusDot.Fill = FreshBrush;
            }
            else if (elapsed.TotalMinutes < 3)
            {
                StatusText.Text = $"Updated: {(int)elapsed.TotalMinutes}m ago";
                StatusDot.Fill = StaleBrush;
            }
            else
            {
                StatusText.Text = $"Updated: {(int)elapsed.TotalMinutes}m ago";
                StatusDot.Fill = OldBrush;
            }
        }

        public void SetStatus(string status)
        {
            StatusText.Text = status;
        }

        public void SetAnalyzing()
        {
            StatusText.Text = "Analyzing...";
            StatusDot.Fill = AnalyzingBrush;
        }

        public void SetError(string message)
        {
            StatusText.Text = message;
            StatusDot.Fill = OldBrush;
        }

        public void SetStaleWarning()
        {
            StatusText.Text = "Using cached data";
            StatusDot.Fill = StaleBrush;
        }
    }
}
