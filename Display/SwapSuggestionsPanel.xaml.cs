using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Owmeta.Display
{
    /// <summary>
    /// F2: the composition table, the team threat, then the heroes you could swap to, ranked by score.
    /// With <see cref="AppSettings.GroupSwapsByComposition"/> the rows are grouped by the composition
    /// each hero is strong in, and the group that beats the enemy's clear tendency comes first.
    /// </summary>
    public partial class SwapSuggestionsPanel : UserControl
    {
        /// <summary>Rows that fit on screen under the composition table.</summary>
        public const int MaxSwaps = 10;

        public ObservableCollection<object> Rows { get; } = new();

        /// <summary>The player's hero and score, shown next to the view tabs.</summary>
        public (HeroName Hero, int Score)? CurrentHero { get; private set; }

        public SwapSuggestionsPanel()
        {
            InitializeComponent();
            SuggestionsControl.ItemsSource = Rows;
        }

        public void Clear() => Clear(null);

        /// <summary>
        /// Clears the panel. When <paramref name="notice"/> is set it is shown in place of the table.
        /// </summary>
        public void Clear(string? notice)
        {
            CurrentHero = null;
            Rows.Clear();
            Header.Update(null, null, null);
            Threat.Update(null);
            SwapSection.Visibility = Visibility.Collapsed;
            NoticeText.Text = notice ?? string.Empty;
            NoticeCard.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Shows both teams and the swaps for <paramref name="playerHero"/>. When that hero is not on the team,
        /// the composition table stays and <paramref name="missingHeroNotice"/> replaces the swap table.
        /// </summary>
        public void UpdateSuggestions(
            HeroName playerHero,
            IReadOnlyDictionary<HeroName, HeroAnalysis> ourTeam,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemyTeam,
            string? missingHeroNotice = null)
        {
            try
            {
                var threat = CompositionPolicy.FindThreat(enemyTeam, ourTeam);
                Header.Update(ourTeam, enemyTeam, threat?.Enemy);
                Threat.Update(threat);
                Rows.Clear();

                if (!CompositionPolicy.IsKnownHero(playerHero) || !ourTeam.TryGetValue(playerHero, out var current))
                {
                    CurrentHero = null;
                    SwapSection.Visibility = Visibility.Collapsed;
                    NoticeText.Text = missingHeroNotice ?? string.Empty;
                    NoticeCard.Visibility = missingHeroNotice == null ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }

                CurrentHero = (playerHero, current.HeroScore);
                NoticeCard.Visibility = Visibility.Collapsed;
                SwapSection.Visibility = Visibility.Visible;

                var minScore = AppSettings.Instance.MinScoreF2;
                var suggestions = current.SwapSuggestions?.ToList() ?? new List<HeroAnalysis>();
                var selection = SwapSuggestionPolicy.SelectSuggestions(suggestions, minScore, MaxSwaps);
                int eligible = suggestions.Count(s => s.HeroScore >= minScore);
                bool grouped = AppSettings.Instance.GroupSwapsByComposition;

                Logger.Log(
                    $"F2 swap suggestions for {playerHero}: total={selection.TotalCount}, eligible={eligible}, shown={selection.ShownCount}, minScore={minScore}, grouped={grouped}, recommended={Header.Tendency.Recommended}");

                var best = selection.Suggestions.FirstOrDefault();
                if (best != null && best.HeroScore <= current.HeroScore) best = null;

                bool IsEnemy(HeroName hero) => enemyTeam == null || enemyTeam.Count == 0 || enemyTeam.ContainsKey(hero);
                bool IsAlly(HeroName hero) => ourTeam.ContainsKey(hero) && hero != playerHero;

                int index = 0;
                void AddRow(HeroAnalysis swap, CompositionType? group) =>
                    Rows.Add(CreateRow(swap, group, swap == best, index++, threat?.Enemy, IsEnemy, IsAlly));

                if (grouped)
                {
                    foreach (var group in CompositionPolicy.Group(selection.Suggestions, Header.Tendency.Recommended))
                    {
                        Rows.Add(new SwapGroupViewModel(group.Composition, group.IsRecommended));
                        foreach (var swap in group.Heroes) AddRow(swap, group.Composition);
                    }
                }
                else
                {
                    foreach (var swap in selection.Suggestions) AddRow(swap, null);
                }

                NoSwapsCard.Visibility = selection.ShownCount == 0 ? Visibility.Visible : Visibility.Collapsed;
                NoSwapsText.Text = $"NO SWAP SCORES {HudFormat.Score(minScore)} OR MORE";

                int hidden = eligible - selection.ShownCount;
                MoreCard.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
                MoreText.Text = $"+{hidden} MORE";
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating SwapSuggestionsPanel: {ex.Message}");
            }
        }

        private static SwapRowViewModel CreateRow(
            HeroAnalysis swap,
            CompositionType? group,
            bool isBest,
            int index,
            HeroName? threat,
            Func<HeroName, bool> isEnemy,
            Func<HeroName, bool> isAlly)
        {
            var hero = swap.Hero?.Name ?? HeroName.Unknown;
            var alsoFits = new List<CompositionWord>();
            if (group.HasValue && group.Value != CompositionType.Unspecified)
            {
                foreach (var comp in CompositionPolicy.Compositions.Where(c => c != group.Value))
                {
                    var fit = CompositionPolicy.Fit(swap.Hero, comp);
                    // On the blue best-pick band, composition colors would vanish: use white instead.
                    if (fit == CompositionFit.Strong)
                        alsoFits.Insert(alsoFits.Count(w => w.Strong), new CompositionWord(Board.CompositionName(comp),
                            isBest ? HudPalette.TextPrimary : HudPalette.CompositionBrush(comp), true));
                    else if (fit == CompositionFit.Neutral)
                        alsoFits.Add(new CompositionWord(Board.CompositionName(comp),
                            isBest ? HudPalette.BestNeutralText : HudPalette.BoardLabel, false));
                }
            }

            return new SwapRowViewModel
            {
                Icon = Board.Portrait(hero),
                Name = HudFormat.HeroName(hero).ToUpperInvariant(),
                ScoreText = HudFormat.Score(swap.HeroScore),
                ScoreBrush = isBest ? HudPalette.TextPrimary : HudPalette.ScoreBrush(swap.HeroScore),
                Background = isBest ? HudPalette.RowBest : index % 2 == 0 ? HudPalette.RowA : HudPalette.RowB,
                AlsoFits = alsoFits,
                Counters = Board.Relations(swap.HardCounters, swap.SoftCounters, isEnemy, threat),
                CounteredBy = Board.Relations(swap.HardCounteredBy, swap.SoftCounteredBy, isEnemy, threat),
                Synergies = Board.Synergies(swap.Synergies, isAlly)
            };
        }
    }

    public sealed record CompositionWord(string Text, Brush Brush, bool Strong);

    public sealed class SwapGroupViewModel
    {
        public SwapGroupViewModel(CompositionType composition, bool isRecommended)
        {
            Name = Board.CompositionName(composition);
            Color = HudPalette.CompositionBrush(composition);
            Background = isRecommended ? HudPalette.CompositionBandBrush(composition) : HudPalette.GroupBand;
            MarkerVisibility = isRecommended ? Visibility.Visible : Visibility.Collapsed;
        }

        public string Name { get; }
        public Brush Color { get; }
        public Brush Background { get; }
        public Visibility MarkerVisibility { get; }
    }

    public sealed class SwapRowViewModel
    {
        public required ImageSource Icon { get; init; }
        public required string Name { get; init; }
        public required string ScoreText { get; init; }
        public required Brush ScoreBrush { get; init; }
        public required Brush Background { get; init; }
        public required List<CompositionWord> AlsoFits { get; init; }
        public required List<BoardIcon> Counters { get; init; }
        public required List<BoardIcon> CounteredBy { get; init; }
        public required List<BoardIcon> Synergies { get; init; }
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
