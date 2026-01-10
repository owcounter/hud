using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Owmeta.Display
{
    public partial class AlertBar : UserControl
    {
        public ObservableCollection<CounterHeroInfo> CounteredByHeroes { get; } = new();

        // Pre-frozen brushes for performance
        private static readonly SolidColorBrush DangerBrush = new(Color.FromRgb(0xF4, 0x3F, 0x5E));
        private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));

        private Storyboard? _pulseStoryboard;
        private bool _isAnimating;

        static AlertBar()
        {
            DangerBrush.Freeze();
            SuccessBrush.Freeze();
        }

        public AlertBar()
        {
            InitializeComponent();
            CounteredByList.ItemsSource = CounteredByHeroes;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Cache storyboard reference
            _pulseStoryboard = TryFindResource("PulseAnimation") as Storyboard;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Stop animation and clean up
            StopPulseAnimation();
        }

        public void Update(HeroName playerHero, HeroAnalysis playerAnalysis, HeroAnalysis? topSwapSuggestion, Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis)
        {
            try
            {
                CounteredByHeroes.Clear();

                // Check if player is hard countered
                var hardCounters = playerAnalysis.HardCounteredBy?.ToList() ?? new List<HeroCounter>();
                bool isHardCountered = hardCounters.Count >= 2;

                if (isHardCountered)
                {
                    // Show alert section
                    AlertSection.Visibility = Visibility.Visible;
                    OpportunitySection.Visibility = Visibility.Collapsed;

                    // Set alert border color with glow
                    MainBorder.BorderBrush = DangerBrush;
                    MainBorder.BorderThickness = new Thickness(1);

                    // Add countering heroes
                    foreach (var counter in hardCounters.Take(3))
                    {
                        CounteredByHeroes.Add(new CounterHeroInfo
                        {
                            Name = FormatHeroName(counter.HeroName),
                            Icon = IconUtils.GetHeroIconAsBitmapSource(counter.HeroName)
                        });
                    }

                    // Start pulsing animation
                    StartPulseAnimation();
                }
                else
                {
                    // Stop pulsing animation first
                    StopPulseAnimation();

                    // Check for opportunities
                    AlertSection.Visibility = Visibility.Collapsed;

                    // Find if there's an opportunity (hero with many hard counters on enemy)
                    var opportunity = FindOpportunity(topSwapSuggestion);
                    if (opportunity != null)
                    {
                        OpportunitySection.Visibility = Visibility.Visible;
                        OpportunityText.Text = opportunity;
                        MainBorder.BorderBrush = SuccessBrush;
                        MainBorder.BorderThickness = new Thickness(1);
                    }
                    else
                    {
                        OpportunitySection.Visibility = Visibility.Collapsed;
                        MainBorder.BorderBrush = Brushes.Transparent;
                        MainBorder.BorderThickness = new Thickness(0);
                    }
                }

                // Update swap recommendation
                UpdateSwapRecommendation(topSwapSuggestion, blueTeamAnalysis);

                // Show empty state if nothing to display
                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating AlertBar: {ex.Message}");
            }
        }

        private void UpdateEmptyState()
        {
            bool hasContent = AlertSection.Visibility == Visibility.Visible ||
                             OpportunitySection.Visibility == Visibility.Visible ||
                             SwapRecommendationSection.Visibility == Visibility.Visible;

            EmptyStateText.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateSwapRecommendation(HeroAnalysis? topSwapSuggestion, Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis)
        {
            if (topSwapSuggestion?.Hero != null)
            {
                SwapRecommendationSection.Visibility = Visibility.Visible;
                RecommendedHeroIcon.Source = IconUtils.GetHeroIconAsBitmapSource(topSwapSuggestion.Hero.Name);
                RecommendedHeroName.Text = FormatHeroName(topSwapSuggestion.Hero.Name);

                // Build reason text from counters
                var reason = BuildSwapReason(topSwapSuggestion, blueTeamAnalysis);
                RecommendedReason.Text = reason;

                // Set score stars based on hero score
                UpdateScoreStars(topSwapSuggestion.HeroScore);
            }
            else
            {
                // Hide swap recommendation when no suggestion available
                SwapRecommendationSection.Visibility = Visibility.Collapsed;
            }
        }

        private void StartPulseAnimation()
        {
            if (_pulseStoryboard != null && !_isAnimating)
            {
                _pulseStoryboard.Begin(MainBorder, true);
                _isAnimating = true;
            }
        }

        private void StopPulseAnimation()
        {
            if (_pulseStoryboard != null && _isAnimating)
            {
                _pulseStoryboard.Stop(MainBorder);
                _isAnimating = false;
            }
        }

        private string? FindOpportunity(HeroAnalysis? swapSuggestion)
        {
            if (swapSuggestion?.Hero == null)
                return null;

            // If top swap has 3+ hard counters on enemy, it's a good opportunity
            var hardCounterCount = swapSuggestion.HardCounters?.Count ?? 0;
            if (hardCounterCount >= 3)
            {
                return $"{FormatHeroName(swapSuggestion.Hero.Name)} counters {hardCounterCount} enemies hard";
            }

            return null;
        }

        private string BuildSwapReason(HeroAnalysis suggestion, Dictionary<HeroName, HeroAnalysis> teamAnalysis)
        {
            var reasons = new List<string>();

            // Add hard counter reasons with comments
            if (suggestion.HardCounters != null)
            {
                foreach (var counter in suggestion.HardCounters.Take(2))
                {
                    if (!string.IsNullOrEmpty(counter.Comment))
                    {
                        reasons.Add(counter.Comment);
                        break; // Just use the first comment
                    }
                    else
                    {
                        reasons.Add($"Counters {FormatHeroName(counter.HeroName)}");
                    }
                }
            }

            // Add synergy reasons
            if (suggestion.Synergies != null && suggestion.Synergies.Count > 0)
            {
                var synergy = suggestion.Synergies.First();
                if (!string.IsNullOrEmpty(synergy.Comment))
                {
                    reasons.Add(synergy.Comment);
                }
                else
                {
                    reasons.Add($"Synergy with {FormatHeroName(synergy.HeroName)}");
                }
            }

            return reasons.Count > 0 ? string.Join(" - ", reasons.Take(2)) : "Good overall matchup";
        }

        private void UpdateScoreStars(int score)
        {
            // Score is typically 0-100, map to 1-5 stars
            int filledStars = Math.Clamp(score / 20, 1, 5);
            int emptyStars = 5 - filledStars;

            ScoreStars.Children.Clear();
            ScoreStars.Children.Add(new TextBlock
            {
                Text = new string('★', filledStars) + new string('☆', emptyStars),
                FontSize = 14,
                Foreground = SuccessBrush
            });
        }

        private static string FormatHeroName(HeroName heroName)
        {
            return heroName.ToString().Replace("_", " ");
        }
    }

    public class CounterHeroInfo
    {
        public required string Name { get; set; }
        public required BitmapSource Icon { get; set; }
    }
}
