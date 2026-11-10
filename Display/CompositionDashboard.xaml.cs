using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// F3: the composition table, then the enemies to counter first (ranked by
    /// <see cref="CompositionPolicy.RankThreats"/>) with the swap that answers each one, then your team:
    /// who each hero counters and is countered by, the compositions he is good and bad in, and the hero
    /// to change to. Both come from one <see cref="CompositionPolicy.PlanSwaps"/>, so the threat answers
    /// and the team changes always agree and no hero is suggested twice.
    /// </summary>
    public partial class CompositionDashboard : UserControl
    {
        /// <summary>Enemy rows that fit on screen above your team.</summary>
        public const int MaxThreats = 3;

        public ObservableCollection<ThreatRowViewModel> Threats { get; } = new();
        public ObservableCollection<TeamRowViewModel> Team { get; } = new();

        public CompositionDashboard()
        {
            InitializeComponent();
            ThreatList.ItemsSource = Threats;
            TeamList.ItemsSource = Team;
            Clear();
        }

        public void Clear()
        {
            Threats.Clear();
            Team.Clear();
            Header.Update(null, null, null);
            UpdateTeamTitle();
            NoThreatsCard.Visibility = Visibility.Collapsed;
        }

        public void Update(
            Dictionary<HeroName, HeroAnalysis> blueTeamAnalysis,
            Dictionary<HeroName, HeroAnalysis> redTeamAnalysis,
            MapName? currentMap = null)
        {
            try
            {
                var target = CompositionPolicy.FitTarget(blueTeamAnalysis, redTeamAnalysis, null);
                Header.Update(blueTeamAnalysis, redTeamAnalysis, target);
                UpdateTeamTitle();
                var plan = CompositionPolicy.PlanSwaps(blueTeamAnalysis, redTeamAnalysis, AppSettings.Instance.MinScoreF3, MaxThreats);
                UpdateThreats(plan, blueTeamAnalysis.Count > 0 && redTeamAnalysis.Count > 0);
                UpdateTeam(blueTeamAnalysis, redTeamAnalysis, target, plan);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating CompositionDashboard: {ex.Message}");
            }
        }

        private void UpdateTeamTitle()
        {
            AllyScoreText.Text = Header.AllyScoreText;
            EnemyScoreText.Text = Header.EnemyScoreText;
            if (Header.AllyTotal.HasValue && Header.EnemyTotal.HasValue)
            {
                int diff = Header.AllyTotal.Value - Header.EnemyTotal.Value;
                BalanceText.Text = diff < 0 ? $"· BEHIND BY {-diff}" : diff > 0 ? $"· AHEAD BY {diff}" : "· EVEN";
                BalanceText.Foreground = HudPalette.ScoreBrush(diff);
            }
            else
            {
                BalanceText.Text = string.Empty;
            }
        }

        private void UpdateThreats(SwapPlan plan, bool bothTeams)
        {
            Threats.Clear();
            foreach (var (threat, answer, shared) in plan.Threats)
            {
                string note = threat.IsHandled ? "ALREADY HANDLED · NO SWAP NEEDED"
                    : answer == null ? "NO SWAP COUNTERS HIM" : string.Empty;
                Threats.Add(new ThreatRowViewModel
                {
                    Icon = Board.Portrait(threat.Enemy),
                    Name = HudFormat.HeroName(threat.Enemy).ToUpperInvariant(),
                    Background = threat.IsHandled ? HudPalette.RowHandled : HudPalette.RowThreat,
                    EdgeBrush = threat.IsHandled ? HudPalette.RowHandled : HudPalette.Enemy,
                    Victims = Board.Links(threat.Victims, HudPalette.Bad),
                    AnsweredBy = Board.Links(threat.AnsweredBy, HudPalette.Good),
                    FromIcon = answer == null ? null : Board.Portrait(answer.From),
                    FromName = answer == null ? string.Empty : HudFormat.HeroName(answer.From).ToUpperInvariant(),
                    ToIcon = answer == null ? null : Board.Portrait(answer.ToHero),
                    ToName = answer == null ? string.Empty : HudFormat.HeroName(answer.ToHero).ToUpperInvariant(),
                    ToScore = answer == null ? string.Empty : HudFormat.Score(answer.To.HeroScore),
                    Hint = shared ? "SAME SWAP" : string.Empty,
                    Note = note
                });
            }
            NoThreatsCard.Visibility = Threats.Count == 0 && bothTeams ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateTeam(
            Dictionary<HeroName, HeroAnalysis> blue,
            Dictionary<HeroName, HeroAnalysis> red,
            CompositionType? target,
            SwapPlan plan)
        {
            Team.Clear();
            int i = 0;
            foreach (var (hero, analysis) in CompositionPolicy.Ordered(blue))
            {
                var change = plan.Changes.TryGetValue(hero, out var planned) ? planned : null;
                // A change is urgent for a hero who is losing his matchups, optional otherwise.
                bool urgent = change != null && analysis.HeroScore < 0;
                var background = urgent ? HudPalette.RowChange : i++ % 2 == 0 ? HudPalette.RowA : HudPalette.RowB;
                Team.Add(new TeamRowViewModel
                {
                    Icon = Board.Portrait(hero),
                    Name = HudFormat.HeroName(hero).ToUpperInvariant(),
                    ScoreText = HudFormat.Score(analysis.HeroScore),
                    ScoreBrush = HudPalette.ScoreBrush(analysis.HeroScore),
                    Background = background,
                    EdgeBrush = change == null ? background : urgent ? HudPalette.Bad : HudPalette.Warn,
                    Counters = Board.Relations(analysis.HardCounters, analysis.SoftCounters, red.ContainsKey, HudPalette.Good),
                    CounteredBy = Board.Relations(analysis.HardCounteredBy, analysis.SoftCounteredBy, red.ContainsKey, HudPalette.Bad),
                    Synergies = Board.Synergies(analysis.Synergies, h => h != hero && blue.ContainsKey(h)),
                    Fits = Board.Fits(analysis.Hero, target),
                    ChangeIcon = change == null ? null : Board.Portrait(change.ToHero),
                    ChangeName = change == null ? string.Empty : HudFormat.HeroName(change.ToHero).ToUpperInvariant(),
                    ChangeScore = change == null ? string.Empty : HudFormat.Score(change.To.HeroScore)
                });
            }
        }
    }

    public sealed class ThreatRowViewModel
    {
        public required ImageSource Icon { get; init; }
        public required string Name { get; init; }
        public required Brush Background { get; init; }
        public required Brush EdgeBrush { get; init; }
        public required List<BoardIcon> Victims { get; init; }
        public required List<BoardIcon> AnsweredBy { get; init; }
        public ImageSource? FromIcon { get; init; }
        public required string FromName { get; init; }
        public ImageSource? ToIcon { get; init; }
        public required string ToName { get; init; }
        public required string ToScore { get; init; }
        public required string Hint { get; init; }
        public required string Note { get; init; }

        public Visibility AnswerVisibility => ToIcon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed class TeamRowViewModel
    {
        public required ImageSource Icon { get; init; }
        public required string Name { get; init; }
        public required string ScoreText { get; init; }
        public required Brush ScoreBrush { get; init; }
        public required Brush Background { get; init; }
        public required Brush EdgeBrush { get; init; }
        public required List<BoardIcon> Counters { get; init; }
        public required List<BoardIcon> CounteredBy { get; init; }
        public required List<BoardIcon> Synergies { get; init; }
        public required List<CompositionWord> Fits { get; init; }
        public ImageSource? ChangeIcon { get; init; }
        public required string ChangeName { get; init; }
        public required string ChangeScore { get; init; }

        public Visibility ChangeVisibility => ChangeIcon != null ? Visibility.Visible : Visibility.Collapsed;
    }
}
