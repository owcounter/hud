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
        private const int IconSize = 128;

        // Two rows of seven cards is what fits on screen below the team panels.
        private const int MaxTeamSwaps = 14;

        public ObservableCollection<VulnerableHeroViewModel> VulnerableTeammates { get; } = new();
        public ObservableCollection<DangerousHeroViewModel> DangerousEnemies { get; } = new();
        public ObservableCollection<TeamSwapViewModel> TeamSwapSuggestions { get; } = new();

        // Matchup indicator threshold
        private const int MatchupAdvantageThreshold = 3;

        public CompositionDashboard()
        {
            InitializeComponent();
            VulnerableList.ItemsSource = VulnerableTeammates;
            DangerousList.ItemsSource = DangerousEnemies;
            TeamSwapList.ItemsSource = TeamSwapSuggestions;
            Clear();
        }

        private MapName? _currentMap;

        public void Clear()
        {
            _currentMap = null;
            BlueTeamPanel.Clear();
            RedTeamPanel.Clear();
            VulnerableTeammates.Clear();
            DangerousEnemies.Clear();
            TeamSwapSuggestions.Clear();
            UpdateSectionStates();

            MatchupDiffText.Text = "--";
            MatchupDiffText.Foreground = HudPalette.TextMuted;
            MatchupText.Text = "NO DATA";
            MatchupText.Foreground = HudPalette.TextMuted;
            MatchupBadge.Background = HudPalette.NeutralSoft;
        }

        private void UpdateMatchupIndicator()
        {
            int blueScore = BlueTeamPanel.LastScore;
            int redScore = RedTeamPanel.LastScore;
            int diff = blueScore - redScore;

            string text;
            SolidColorBrush color;
            SolidColorBrush background;

            if (diff >= MatchupAdvantageThreshold)
            {
                text = "ADVANTAGE";
                color = HudPalette.Good;
                background = HudPalette.GoodSoft;
            }
            else if (diff <= -MatchupAdvantageThreshold)
            {
                text = "DISADVANTAGE";
                color = HudPalette.Bad;
                background = HudPalette.BadSoft;
            }
            else
            {
                text = "EVEN";
                color = HudPalette.Warn;
                background = HudPalette.WarnSoft;
            }

            MatchupDiffText.Text = HudFormat.Score(diff);
            MatchupDiffText.Foreground = color;
            MatchupText.Text = text;
            MatchupText.Foreground = color;
            MatchupBadge.Background = background;
        }

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

                // Update matchup indicator
                if (blueTeamAnalysis.Count > 0 && redTeamAnalysis.Count > 0)
                    UpdateMatchupIndicator();
                else
                {
                    MatchupDiffText.Text = "--";
                    MatchupDiffText.Foreground = HudPalette.TextMuted;
                    MatchupText.Text = "INSUFFICIENT DATA";
                    MatchupText.Foreground = HudPalette.Warn;
                    MatchupBadge.Background = HudPalette.WarnSoft;
                }

                VulnerableTeammates.Clear();
                DangerousEnemies.Clear();
                TeamSwapSuggestions.Clear();

                // Find vulnerable teammates: blue team heroes with HeroScore <= -2
                var vulnerableHeroes = blueTeamAnalysis
                    .Where(kvp => kvp.Value.HeroScore <= -2)
                    .OrderBy(kvp => kvp.Value.HeroScore)  // Most negative first
                    .ToList();

                foreach (var (heroName, analysis) in vulnerableHeroes)
                {
                    var counteredBy = TakeCounters(
                        analysis.HardCounteredBy, analysis.SoftCounteredBy,
                        c => redTeamAnalysis.ContainsKey(c.HeroName), "Hard counter", "Soft counter");

                    var isHardCountered = analysis.HardCounteredBy?
                        .Any(c => redTeamAnalysis.ContainsKey(c.HeroName)) ?? false;

                    VulnerableTeammates.Add(new VulnerableHeroViewModel
                    {
                        HeroName = HudFormat.HeroName(heroName),
                        HeroIcon = IconUtils.GetHeroIconAsBitmapSource(heroName, IconSize),
                        IsHardCountered = isHardCountered,
                        ScoreValue = analysis.HeroScore,
                        ScoreBrush = HudPalette.ScoreBrush(analysis.HeroScore),
                        ScoreBackgroundBrush = HudPalette.ScoreSoftBrush(analysis.HeroScore),
                        Compositions = HudFormat.CreateCompositionInfos(analysis.Hero?.BestCompositions),
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

                foreach (var (enemyName, analysis) in dangerousHeroes)
                {
                    // Who this enemy counters on blue team
                    var counters = TakeCounters(
                        analysis.HardCounters, analysis.SoftCounters,
                        c => blueTeamAnalysis.ContainsKey(c.HeroName), "Hard counter", "Soft counter");

                    // Swap suggestions to counter this enemy
                    var counteredBy = TakeCounters(
                        analysis.HardCounteredBy, analysis.SoftCounteredBy,
                        _ => true, "Hard counter", "Soft counter");

                    DangerousEnemies.Add(new DangerousHeroViewModel
                    {
                        HeroName = HudFormat.HeroName(enemyName),
                        HeroIcon = IconUtils.GetHeroIconAsBitmapSource(enemyName, IconSize),
                        IsHighThreat = counters.Any(c => c.IsHard),
                        ScoreValue = analysis.HeroScore,
                        ScoreBrush = HudPalette.ScoreBrush(analysis.HeroScore),
                        ScoreBackgroundBrush = HudPalette.ScoreSoftBrush(analysis.HeroScore),
                        Compositions = HudFormat.CreateCompositionInfos(analysis.Hero?.BestCompositions),
                        Counters = counters,
                        CounteredBy = counteredBy,
                        HasCounters = counters.Count > 0,
                        HasCounteredBy = counteredBy.Count > 0
                    });
                }

                // Calculate team swap suggestions - heroes that would help the team the most
                CalculateTeamSwapSuggestions(blueTeamAnalysis, redTeamAnalysis);

                UpdateSectionStates();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating CompositionDashboard: {ex.Message}");
            }
        }

        private void UpdateSectionStates()
        {
            NoVulnerableText.Visibility = VulnerableTeammates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoDangerousText.Visibility = DangerousEnemies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoTeamSwapsText.Visibility = TeamSwapSuggestions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoTeamSwapsText.Text = $"No swap gains {HudFormat.Score(AppSettings.Instance.MinScoreF3)} or more. Your lineup is already a good fit.";
        }

        private static List<CounterIconInfo> TakeCounters(
            IEnumerable<HeroCounter>? hard,
            IEnumerable<HeroCounter>? soft,
            Func<HeroCounter, bool> filter,
            string hardFallback,
            string softFallback)
        {
            var result = new List<CounterIconInfo>();
            if (hard != null)
            {
                foreach (var c in hard.Where(filter).Take(3))
                {
                    result.Add(new CounterIconInfo
                    {
                        Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName, IconSize),
                        IsHard = true,
                        Comment = c.Comment ?? hardFallback
                    });
                }
            }
            if (soft != null)
            {
                foreach (var c in soft.Where(filter).Take(3 - result.Count))
                {
                    result.Add(new CounterIconInfo
                    {
                        Icon = IconUtils.GetHeroIconAsBitmapSource(c.HeroName, IconSize),
                        IsHard = false,
                        Comment = c.Comment ?? softFallback
                    });
                }
            }
            return result;
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
            var eligibleSwaps = allSwaps
                .Where(x => x.scoreDiff >= minScoreDiff)
                .OrderByDescending(x => x.scoreDiff)
                .ToList();
            var topSwaps = eligibleSwaps.Take(MaxTeamSwaps);

            TeamSwapSummaryText.Text = eligibleSwaps.Count > MaxTeamSwaps
                ? $"top {MaxTeamSwaps} of {eligibleSwaps.Count} · min gain {HudFormat.Score(minScoreDiff)}"
                : $"biggest score gains first · min gain {HudFormat.Score(minScoreDiff)}";

            foreach (var (fromHeroName, fromScore, toHeroName, toAnalysis, scoreDiff) in topSwaps)
            {
                // Who on red team this hero counters / is countered by, and who on blue team it pairs with
                var counters = TakeCounters(
                    toAnalysis.HardCounters, toAnalysis.SoftCounters,
                    c => redTeamAnalysis.ContainsKey(c.HeroName), "Hard counter", "Soft counter");
                var counteredBy = TakeCounters(
                    toAnalysis.HardCounteredBy, toAnalysis.SoftCounteredBy,
                    c => redTeamAnalysis.ContainsKey(c.HeroName), "Hard countered by", "Soft countered by");

                var synergies = new List<CounterIconInfo>();
                if (toAnalysis.Synergies != null)
                {
                    foreach (var s in toAnalysis.Synergies.Where(s => blueTeamAnalysis.ContainsKey(s.HeroName)).Take(3))
                    {
                        synergies.Add(new CounterIconInfo
                        {
                            Icon = IconUtils.GetHeroIconAsBitmapSource(s.HeroName, IconSize),
                            IsHard = false,
                            Comment = s.Comment ?? "Good synergy"
                        });
                    }
                }

                blueTeamAnalysis.TryGetValue(fromHeroName, out var fromAnalysis);

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
                    FromHeroName = HudFormat.HeroName(fromHeroName),
                    FromHeroIcon = IconUtils.GetHeroIconAsBitmapSource(fromHeroName, IconSize),
                    FromScore = fromScore,
                    FromScoreBrush = HudPalette.ScoreBrush(fromScore),
                    FromScoreBackgroundBrush = HudPalette.ScoreSoftBrush(fromScore),
                    FromCompositions = HudFormat.CreateCompositionInfos(fromAnalysis?.Hero?.BestCompositions),
                    FromIncompatibleCompositions = HudFormat.CreateCompositionInfos(fromAnalysis?.Hero?.IncompatibleCompositions),
                    FromIsStrongMap = fromIsStrongMap,
                    FromIsWeakMap = fromIsWeakMap,
                    ToHeroName = HudFormat.HeroName(toHeroName),
                    ToHeroIcon = IconUtils.GetHeroIconAsBitmapSource(toHeroName, IconSize),
                    ToScore = toAnalysis.HeroScore,
                    ToScoreBrush = HudPalette.ScoreBrush(toAnalysis.HeroScore),
                    ToScoreBackgroundBrush = HudPalette.ScoreSoftBrush(toAnalysis.HeroScore),
                    ToCompositions = HudFormat.CreateCompositionInfos(toAnalysis.Hero?.BestCompositions),
                    ToIncompatibleCompositions = HudFormat.CreateCompositionInfos(toAnalysis.Hero?.IncompatibleCompositions),
                    ToIsStrongMap = toIsStrongMap,
                    ToIsWeakMap = toIsWeakMap,
                    ScoreDiff = scoreDiff,
                    ScoreDiffBrush = HudPalette.Good,
                    ScoreDiffBackgroundBrush = HudPalette.GoodSoft,
                    Counters = counters,
                    CounteredBy = counteredBy,
                    Synergies = synergies,
                    HasCounters = counters.Count > 0,
                    HasCounteredBy = counteredBy.Count > 0,
                    HasSynergies = synergies.Count > 0
                });
            }
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
        public Visibility ToMapVisibility => ToIsStrongMap || ToIsWeakMap ? Visibility.Visible : Visibility.Collapsed;
        public SolidColorBrush ToMapBrush => ToIsStrongMap ? HudPalette.Good : HudPalette.Bad;
        public SolidColorBrush ToMapBackground => ToIsStrongMap ? HudPalette.GoodSoft : HudPalette.BadSoft;
        public string ToMapText => ToIsStrongMap ? "Strong map" : "Weak map";

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
