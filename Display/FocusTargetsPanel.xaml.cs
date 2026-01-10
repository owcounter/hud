using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Owmeta.Display
{
    public partial class FocusTargetsPanel : UserControl
    {
        public ObservableCollection<VulnerableHeroViewModel> VulnerableTeammates { get; } = new();
        public ObservableCollection<DangerousHeroViewModel> DangerousEnemies { get; } = new();

        public FocusTargetsPanel()
        {
            InitializeComponent();
            VulnerableList.ItemsSource = VulnerableTeammates;
            DangerousList.ItemsSource = DangerousEnemies;
        }

        public void Update(
            Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis,
            Dictionary<HeroName, HeroAnalysis> redTeamAnalysis)
        {
            try
            {
                VulnerableTeammates.Clear();
                DangerousEnemies.Clear();

                // Find vulnerable teammates (blue team heroes that are countered by red team)
                var vulnerableScores = new List<(HeroName hero, HeroAnalysis analysis, int score, bool isHard)>();

                foreach (var (heroName, analysis) in blueTeamAnalysis)
                {
                    var hardCounteredBy = analysis.HardCounteredBy?
                        .Where(c => redTeamAnalysis.ContainsKey(c.HeroName))
                        .ToList() ?? new List<HeroCounter>();

                    var softCounteredBy = analysis.SoftCounteredBy?
                        .Where(c => redTeamAnalysis.ContainsKey(c.HeroName))
                        .ToList() ?? new List<HeroCounter>();

                    int score = hardCounteredBy.Count * 2 + softCounteredBy.Count;
                    if (score > 0)
                    {
                        vulnerableScores.Add((heroName, analysis, score, hardCounteredBy.Count > 0));
                    }
                }

                // Add top vulnerable teammates
                foreach (var (heroName, analysis, score, isHard) in vulnerableScores.OrderByDescending(x => x.score).Take(3))
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

                    var synergies = new List<CounterIconInfo>();
                    if (analysis.Synergies != null)
                    {
                        foreach (var s in analysis.Synergies.Where(s => blueTeamAnalysis.ContainsKey(s.HeroName)).Take(3))
                        {
                            synergies.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(s.HeroName),
                                IsHard = false,
                                Comment = s.Comment ?? "Synergy"
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

                    VulnerableTeammates.Add(new VulnerableHeroViewModel
                    {
                        HeroName = FormatHeroName(heroName),
                        HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName),
                        IsHardCountered = isHard,
                        ScoreValue = analysis.HeroScore,
                        ScoreBrush = analysis.HeroScore >= 0 ? BrushCache.Green : BrushCache.Red,
                        ScoreBackgroundBrush = analysis.HeroScore >= 0 ? BrushCache.GreenBg : BrushCache.RedBg,
                        Compositions = compositions,
                        CounteredBy = counteredBy,
                        Synergies = synergies,
                        HasCounteredBy = counteredBy.Count > 0,
                        HasSynergies = synergies.Count > 0
                    });
                }

                // Find dangerous enemies (red team heroes that counter blue team)
                var dangerousScores = new List<(HeroName hero, HeroAnalysis analysis, int score, bool hasHard, List<CounterIconInfo> counters, List<CounterIconInfo> counteredBy)>();

                foreach (var (enemyName, enemyAnalysis) in redTeamAnalysis)
                {
                    var counters = new List<CounterIconInfo>();
                    bool hasHard = false;

                    if (enemyAnalysis.HardCounters != null)
                    {
                        foreach (var c in enemyAnalysis.HardCounters.Where(c => blueTeamAnalysis.ContainsKey(c.HeroName)).Take(3))
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

                    if (enemyAnalysis.SoftCounters != null)
                    {
                        foreach (var c in enemyAnalysis.SoftCounters.Where(c => blueTeamAnalysis.ContainsKey(c.HeroName)).Take(3 - counters.Count))
                        {
                            counters.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = false,
                                Comment = c.Comment ?? "Soft counter"
                            });
                        }
                    }

                    var counteredBy = new List<CounterIconInfo>();
                    if (enemyAnalysis.HardCounteredBy != null)
                    {
                        foreach (var c in enemyAnalysis.HardCounteredBy.Where(c => blueTeamAnalysis.ContainsKey(c.HeroName)).Take(3))
                        {
                            counteredBy.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = true,
                                Comment = c.Comment ?? "Hard counter"
                            });
                        }
                    }
                    if (enemyAnalysis.SoftCounteredBy != null)
                    {
                        foreach (var c in enemyAnalysis.SoftCounteredBy.Where(c => blueTeamAnalysis.ContainsKey(c.HeroName)).Take(3 - counteredBy.Count))
                        {
                            counteredBy.Add(new CounterIconInfo
                            {
                                Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName),
                                IsHard = false,
                                Comment = c.Comment ?? "Soft counter"
                            });
                        }
                    }

                    int score = counters.Count(c => c.IsHard) * 2 + counters.Count(c => !c.IsHard);
                    if (score > 0)
                    {
                        dangerousScores.Add((enemyName, enemyAnalysis, score, hasHard, counters, counteredBy));
                    }
                }

                // Add top dangerous enemies
                foreach (var (enemyName, analysis, score, hasHard, counters, counteredBy) in dangerousScores.OrderByDescending(x => x.score).Take(3))
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

                // Hide sections if empty
                VulnerableSection.Visibility = VulnerableTeammates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                DangerousSection.Visibility = DangerousEnemies.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating FocusTargetsPanel: {ex.Message}");
            }
        }

        private SolidColorBrush GetCompositionBrush(CompositionType comp)
        {
            return comp switch
            {
                CompositionType.Brawl => BrushCache.Teal,
                CompositionType.Dive => BrushCache.Pink,
                CompositionType.Poke => BrushCache.Blue,
                _ => BrushCache.White
            };
        }

        private SolidColorBrush GetCompositionBackgroundBrush(CompositionType comp)
        {
            return comp switch
            {
                CompositionType.Brawl => BrushCache.TealBg,
                CompositionType.Dive => BrushCache.PinkBg,
                CompositionType.Poke => BrushCache.BlueBg,
                _ => BrushCache.WhiteBg
            };
        }

        private string FormatHeroName(HeroName heroName)
        {
            return heroName.ToString().Replace("_", " ");
        }
    }

    public class VulnerableHeroViewModel
    {
        public required string HeroName { get; set; }
        public required BitmapSource HeroIcon { get; set; }
        public bool IsHardCountered { get; set; }
        public int ScoreValue { get; set; }
        public required SolidColorBrush ScoreBrush { get; set; }
        public required SolidColorBrush ScoreBackgroundBrush { get; set; }
        public required List<CompositionInfo> Compositions { get; set; }
        public required List<CounterIconInfo> CounteredBy { get; set; }
        public required List<CounterIconInfo> Synergies { get; set; }
        public bool HasCounteredBy { get; set; }
        public bool HasSynergies { get; set; }
    }

    public class DangerousHeroViewModel
    {
        public required string HeroName { get; set; }
        public required BitmapSource HeroIcon { get; set; }
        public bool IsHighThreat { get; set; }
        public int ScoreValue { get; set; }
        public required SolidColorBrush ScoreBrush { get; set; }
        public required SolidColorBrush ScoreBackgroundBrush { get; set; }
        public required List<CompositionInfo> Compositions { get; set; }
        public required List<CounterIconInfo> Counters { get; set; }
        public required List<CounterIconInfo> CounteredBy { get; set; }
        public bool HasCounters { get; set; }
        public bool HasCounteredBy { get; set; }
    }

    public class CounterIconInfo
    {
        public required BitmapSource Icon { get; set; }
        public bool IsHard { get; set; }
        public required string Comment { get; set; }
    }
}
