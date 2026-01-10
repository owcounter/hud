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
        public ObservableCollection<HeroSuggestionViewModel> SwapSuggestions { get; } = new();
        private HeroSuggestionViewModel? _currentHero;
        private MapName? _currentMap;
        private Dictionary<HeroName, HeroAnalysis>? _blueTeamAnalysis;

        public SwapSuggestionsPanel()
        {
            InitializeComponent();
            SuggestionsControl.ItemsSource = SwapSuggestions;
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
                _currentHero = CreateHeroViewModel(playerHero, currentHeroAnalysis, true);
                CurrentHeroCard.DataContext = _currentHero;

                // Update swap suggestions - sorted by score (highest first), filtered by min score, limited to 21
                SwapSuggestions.Clear();
                if (currentHeroAnalysis.SwapSuggestions != null)
                {
                    var minScore = AppSettings.Instance.MinScoreF2;
                    var sortedSuggestions = currentHeroAnalysis.SwapSuggestions
                        .Where(s => s.HeroScore >= minScore)
                        .OrderByDescending(s => s.HeroScore)
                        .Take(21);

                    foreach (var suggestion in sortedSuggestions)
                    {
                        SwapSuggestions.Add(CreateHeroViewModel(suggestion.Hero.Name, suggestion, false));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating SwapSuggestionsPanel: {ex.Message}");
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
                        Foreground = GetCompositionBrush(comp),
                        Background = GetCompositionBackgroundBrush(comp)
                    });
                }
            }

            var incompatibleCompositions = new List<CompositionInfo>();
            if (analysis.Hero?.IncompatibleCompositions != null)
            {
                foreach (var comp in analysis.Hero.IncompatibleCompositions)
                {
                    incompatibleCompositions.Add(new CompositionInfo
                    {
                        Name = comp.ToString(),
                        Foreground = GetCompositionBrush(comp),
                        Background = GetCompositionBackgroundBrush(comp)
                    });
                }
            }

            var mapStrengthInfo = GetMapStrengthInfo(analysis.Hero, _currentMap);
            var synergies = ConvertToSynergyInfoList(analysis.Synergies);
            var counters = ConvertToCounterInfoList(analysis.HardCounters, analysis.SoftCounters, true);
            var counteredBy = ConvertToCounterInfoList(analysis.HardCounteredBy, analysis.SoftCounteredBy, false);

            return new HeroSuggestionViewModel
            {
                HeroName = FormatHeroName(heroName),
                HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName),
                IsCurrent = isCurrent,
                ScoreValue = analysis.HeroScore,
                ScoreBrush = analysis.HeroScore >= 0 ? BrushCache.Green : BrushCache.Red,
                ScoreBackgroundBrush = analysis.HeroScore >= 0 ? BrushCache.GreenBg : BrushCache.RedBg,
                Compositions = compositions,
                IncompatibleCompositions = incompatibleCompositions,
                Counters = counters,
                CounteredBy = counteredBy,
                Synergies = synergies,
                HasCounters = counters.Count > 0,
                HasCounteredBy = counteredBy.Count > 0,
                HasSynergies = synergies.Count > 0,
                MapStrengthVisibility = mapStrengthInfo.HasValue ? Visibility.Visible : Visibility.Collapsed,
                MapStrengthBrush = mapStrengthInfo.HasValue ?
                    (mapStrengthInfo.Value.IsStrong ? BrushCache.Green : BrushCache.Red) : BrushCache.White,
                MapStrengthText = mapStrengthInfo.HasValue ?
                    (mapStrengthInfo.Value.IsStrong ? "Strong Map" : "Weak Map") : string.Empty
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
                        Icon = IconUtils.GetHeroIconAsBitmapSource(synergy.HeroName),
                        Name = FormatHeroName(synergy.HeroName),
                        Comment = string.IsNullOrEmpty(synergy.Comment) ? "Good synergy" : synergy.Comment
                    });
                }
            }

            return result;
        }

        private List<CounterInfo> ConvertToCounterInfoList(
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
                        Icon = IconUtils.GetHeroIconAsBitmapSource(counter.HeroName),
                        Name = FormatHeroName(counter.HeroName),
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
                        Icon = IconUtils.GetHeroIconAsBitmapSource(counter.HeroName),
                        Name = FormatHeroName(counter.HeroName),
                        IsHard = false,
                        Comment = string.IsNullOrEmpty(counter.Comment)
                            ? (isCountering ? "Soft counter" : "Soft countered by")
                            : counter.Comment
                    });
                }
            }

            return result;
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

        private SolidColorBrush GetCompositionBrush(CompositionType comp)
        {
            return comp switch
            {
                CompositionType.Brawl => BrushCache.Teal,   // Orange
                CompositionType.Dive => BrushCache.Pink,    // Sky blue
                CompositionType.Poke => BrushCache.Purple,  // Purple
                _ => BrushCache.White
            };
        }

        private SolidColorBrush GetCompositionBackgroundBrush(CompositionType comp)
        {
            return comp switch
            {
                CompositionType.Brawl => BrushCache.TealBg,   // Orange bg
                CompositionType.Dive => BrushCache.PinkBg,    // Sky blue bg
                CompositionType.Poke => BrushCache.PurpleBg,  // Purple bg
                _ => BrushCache.WhiteBg
            };
        }

        private string FormatHeroName(HeroName heroName)
        {
            return heroName.ToString().Replace("_", " ");
        }
    }

    internal static class BrushCache
    {
        public static readonly SolidColorBrush Green = CreateFrozen(0x10, 0xB9, 0x81);
        public static readonly SolidColorBrush Red = CreateFrozen(0xEF, 0x44, 0x44);
        public static readonly SolidColorBrush White = CreateFrozen(0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Blue = CreateFrozen(0x3B, 0x82, 0xF6);
        public static readonly SolidColorBrush Teal = CreateFrozen(0xfb, 0x92, 0x3c);  // Orange for Brawl
        public static readonly SolidColorBrush Pink = CreateFrozen(0x38, 0xbd, 0xf8);  // Sky blue for Dive
        public static readonly SolidColorBrush Purple = CreateFrozen(0xc0, 0x84, 0xfc); // Purple for Poke

        public static readonly SolidColorBrush GreenBg = CreateFrozen(0x1A, 0x10, 0xB9, 0x81);
        public static readonly SolidColorBrush RedBg = CreateFrozen(0x1A, 0xEF, 0x44, 0x44);
        public static readonly SolidColorBrush TealBg = CreateFrozen(0x1A, 0xfb, 0x92, 0x3c);  // Orange bg for Brawl
        public static readonly SolidColorBrush PinkBg = CreateFrozen(0x1A, 0x38, 0xbd, 0xf8);  // Sky blue bg for Dive
        public static readonly SolidColorBrush BlueBg = CreateFrozen(0x1A, 0x3B, 0x82, 0xF6);
        public static readonly SolidColorBrush PurpleBg = CreateFrozen(0x1A, 0xc0, 0x84, 0xfc); // Purple bg for Poke
        public static readonly SolidColorBrush WhiteBg = CreateFrozen(0x1A, 0xFF, 0xFF, 0xFF);

        private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush CreateFrozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public class HeroSuggestionViewModel
    {
        public required string HeroName { get; set; }
        public required BitmapSource HeroIcon { get; set; }
        public bool IsCurrent { get; set; }
        public int ScoreValue { get; set; }
        public required SolidColorBrush ScoreBrush { get; set; }
        public required SolidColorBrush ScoreBackgroundBrush { get; set; }
        public required List<CompositionInfo> Compositions { get; set; }
        public required List<CompositionInfo> IncompatibleCompositions { get; set; }
        public required List<CounterInfo> Counters { get; set; }
        public required List<CounterInfo> CounteredBy { get; set; }
        public required List<SynergyInfo> Synergies { get; set; }
        public bool HasCounters { get; set; }
        public bool HasCounteredBy { get; set; }
        public bool HasSynergies { get; set; }
        public bool HasIncompatibleCompositions => IncompatibleCompositions?.Count > 0;
        public required Visibility MapStrengthVisibility { get; set; }
        public required SolidColorBrush MapStrengthBrush { get; set; }
        public required string MapStrengthText { get; set; }
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
        public required string Comment { get; set; }
    }
}
