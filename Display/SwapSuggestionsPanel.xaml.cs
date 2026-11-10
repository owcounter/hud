using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Owmeta.Display
{
    public partial class SwapSuggestionsPanel : UserControl
    {
        private const int IconSize = 128;

        public ObservableCollection<HeroSuggestionViewModel> SwapSuggestions { get; } = new();
        private HeroSuggestionViewModel? _currentHero;
        private MapName? _currentMap;
        private Dictionary<HeroName, HeroAnalysis>? _blueTeamAnalysis;

        public SwapSuggestionsPanel()
        {
            InitializeComponent();
            SuggestionsControl.ItemsSource = SwapSuggestions;
        }

        public void Clear() => Clear(null);

        /// <summary>
        /// Clears the panel. When <paramref name="notice"/> is set it is shown in place of the hero card.
        /// </summary>
        public void Clear(string? notice)
        {
            _currentHero = null;
            _currentMap = null;
            _blueTeamAnalysis = null;
            CurrentHeroCard.DataContext = null;
            CurrentHeroCard.Visibility = Visibility.Collapsed;
            SwapSection.Visibility = Visibility.Collapsed;
            SwapSuggestions.Clear();

            NoticeText.Text = notice ?? string.Empty;
            NoticeCard.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void UpdateSuggestions(HeroName playerHero, Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis, MapName? currentMap = null)
        {
            try
            {
                _currentMap = currentMap;
                _blueTeamAnalysis = blueTeamAnalysis;

                if (!blueTeamAnalysis.TryGetValue(playerHero, out var currentHeroAnalysis))
                    return;

                // Update current hero
                _currentHero = CreateHeroViewModel(playerHero, currentHeroAnalysis, true, currentHeroAnalysis.HeroScore);
                CurrentHeroCard.DataContext = _currentHero;
                CurrentHeroCard.Visibility = Visibility.Visible;
                NoticeCard.Visibility = Visibility.Collapsed;
                SwapSection.Visibility = Visibility.Visible;

                // Update swap suggestions - sorted by score (highest first), filtered by min score, limited to 21
                SwapSuggestions.Clear();
                var minScore = AppSettings.Instance.MinScoreF2;
                if (currentHeroAnalysis.SwapSuggestions != null)
                {
                    var selection = SwapSuggestionPolicy.SelectSuggestions(
                        currentHeroAnalysis.SwapSuggestions,
                        minScore);

                    Logger.Log(
                        $"F2 swap suggestions for {playerHero}: total={selection.TotalCount}, shown={selection.ShownCount}, minScore={minScore}");

                    foreach (var suggestion in selection.Suggestions)
                    {
                        var vm = CreateHeroViewModel(suggestion.Hero.Name, suggestion, false, currentHeroAnalysis.HeroScore);
                        vm.IsBest = SwapSuggestions.Count == 0 && suggestion.HeroScore > currentHeroAnalysis.HeroScore;
                        SwapSuggestions.Add(vm);
                    }

                    SwapSummaryText.Text = selection.TotalCount > selection.ShownCount
                        ? $"top {selection.ShownCount} of {selection.TotalCount} · min score {HudFormat.Score(minScore)}"
                        : $"ranked by score for this match";
                }
                else
                {
                    Logger.Log($"F2 swap suggestions for {playerHero}: no suggestions in response");
                    SwapSummaryText.Text = string.Empty;
                }

                bool hasSwaps = SwapSuggestions.Count > 0;
                NoSwapsText.Visibility = hasSwaps ? Visibility.Collapsed : Visibility.Visible;
                NoSwapsText.Text = $"No swap scores {HudFormat.Score(minScore)} or better. {_currentHero.HeroName} is a solid pick here.";
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating SwapSuggestionsPanel: {ex.Message}");
            }
        }

        private HeroSuggestionViewModel CreateHeroViewModel(HeroName heroName, HeroAnalysis analysis, bool isCurrent, int currentScore)
        {
            var compositions = HudFormat.CreateCompositionInfos(analysis.Hero?.BestCompositions);
            var incompatibleCompositions = HudFormat.CreateCompositionInfos(analysis.Hero?.IncompatibleCompositions);

            var mapStrengthInfo = GetMapStrengthInfo(analysis.Hero, _currentMap);
            var synergies = ConvertToSynergyInfoList(analysis.Synergies);
            var counters = ConvertToCounterInfoList(analysis.HardCounters, analysis.SoftCounters, true);
            var counteredBy = ConvertToCounterInfoList(analysis.HardCounteredBy, analysis.SoftCounteredBy, false);

            int delta = analysis.HeroScore - currentScore;
            bool isStrong = mapStrengthInfo?.IsStrong ?? false;

            return new HeroSuggestionViewModel
            {
                HeroName = HudFormat.HeroName(heroName),
                HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName, IconSize),
                IsCurrent = isCurrent,
                ScoreValue = analysis.HeroScore,
                ScoreText = HudFormat.Score(analysis.HeroScore),
                ScoreBrush = HudPalette.ScoreBrush(analysis.HeroScore),
                ScoreBackgroundBrush = HudPalette.ScoreSoftBrush(analysis.HeroScore),
                DeltaText = delta == 0 ? "same as current" : $"{HudFormat.Score(delta)} vs current",
                DeltaBrush = delta > 0 ? HudPalette.Good : delta < 0 ? HudPalette.Bad : HudPalette.TextMuted,
                Compositions = compositions,
                IncompatibleCompositions = incompatibleCompositions,
                Counters = counters,
                CounteredBy = counteredBy,
                Synergies = synergies,
                HasCounters = counters.Count > 0,
                HasCounteredBy = counteredBy.Count > 0,
                HasSynergies = synergies.Count > 0,
                MapStrengthVisibility = mapStrengthInfo.HasValue ? Visibility.Visible : Visibility.Collapsed,
                MapStrengthBrush = mapStrengthInfo.HasValue ? (isStrong ? HudPalette.Good : HudPalette.Bad) : HudPalette.TextPrimary,
                MapStrengthBackground = mapStrengthInfo.HasValue ? (isStrong ? HudPalette.GoodSoft : HudPalette.BadSoft) : HudPalette.NeutralSoft,
                MapStrengthText = mapStrengthInfo.HasValue
                    ? (isStrong ? "Strong" : "Weak") + (_currentMap.HasValue ? $" on {HudFormat.MapName(_currentMap.Value)}" : " map")
                    : string.Empty,
                MapStrengthShortText = mapStrengthInfo.HasValue ? (isStrong ? "Strong map" : "Weak map") : string.Empty
            };
        }

        private List<SynergyInfo> ConvertToSynergyInfoList(IEnumerable<HeroSynergy>? synergies)
        {
            var result = new List<SynergyInfo>();
            if (synergies == null) return result;

            foreach (var synergy in synergies.Take(3))
            {
                if (_blueTeamAnalysis != null && _blueTeamAnalysis.ContainsKey(synergy.HeroName))
                {
                    result.Add(new SynergyInfo
                    {
                        Icon = IconUtils.GetHeroIconAsBitmapSource(synergy.HeroName, IconSize),
                        Name = HudFormat.HeroName(synergy.HeroName),
                        Comment = string.IsNullOrEmpty(synergy.Comment) ? "Good synergy" : synergy.Comment
                    });
                }
            }

            return result;
        }

        private static List<CounterInfo> ConvertToCounterInfoList(
            IEnumerable<HeroCounter>? hardCounters,
            IEnumerable<HeroCounter>? softCounters,
            bool isCountering)
        {
            var result = new List<CounterInfo>();

            if (hardCounters != null)
            {
                foreach (var counter in hardCounters.Take(3))
                {
                    result.Add(new CounterInfo
                    {
                        Icon = IconUtils.GetHeroIconAsBitmapSource(counter.HeroName, IconSize),
                        Name = HudFormat.HeroName(counter.HeroName),
                        IsHard = true,
                        Comment = string.IsNullOrEmpty(counter.Comment)
                            ? (isCountering ? "Hard counter" : "Hard countered by")
                            : counter.Comment
                    });
                }
            }

            if (softCounters != null && result.Count < 4)
            {
                foreach (var counter in softCounters.Take(4 - result.Count))
                {
                    result.Add(new CounterInfo
                    {
                        Icon = IconUtils.GetHeroIconAsBitmapSource(counter.HeroName, IconSize),
                        Name = HudFormat.HeroName(counter.HeroName),
                        IsHard = false,
                        Comment = string.IsNullOrEmpty(counter.Comment)
                            ? (isCountering ? "Soft counter" : "Soft countered by")
                            : counter.Comment
                    });
                }
            }

            return result;
        }

        private static (bool IsStrong, bool IsWeak)? GetMapStrengthInfo(Hero? hero, MapName? currentMap)
        {
            if (hero == null || currentMap == null)
                return null;

            bool isStrong = hero.StrongMaps?.Contains(currentMap.Value) ?? false;
            bool isWeak = hero.WeakMaps?.Contains(currentMap.Value) ?? false;

            if (!isStrong && !isWeak)
                return null;

            return (isStrong, isWeak);
        }
    }

    public class HeroSuggestionViewModel
    {
        public required string HeroName { get; set; }
        public required BitmapSource HeroIcon { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsBest { get; set; }
        public Visibility BestVisibility => IsBest ? Visibility.Visible : Visibility.Collapsed;
        public int ScoreValue { get; set; }
        public required string ScoreText { get; set; }
        public required SolidColorBrush ScoreBrush { get; set; }
        public required SolidColorBrush ScoreBackgroundBrush { get; set; }
        public required string DeltaText { get; set; }
        public required SolidColorBrush DeltaBrush { get; set; }
        public required List<CompositionInfo> Compositions { get; set; }
        public required List<CompositionInfo> IncompatibleCompositions { get; set; }
        public required List<CounterInfo> Counters { get; set; }
        public required List<CounterInfo> CounteredBy { get; set; }
        public required List<SynergyInfo> Synergies { get; set; }
        public bool HasCounters { get; set; }
        public bool HasCounteredBy { get; set; }
        public bool HasSynergies { get; set; }
        public bool HasIncompatibleCompositions => IncompatibleCompositions?.Count > 0;
        public Visibility NoCountersVisibility => HasCounters ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoCounteredByVisibility => HasCounteredBy ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NoSynergiesVisibility => HasSynergies ? Visibility.Collapsed : Visibility.Visible;
        public required Visibility MapStrengthVisibility { get; set; }
        public required SolidColorBrush MapStrengthBrush { get; set; }
        public required SolidColorBrush MapStrengthBackground { get; set; }
        public required string MapStrengthText { get; set; }
        public required string MapStrengthShortText { get; set; }
    }

    public class CompositionInfo
    {
        public required string Name { get; set; }
        public required SolidColorBrush Foreground { get; set; }
        public required SolidColorBrush Background { get; set; }
    }

    public class CounterInfo
    {
        public required BitmapSource Icon { get; set; }
        public required string Name { get; set; }
        public bool IsHard { get; set; }
        public required string Comment { get; set; }
    }

    public class SynergyInfo
    {
        public required BitmapSource Icon { get; set; }
        public required string Name { get; set; }
        public bool IsHard => false;
        public required string Comment { get; set; }
    }
}
