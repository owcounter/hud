using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Owmeta.Display
{
    public partial class SwapSuggestionsPanel : UserControl
    {
        public ObservableCollection<HeroSuggestionViewModel> Suggestions { get; } = new();
        private MapName? _currentMap;

        public SwapSuggestionsPanel()
        {
            InitializeComponent();
            SuggestionsControl.ItemsSource = Suggestions;
        }

        public void UpdateSuggestions(HeroName playerHero, Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis, MapName? currentMap = null)
        {
            _currentMap = currentMap;
            if (!blueTeamAnalysis.TryGetValue(playerHero, out var currentHeroAnalysis))
                return;

            Suggestions.Clear();
            Suggestions.Add(CreateHeroViewModel(playerHero, currentHeroAnalysis, true));

            if (currentHeroAnalysis.SwapSuggestions != null)
            {
                foreach (var suggestion in currentHeroAnalysis.SwapSuggestions)
                {
                    Suggestions.Add(CreateHeroViewModel(suggestion.Hero.Name, suggestion, false));
                }
            }
        }

        private HeroSuggestionViewModel CreateHeroViewModel(HeroName heroName, HeroAnalysis analysis, bool isCurrent)
        {
            var compositions = new List<CompositionInfo>();

            if (analysis.Hero?.BestCompositions != null)
            {
                foreach (var comp in analysis.Hero.BestCompositions)
                {
                    compositions.Add(new CompositionInfo
                    {
                        Name = comp.ToString(),
                        Color = GetCompositionColor(comp)
                    });
                }
            }

            var mapStrengthInfo = GetMapStrengthInfo(analysis.Hero, _currentMap);

            return new HeroSuggestionViewModel
            {
                HeroName = FormatHeroName(heroName),
                HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName),
                IsCurrent = isCurrent,
                Compositions = compositions,
                CountersHard = ConvertHeroListToBitmapSource(analysis.HardCounters),
                CountersSoft = ConvertHeroListToBitmapSource(analysis.SoftCounters),
                HardCounteredBy = ConvertHeroListToBitmapSource(analysis.HardCounteredBy),
                SoftCounteredBy = ConvertHeroListToBitmapSource(analysis.SoftCounteredBy),
                MapStrengthVisibility = mapStrengthInfo.HasValue ? Visibility.Visible : Visibility.Collapsed,
                MapStrengthColor = mapStrengthInfo.HasValue ?
                    (mapStrengthInfo.Value.IsStrong ? "#10B981" : "#EF4444") : "#FFFFFF",
                MapStrengthText = mapStrengthInfo.HasValue ?
                    (mapStrengthInfo.Value.IsStrong ? "Strong Map" : "Weak Map") : string.Empty
            };
        }

        private (bool IsStrong, bool IsWeak)? GetMapStrengthInfo(Hero? hero, MapName? currentMap)
        {
            if (hero == null || currentMap == null)
                return null;

            bool isStrong = hero.StrongMaps?.Contains(currentMap.Value) ?? false;
            bool isWeak = hero.WeakMaps?.Contains(currentMap.Value) ?? false;

            if (!isStrong && !isWeak)
                return null;

            return (isStrong, isWeak);
        }

        private List<BitmapSource> ConvertHeroListToBitmapSource(IEnumerable<HeroCounter> counters)
        {
            return counters?.Select(c => IconUtils.GetHeroIconAsBitmapSource(c.HeroName))
                          .ToList()
                          ?? new List<BitmapSource>();
        }

        private string GetCompositionColor(CompositionType comp)
        {
            return comp switch
            {
                CompositionType.Brawl => "#14B8A6",
                CompositionType.Dive => "#EC4899",
                CompositionType.Poke => "#0EA5E9",
                _ => "#FFFFFF"
            };
        }

        private string FormatHeroName(HeroName heroName)
        {
            return heroName.ToString().Replace("_", " ");
        }
    }

    public class HeroSuggestionViewModel
    {
        public required string HeroName { get; set; }
        public required BitmapSource HeroIcon { get; set; }
        public bool IsCurrent { get; set; }
        public required List<CompositionInfo> Compositions { get; set; }
        public required List<BitmapSource> CountersHard { get; set; }
        public required List<BitmapSource> CountersSoft { get; set; }
        public required List<BitmapSource> HardCounteredBy { get; set; }
        public required List<BitmapSource> SoftCounteredBy { get; set; }
        public required Visibility MapStrengthVisibility { get; set; }
        public required string MapStrengthColor { get; set; }
        public required string MapStrengthText { get; set; }
    }

    public class CompositionInfo
    {
        public required string Name { get; set; }
        public required string Color { get; set; }
    }
}