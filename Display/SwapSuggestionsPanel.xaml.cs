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
    /// F2: the composition table, then your hero as the first row of a table (who he counters, who
    /// counters him, his compositions), then the swap suggestions in the same columns, ranked by score.
    /// No suggestion is singled out. The composition to fit is computed without your own hero
    /// (<see cref="CompositionPolicy.FitTarget"/>). With <see cref="AppSettings.GroupSwapsByComposition"/>
    /// the suggestions are grouped by the composition each hero is strong in, the composition to fit first.
    /// </summary>
    public partial class SwapSuggestionsPanel : UserControl
    {
        /// <summary>Suggestion rows that fit on screen under your hero.</summary>
        public const int MaxOptions = 9;

        public ObservableCollection<object> Rows { get; } = new();

        /// <summary>The player's hero and score.</summary>
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
            CurrentRow.Content = null;
            Rows.Clear();
            Header.Update(null, null, null);
            SwapSection.Visibility = Visibility.Collapsed;
            NoticeText.Text = notice ?? string.Empty;
            NoticeCard.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Shows both teams and the swaps for <paramref name="playerHero"/>. When that hero is not on the team,
        /// the composition table stays and <paramref name="missingHeroNotice"/> replaces the swaps.
        /// </summary>
        public void UpdateSuggestions(
            HeroName playerHero,
            IReadOnlyDictionary<HeroName, HeroAnalysis> ourTeam,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemyTeam,
            string? missingHeroNotice = null)
        {
            try
            {
                Rows.Clear();
                CurrentRow.Content = null;

                if (!CompositionPolicy.IsKnownHero(playerHero) || !ourTeam.TryGetValue(playerHero, out var current))
                {
                    Header.Update(ourTeam, enemyTeam, CompositionPolicy.FitTarget(ourTeam, enemyTeam, null));
                    CurrentHero = null;
                    SwapSection.Visibility = Visibility.Collapsed;
                    NoticeText.Text = missingHeroNotice ?? string.Empty;
                    NoticeCard.Visibility = missingHeroNotice == null ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }

                var target = CompositionPolicy.FitTarget(ourTeam, enemyTeam, playerHero);
                Header.Update(ourTeam, enemyTeam, target);
                CurrentHero = (playerHero, current.HeroScore);
                NoticeCard.Visibility = Visibility.Collapsed;
                SwapSection.Visibility = Visibility.Visible;

                bool IsEnemy(HeroName hero) => enemyTeam == null || enemyTeam.Count == 0 || enemyTeam.ContainsKey(hero);
                // Synergies count towards the score only with the teammates you keep.
                bool IsTeammate(HeroName hero) => hero != playerHero && ourTeam.ContainsKey(hero);

                var minScore = AppSettings.Instance.MinScoreF2;
                // Only heroes the team can pick: known, and not already played by a teammate.
                var suggestions = (current.SwapSuggestions ?? Enumerable.Empty<HeroAnalysis>())
                    .Where(s => CompositionPolicy.IsCandidate(ourTeam, s))
                    .OrderByDescending(s => s.HeroScore)
                    .ToList();
                bool grouped = AppSettings.Instance.GroupSwapsByComposition;

                var selection = SwapSuggestionPolicy.SelectSuggestions(suggestions, minScore, MaxOptions);
                int eligible = suggestions.Count(s => s.HeroScore >= minScore);

                Logger.Log(
                    $"F2 swap suggestions for {playerHero}: total={selection.TotalCount}, eligible={eligible}, shown={selection.ShownCount}, minScore={minScore}, grouped={grouped}, target={target}");

                CurrentRow.Content = CreateRow(current, target, 0, IsEnemy, IsTeammate, isYou: true);
                var options = selection.Suggestions;

                int index = 0;
                void AddRow(HeroAnalysis swap) => Rows.Add(CreateRow(swap, target, index++, IsEnemy, IsTeammate));
                if (grouped)
                {
                    foreach (var group in CompositionPolicy.Group(options, target))
                    {
                        Rows.Add(new SwapGroupViewModel(group.Composition, group.IsRecommended));
                        foreach (var swap in group.Heroes) AddRow(swap);
                    }
                }
                else
                {
                    foreach (var swap in options) AddRow(swap);
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
            HeroAnalysis analysis,
            CompositionType? target,
            int index,
            Func<HeroName, bool> isEnemy,
            Func<HeroName, bool> isTeammate,
            bool isYou = false)
        {
            var hero = analysis.Hero?.Name ?? HeroName.Unknown;
            var background = isYou ? HudPalette.RowYou : index % 2 == 0 ? HudPalette.RowA : HudPalette.RowB;
            return new SwapRowViewModel
            {
                Icon = Board.Portrait(hero),
                Name = HudFormat.HeroName(hero).ToUpperInvariant(),
                ScoreText = HudFormat.Score(analysis.HeroScore),
                ScoreBrush = HudPalette.ScoreBrush(analysis.HeroScore),
                Background = background,
                EdgeBrush = isYou ? HudPalette.Ally : background,
                EdgeAccent = isYou ? HudPalette.Ally : null,
                YouVisibility = isYou ? Visibility.Visible : Visibility.Collapsed,
                Counters = Board.Relations(analysis.HardCounters, analysis.SoftCounters, isEnemy, HudPalette.Good),
                CounteredBy = Board.Relations(analysis.HardCounteredBy, analysis.SoftCounteredBy, isEnemy, HudPalette.Bad),
                Synergies = Board.Synergies(analysis.Synergies, isTeammate),
                Fits = Board.Fits(analysis.Hero, target)
            };
        }
    }

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
        public required Brush EdgeBrush { get; init; }
        public Brush? EdgeAccent { get; init; }
        public Visibility YouVisibility { get; init; } = Visibility.Collapsed;
        public required List<BoardIcon> Counters { get; init; }
        public required List<BoardIcon> CounteredBy { get; init; }
        public required List<BoardIcon> Synergies { get; init; }
        public required List<CompositionWord> Fits { get; init; }
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
