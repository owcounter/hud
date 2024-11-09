using Owcounter.Model;
using Owcounter.Services;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Owcounter.Display
{
    public partial class SwapSuggestionsPanel : UserControl
    {
        public ObservableCollection<HeroSuggestionViewModel> Suggestions { get; } = new();

        public SwapSuggestionsPanel()
        {
            InitializeComponent();
            SuggestionsControl.ItemsSource = Suggestions;
        }

        public void UpdateSuggestions(HeroName playerHero, Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis)
        {
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

            return new HeroSuggestionViewModel
            {
                HeroName = FormatHeroName(heroName),
                HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName),
                IsCurrent = isCurrent,
                Compositions = compositions,
                CountersHard = ConvertHeroListToBitmapSource(analysis.HardCounters),
                CountersSoft = ConvertHeroListToBitmapSource(analysis.SoftCounters),
                HardCounteredBy = ConvertHeroListToBitmapSource(analysis.HardCounteredBy),
                SoftCounteredBy = ConvertHeroListToBitmapSource(analysis.SoftCounteredBy)
            };
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
    }

    public class CompositionInfo
    {
        public required string Name { get; set; }
        public required string Color { get; set; }
    }
}