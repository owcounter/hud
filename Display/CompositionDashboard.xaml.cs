using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Owmeta.Display
{
    public partial class CompositionDashboard : UserControl
    {
        public ObservableCollection<VulnerableHeroViewModel> VulnerableTeammates { get; } = new();
        public ObservableCollection<DangerousHeroViewModel> DangerousEnemies { get; } = new();
        public ObservableCollection<TeamSwapViewModel> TeamSwapSuggestions { get; } = new();

        public CompositionDashboard()
        {
            InitializeComponent();
            VulnerableList.ItemsSource = VulnerableTeammates;
            DangerousList.ItemsSource = DangerousEnemies;
            TeamSwapList.ItemsSource = TeamSwapSuggestions;
        }

        private MapName? _currentMap;

        public void Update(
            Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis,
            Dictionary<HeroName, HeroAnalysis> redTeamAnalysis,
            MapName? currentMap = null)
        {
            try
            {
                _currentMap = currentMap;

                // Update team composition panels
                BlueTeamPanel.UpdateCompositions(blueTeamAnalysis);
                RedTeamPanel.UpdateCompositions(redTeamAnalysis);

                VulnerableTeammates.Clear();
                DangerousEnemies.Clear();
                TeamSwapSuggestions.Clear();

                // Find vulnerable teammates: blue team heroes with HeroScore <= -2
                var vulnerableHeroes = blueTeamAnalysis
                    .Where(kvp => kvp.Value.HeroScore <= -2)
                    .OrderBy(kvp => kvp.Value.HeroScore)  // Most negative first
                    .ToList();

                // Add vulnerable teammates
                foreach (var (heroName, analysis) in vulnerableHeroes)
                {
                    var counteredBy = new List<CounterIconInfo>();
                    if (analysis.HardCounteredBy != null)
                    {
                        foreach (var c in analysis.HardCounteredBy.Where(c => redTeamAnalysis.ContainsKey(c.HeroName)).Take(3))
                        {
                            counteredBy.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = true,
                                Comment = c.Comment ?? "Hard counter"
                            });
                        }
                    }
                    if (analysis.SoftCounteredBy != null)
                    {
                        foreach (var c in analysis.SoftCounteredBy.Where(c => redTeamAnalysis.ContainsKey(c.HeroName)).Take(3 - counteredBy.Count))
                        {
                            counteredBy.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = false,
                                Comment = c.Comment ?? "Soft counter"
                            });
                        }
                    }

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

                    var isHardCountered = analysis.HardCounteredBy?
                        .Any(c => redTeamAnalysis.ContainsKey(c.HeroName)) ?? false;

                    VulnerableTeammates.Add(new VulnerableHeroViewModel
                    {
                        HeroName = FormatHeroName(heroName),
                        HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName),
                        IsHardCountered = isHardCountered,
                        ScoreValue = analysis.HeroScore,
                        ScoreBrush = analysis.HeroScore >= 0 ? BrushCache.Green : BrushCache.Red,
                        ScoreBackgroundBrush = analysis.HeroScore >= 0 ? BrushCache.GreenBg : BrushCache.RedBg,
                        Compositions = compositions,
                        CounteredBy = counteredBy,
                        Synergies = new List<CounterIconInfo>(),
                        HasCounteredBy = counteredBy.Count > 0,
                        HasSynergies = false
                    });
                }

                // Find dangerous enemies: red team heroes with HeroScore >= 3
                var dangerousHeroes = redTeamAnalysis
                    .Where(kvp => kvp.Value.HeroScore >= 3)
                    .OrderByDescending(kvp => kvp.Value.HeroScore)  // Highest score first
                    .ToList();

                // Add dangerous enemies
                foreach (var (enemyName, analysis) in dangerousHeroes)
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

                    // Who this enemy counters on blue team
                    var counters = new List<CounterIconInfo>();
                    bool hasHard = false;
                    if (analysis.HardCounters != null)
                    {
                        foreach (var c in analysis.HardCounters.Where(c => blueTeamAnalysis.ContainsKey(c.HeroName)).Take(3))
                        {
                            counters.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = true,
                                Comment = c.Comment ?? "Hard counter"
                            });
                            hasHard = true;
                        }
                    }
                    if (analysis.SoftCounters != null)
                    {
                        foreach (var c in analysis.SoftCounters.Where(c => blueTeamAnalysis.ContainsKey(c.HeroName)).Take(3 - counters.Count))
                        {
                            counters.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = false,
                                Comment = c.Comment ?? "Soft counter"
                            });
                        }
                    }

                    // Swap suggestions to counter this enemy
                    var counteredBy = new List<CounterIconInfo>();
                    if (analysis.HardCounteredBy != null)
                    {
                        foreach (var c in analysis.HardCounteredBy.Take(3))
                        {
                            counteredBy.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = true,
                                Comment = c.Comment ?? "Hard counter"
                            });
                        }
                    }
                    if (analysis.SoftCounteredBy != null)
                    {
                        foreach (var c in analysis.SoftCounteredBy.Take(3 - counteredBy.Count))
                        {
                            counteredBy.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = false,
                                Comment = c.Comment ?? "Soft counter"
                            });
                        }
                    }

                    DangerousEnemies.Add(new DangerousHeroViewModel
                    {
                        HeroName = FormatHeroName(enemyName),
                        HeroIcon = IconUtils.GetHeroIconAsBitmapSource(enemyName),
                        IsHighThreat = hasHard,
                        ScoreValue = analysis.HeroScore,
                        ScoreBrush = analysis.HeroScore >= 0 ? BrushCache.Green : BrushCache.Red,
                        ScoreBackgroundBrush = analysis.HeroScore >= 0 ? BrushCache.GreenBg : BrushCache.RedBg,
                        Compositions = compositions,
                        Counters = counters,
                        CounteredBy = counteredBy,
                        HasCounters = counters.Count > 0,
                        HasCounteredBy = counteredBy.Count > 0
                    });
                }

                // Calculate team swap suggestions - heroes that would help the team the most
                CalculateTeamSwapSuggestions(blueTeamAnalysis, redTeamAnalysis);

                // Hide sections if empty
                VulnerableSection.Visibility = VulnerableTeammates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                DangerousSection.Visibility = DangerousEnemies.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                TeamSwapSection.Visibility = TeamSwapSuggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating CompositionDashboard: {ex.Message}");
            }
        }

        private void CalculateTeamSwapSuggestions(
            Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis,
            Dictionary<HeroName, HeroAnalysis> redTeamAnalysis)
        {
            // For each blue team hero, find their best swap suggestions
            // Calculate score difference and sort by highest improvement
            var allSwaps = new List<(HeroName fromHero, int fromScore, HeroName toHero, HeroAnalysis toAnalysis, int scoreDiff)>();

            foreach (var (fromHeroName, fromAnalysis) in blueTeamAnalysis)
            {
                if (fromAnalysis.SwapSuggestions == null) continue;

                foreach (var suggestion in fromAnalysis.SwapSuggestions)
                {
                    var toHeroName = suggestion.Hero.Name;

                    // Skip if already on team
                    if (blueTeamAnalysis.ContainsKey(toHeroName)) continue;

                    int scoreDiff = suggestion.HeroScore - fromAnalysis.HeroScore;

                    // Only include swaps that improve the score
                    if (scoreDiff > 0)
                    {
                        allSwaps.Add((fromHeroName, fromAnalysis.HeroScore, toHeroName, suggestion, scoreDiff));
                    }
                }
            }

            // Sort by highest score difference, filter by min score diff, and take top suggestions
            var minScoreDiff = AppSettings.Instance.MinScoreF3;
            var topSwaps = allSwaps
                .Where(x => x.scoreDiff >= minScoreDiff)
                .OrderByDescending(x => x.scoreDiff)
                .Take(21);

            foreach (var (fromHeroName, fromScore, toHeroName, toAnalysis, scoreDiff) in topSwaps)
            {
                // Get counters for the swap target (who on red team this hero counters)
                var counters = new List<CounterIconInfo>();
                if (toAnalysis.HardCounters != null)
                {
                    foreach (var c in toAnalysis.HardCounters.Where(c => redTeamAnalysis.ContainsKey(c.HeroName)).Take(3))
                    {
                        counters.Add(new CounterIconInfo
                        {
                            Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                            IsHard = true,
                            Comment = c.Comment ?? "Hard counter"
                        });
                    }
                }
                if (toAnalysis.SoftCounters != null)
                {
                    foreach (var c in toAnalysis.SoftCounters.Where(c => redTeamAnalysis.ContainsKey(c.HeroName)).Take(3 - counters.Count))
                    {
                        counters.Add(new CounterIconInfo
                        {
                            Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                            IsHard = false,
                            Comment = c.Comment ?? "Soft counter"
                        });
                    }
                }

                // Get countered by for the swap target (who on red team counters this hero)
                var counteredBy = new List<CounterIconInfo>();
                if (toAnalysis.HardCounteredBy != null)
                {
                    foreach (var c in toAnalysis.HardCounteredBy.Where(c => redTeamAnalysis.ContainsKey(c.HeroName)).Take(3))
                    {
                        counteredBy.Add(new CounterIconInfo
                        {
                            Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                            IsHard = true,
                            Comment = c.Comment ?? "Hard countered by"
                        });
                    }
                }
                if (toAnalysis.SoftCounteredBy != null)
                {
                    foreach (var c in toAnalysis.SoftCounteredBy.Where(c => redTeamAnalysis.ContainsKey(c.HeroName)).Take(3 - counteredBy.Count))
                    {
                        counteredBy.Add(new CounterIconInfo
                        {
                            Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                            IsHard = false,
                            Comment = c.Comment ?? "Soft countered by"
                        });
                    }
                }

                // Get synergies for the swap target (who on blue team synergizes with this hero)
                var synergies = new List<CounterIconInfo>();
                if (toAnalysis.Synergies != null)
                {
                    foreach (var s in toAnalysis.Synergies.Where(s => blueTeamAnalysis.ContainsKey(s.HeroName)).Take(3))
                    {
                        synergies.Add(new CounterIconInfo
                        {
                            Icon = IconUtils.GetHeroIconAsBitmapSource(s.HeroName),
                            IsHard = false,
                            Comment = s.Comment ?? "Good synergy"
                        });
                    }
                }

                // Get compositions for the "from" hero
                var fromCompositions = new List<CompositionInfo>();
                var fromIncompatibleCompositions = new List<CompositionInfo>();
                if (blueTeamAnalysis.TryGetValue(fromHeroName, out var fromAnalysis))
                {
                    if (fromAnalysis.Hero?.BestCompositions != null)
                    {
                        foreach (var comp in fromAnalysis.Hero.BestCompositions)
                        {
                            fromCompositions.Add(new CompositionInfo
                            {
                                Name = comp.ToString(),
                                Foreground = GetCompositionBrush(comp),
                                Background = GetCompositionBackgroundBrush(comp)
                            });
                        }
                    }
                    if (fromAnalysis.Hero?.IncompatibleCompositions != null)
                    {
                        foreach (var comp in fromAnalysis.Hero.IncompatibleCompositions)
                        {
                            fromIncompatibleCompositions.Add(new CompositionInfo
                            {
                                Name = comp.ToString(),
                                Foreground = GetCompositionBrush(comp),
                                Background = GetCompositionBackgroundBrush(comp)
                            });
                        }
                    }
                }

                // Get compositions for the "to" hero
                var toCompositions = new List<CompositionInfo>();
                var toIncompatibleCompositions = new List<CompositionInfo>();
                if (toAnalysis.Hero?.BestCompositions != null)
                {
                    foreach (var comp in toAnalysis.Hero.BestCompositions)
                    {
                        toCompositions.Add(new CompositionInfo
                        {
                            Name = comp.ToString(),
                            Foreground = GetCompositionBrush(comp),
                            Background = GetCompositionBackgroundBrush(comp)
                        });
                    }
                }
                if (toAnalysis.Hero?.IncompatibleCompositions != null)
                {
                    foreach (var comp in toAnalysis.Hero.IncompatibleCompositions)
                    {
                        toIncompatibleCompositions.Add(new CompositionInfo
                        {
                            Name = comp.ToString(),
                            Foreground = GetCompositionBrush(comp),
                            Background = GetCompositionBackgroundBrush(comp)
                        });
                    }
                }

                // Get map strength for both heroes
                bool fromIsStrongMap = false, fromIsWeakMap = false;
                bool toIsStrongMap = false, toIsWeakMap = false;
                if (_currentMap.HasValue)
                {
                    if (fromAnalysis?.Hero != null)
                    {
                        fromIsStrongMap = fromAnalysis.Hero.StrongMaps?.Contains(_currentMap.Value) ?? false;
                        fromIsWeakMap = fromAnalysis.Hero.WeakMaps?.Contains(_currentMap.Value) ?? false;
                    }
                    if (toAnalysis.Hero != null)
                    {
                        toIsStrongMap = toAnalysis.Hero.StrongMaps?.Contains(_currentMap.Value) ?? false;
                        toIsWeakMap = toAnalysis.Hero.WeakMaps?.Contains(_currentMap.Value) ?? false;
                    }
                }

                TeamSwapSuggestions.Add(new TeamSwapViewModel
                {
                    FromHeroName = FormatHeroName(fromHeroName),
                    FromHeroIcon = IconUtils.GetHeroIconAsBitmapSource(fromHeroName),
                    FromScore = fromScore,
                    FromScoreBrush = fromScore >= 0 ? BrushCache.Green : BrushCache.Red,
                    FromScoreBackgroundBrush = fromScore >= 0 ? BrushCache.GreenBg : BrushCache.RedBg,
                    FromCompositions = fromCompositions,
                    FromIncompatibleCompositions = fromIncompatibleCompositions,
                    FromIsStrongMap = fromIsStrongMap,
                    FromIsWeakMap = fromIsWeakMap,
                    ToHeroName = FormatHeroName(toHeroName),
                    ToHeroIcon = IconUtils.GetHeroIconAsBitmapSource(toHeroName),
                    ToScore = toAnalysis.HeroScore,
                    ToScoreBrush = toAnalysis.HeroScore >= 0 ? BrushCache.Green : BrushCache.Red,
                    ToScoreBackgroundBrush = toAnalysis.HeroScore >= 0 ? BrushCache.GreenBg : BrushCache.RedBg,
                    ToCompositions = toCompositions,
                    ToIncompatibleCompositions = toIncompatibleCompositions,
                    ToIsStrongMap = toIsStrongMap,
                    ToIsWeakMap = toIsWeakMap,
                    ScoreDiff = scoreDiff,
                    ScoreDiffBrush = BrushCache.Green,
                    ScoreDiffBackgroundBrush = BrushCache.GreenBg,
                    Counters = counters,
                    CounteredBy = counteredBy,
                    Synergies = synergies,
                    HasCounters = counters.Count > 0,
                    HasCounteredBy = counteredBy.Count > 0,
                    HasSynergies = synergies.Count > 0
                });
            }
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

    public class TeamSwapViewModel
    {
        // From hero (current)
        public required string FromHeroName { get; set; }
        public required BitmapSource FromHeroIcon { get; set; }
        public int FromScore { get; set; }
        public required SolidColorBrush FromScoreBrush { get; set; }
        public required SolidColorBrush FromScoreBackgroundBrush { get; set; }
        public required List<CompositionInfo> FromCompositions { get; set; }

        public required List<CompositionInfo> FromIncompatibleCompositions { get; set; }
        public bool FromIsStrongMap { get; set; }
        public bool FromIsWeakMap { get; set; }

        // To hero (swap suggestion)
        public required string ToHeroName { get; set; }
        public required BitmapSource ToHeroIcon { get; set; }
        public int ToScore { get; set; }
        public required SolidColorBrush ToScoreBrush { get; set; }
        public required SolidColorBrush ToScoreBackgroundBrush { get; set; }
        public required List<CompositionInfo> ToCompositions { get; set; }
        public required List<CompositionInfo> ToIncompatibleCompositions { get; set; }
        public bool ToIsStrongMap { get; set; }
        public bool ToIsWeakMap { get; set; }

        // Score difference
        public int ScoreDiff { get; set; }
        public required SolidColorBrush ScoreDiffBrush { get; set; }
        public required SolidColorBrush ScoreDiffBackgroundBrush { get; set; }

        public required List<CounterIconInfo> Counters { get; set; }
        public required List<CounterIconInfo> CounteredBy { get; set; }
        public required List<CounterIconInfo> Synergies { get; set; }
        public bool HasCounters { get; set; }
        public bool HasCounteredBy { get; set; }
        public bool HasSynergies { get; set; }
        public bool HasFromCompositions => FromCompositions?.Count > 0;
        public bool HasToCompositions => ToCompositions?.Count > 0;
    }
}
