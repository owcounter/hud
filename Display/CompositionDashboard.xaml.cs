using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// F3: the composition table with team scores, the team threat, both rosters side by side,
    /// then the swaps that would help the team most.
    /// </summary>
    public partial class CompositionDashboard : UserControl
    {
        // What fits on screen under the rosters.
        private const int MaxTeamSwaps = 6;
        private const int MaxSwapsPerTeammate = 2;

        private const int AtRiskScore = -2;
        private const int DangerousScore = 3;

        public ObservableCollection<RosterRowViewModel> Allies { get; } = new();
        public ObservableCollection<RosterRowViewModel> Enemies { get; } = new();
        public ObservableCollection<TeamSwapRowViewModel> TeamSwaps { get; } = new();

        public CompositionDashboard()
        {
            InitializeComponent();
            AllyList.ItemsSource = Allies;
            EnemyList.ItemsSource = Enemies;
            TeamSwapList.ItemsSource = TeamSwaps;
            Clear();
        }

        public void Clear()
        {
            Allies.Clear();
            Enemies.Clear();
            TeamSwaps.Clear();
            Header.Update(null, null, null);
            Threat.Update(null);
            UpdateEmptyState();
        }

        public void Update(
            Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis,
            Dictionary<HeroName, HeroAnalysis> redTeamAnalysis,
            MapName? currentMap = null)
        {
            try
            {
                var threat = CompositionPolicy.FindThreat(redTeamAnalysis, blueTeamAnalysis);
                Header.Update(blueTeamAnalysis, redTeamAnalysis, threat?.Enemy);
                Threat.Update(threat);

                Allies.Clear();
                int i = 0;
                foreach (var (hero, analysis) in CompositionPolicy.Ordered(blueTeamAnalysis))
                {
                    Allies.Add(RosterRow(hero, analysis, i++ % 2 == 0 ? HudPalette.AllyRowA : HudPalette.AllyRowB,
                        analysis.HeroScore <= AtRiskScore ? HudPalette.Bad : null,
                        Board.Relations(analysis.HardCounteredBy, analysis.SoftCounteredBy, redTeamAnalysis.ContainsKey, threat?.Enemy)));
                }

                Enemies.Clear();
                i = 0;
                foreach (var (hero, analysis) in CompositionPolicy.Ordered(redTeamAnalysis))
                {
                    Enemies.Add(RosterRow(hero, analysis, i++ % 2 == 0 ? HudPalette.EnemyRowA : HudPalette.EnemyRowB,
                        analysis.HeroScore >= DangerousScore ? HudPalette.Warn : null,
                        Board.Relations(analysis.HardCounters, analysis.SoftCounters, blueTeamAnalysis.ContainsKey, null)));
                }

                UpdateTeamSwaps(blueTeamAnalysis, redTeamAnalysis, threat?.Enemy);
                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating CompositionDashboard: {ex.Message}");
            }
        }

        private static RosterRowViewModel RosterRow(HeroName hero, HeroAnalysis analysis, Brush background, Brush? edge, List<BoardIcon> icons) => new()
        {
            Icon = Board.Portrait(hero),
            Name = HudFormat.HeroName(hero).ToUpperInvariant(),
            ScoreText = HudFormat.Score(analysis.HeroScore),
            ScoreBrush = HudPalette.ScoreBrush(analysis.HeroScore),
            Background = background,
            EdgeBrush = edge ?? background,
            Icons = icons
        };

        private void UpdateEmptyState()
        {
            NoTeamSwapsCard.Visibility = TeamSwaps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoTeamSwapsText.Text = $"NO TEAM SWAP GAINS {HudFormat.Score(AppSettings.Instance.MinScoreF3)} OR MORE";
        }

        private void UpdateTeamSwaps(
            Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis,
            Dictionary<HeroName, HeroAnalysis> redTeamAnalysis,
            HeroName? threat)
        {
            TeamSwaps.Clear();

            // For each teammate, the swaps that raise his score, biggest gain first. At most two per
            // teammate, so one badly placed hero does not fill the list (F2 already lists his swaps).
            var minGain = AppSettings.Instance.MinScoreF3;
            var swaps = new List<(HeroName From, HeroAnalysis To, int Gain)>();
            foreach (var (from, fromAnalysis) in blueTeamAnalysis)
            {
                if (!CompositionPolicy.IsKnownHero(from) || fromAnalysis.SwapSuggestions == null) continue;
                foreach (var suggestion in fromAnalysis.SwapSuggestions)
                {
                    var to = suggestion.Hero?.Name ?? HeroName.Unknown;
                    if (!CompositionPolicy.IsKnownHero(to) || blueTeamAnalysis.ContainsKey(to)) continue;
                    int gain = suggestion.HeroScore - fromAnalysis.HeroScore;
                    if (gain > 0 && gain >= minGain) swaps.Add((from, suggestion, gain));
                }
            }

            int index = 0;
            var shown = swaps.OrderByDescending(s => s.Gain)
                .GroupBy(s => s.From).SelectMany(g => g.Take(MaxSwapsPerTeammate))
                .OrderByDescending(s => s.Gain)
                .Take(MaxTeamSwaps);
            foreach (var (from, to, gain) in shown)
            {
                bool best = index == 0;
                var toHero = to.Hero.Name;
                TeamSwaps.Add(new TeamSwapRowViewModel
                {
                    FromIcon = Board.Portrait(from),
                    FromName = HudFormat.HeroName(from),
                    FromBrush = best ? HudPalette.TextPrimary : HudPalette.TextSecondary,
                    ToIcon = Board.Portrait(toHero),
                    ToName = HudFormat.HeroName(toHero).ToUpperInvariant(),
                    GainText = HudFormat.Score(gain),
                    GainBrush = best ? HudPalette.TextPrimary : HudPalette.Good,
                    Background = best ? HudPalette.RowBest : index % 2 == 0 ? HudPalette.RowA : HudPalette.RowB,
                    Counters = Board.Relations(to.HardCounters, to.SoftCounters, redTeamAnalysis.ContainsKey, threat),
                    CounteredBy = Board.Relations(to.HardCounteredBy, to.SoftCounteredBy, redTeamAnalysis.ContainsKey, threat),
                    StrongIn = CompositionPolicy.StrongCompositions(to.Hero)
                        .Select(c => new CompositionWord(Board.CompositionName(c), best ? HudPalette.TextPrimary : HudPalette.CompositionBrush(c), true))
                        .ToList()
                });
                index++;
            }
        }
    }

    public sealed class RosterRowViewModel
    {
        public required ImageSource Icon { get; init; }
        public required string Name { get; init; }
        public required string ScoreText { get; init; }
        public required Brush ScoreBrush { get; init; }
        public required Brush Background { get; init; }
        public required Brush EdgeBrush { get; init; }
        public required List<BoardIcon> Icons { get; init; }
    }

    public sealed class TeamSwapRowViewModel
    {
        public required ImageSource FromIcon { get; init; }
        public required string FromName { get; init; }
        public required Brush FromBrush { get; init; }
        public required ImageSource ToIcon { get; init; }
        public required string ToName { get; init; }
        public required string GainText { get; init; }
        public required Brush GainBrush { get; init; }
        public required Brush Background { get; init; }
        public required List<BoardIcon> Counters { get; init; }
        public required List<BoardIcon> CounteredBy { get; init; }
        public required List<CompositionWord> StrongIn { get; init; }
    }
}
