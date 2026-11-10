using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Owmeta.Display
{
    public partial class DataFreshnessIndicator : UserControl
    {
        private DateTime _lastUpdateTime;
        private string? _note;
        private readonly DispatcherTimer _updateTimer;

        // Colors for different freshness levels
        private static readonly SolidColorBrush FreshBrush = HudPalette.Good;      // < 1 min
        private static readonly SolidColorBrush StaleBrush = HudPalette.Warn;      // 1-3 min
        private static readonly SolidColorBrush OldBrush = HudPalette.Bad;         // > 3 min
        private static readonly SolidColorBrush AnalyzingBrush = HudPalette.Info;
        private static readonly SolidColorBrush PartialBrush = HudPalette.Dive;

        public DataFreshnessIndicator()
        {
            InitializeComponent();

            _lastUpdateTime = DateTime.MinValue;
            UpdateDisplay();

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

        /// <summary>Fresh data; <paramref name="note"/> qualifies it (e.g. a partially read frame) until the next update.</summary>
        public void MarkUpdated(string? note = null)
        {
            _lastUpdateTime = DateTime.Now;
            _note = note;
            UpdateDisplay();
        }

        public void Reset()
        {
            _lastUpdateTime = DateTime.MinValue;
            _note = null;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_lastUpdateTime == DateTime.MinValue)
            {
                StatusText.Text = "No data yet";
                StatusDot.Fill = HudPalette.TextMuted;
                return;
            }

            var elapsed = DateTime.Now - _lastUpdateTime;

            if (elapsed.TotalSeconds < 10)
            {
                StatusText.Text = "Updated just now";
                StatusDot.Fill = FreshBrush;
            }
            else if (elapsed.TotalSeconds < 60)
            {
                StatusText.Text = $"Updated {(int)elapsed.TotalSeconds}s ago";
                StatusDot.Fill = FreshBrush;
            }
            else if (elapsed.TotalMinutes < 3)
            {
                StatusText.Text = $"Updated {(int)elapsed.TotalMinutes}m ago";
                StatusDot.Fill = StaleBrush;
            }
            else
            {
                StatusText.Text = $"Updated {(int)elapsed.TotalMinutes}m ago";
                StatusDot.Fill = OldBrush;
            }
            if (_note != null) StatusText.Text += $" · {_note}";
        }

        public void SetStatus(string status)
        {
            StatusText.Text = status;
        }

        public void SetAnalyzing()
        {
            StatusText.Text = "Analyzing scoreboard…";
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

        public void SetPartialUpdate(int persistedCount)
        {
            _lastUpdateTime = DateTime.Now;
            _note = $"{persistedCount} from memory";
            StatusText.Text = $"Updated · {_note}";
            StatusDot.Fill = PartialBrush;
        }
    }
}
