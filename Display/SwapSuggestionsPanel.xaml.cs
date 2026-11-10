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
    /// F2: the composition table, then your hero next to the best swap (who counters you, who it
    /// counters), then the other heroes you could swap to, ranked by score. The composition to fit is
    /// computed without your own hero (<see cref="CompositionPolicy.FitTarget"/>); the best swap is never
    /// a hero who is bad in it. With <see cref="AppSettings.GroupSwapsByComposition"/> the options are
    /// grouped by the composition each hero is strong in, the composition to fit first.
    /// </summary>
    public partial class SwapSuggestionsPanel : UserControl
    {
        /// <summary>Option rows that fit on screen under the best swap.</summary>
        public const int MaxOptions = 8;

        public ObservableCollection<object> Rows { get; } = new();

        /// <summary>The player's hero and score.</summary>
        public (HeroName Hero, int Score)? CurrentHero { get; private set; }

        /// <summary>The swap shown next to your hero, if any.</summary>
        public HeroName? BestSwap { get; private set; }

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
            BestSwap = null;
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
                BestSwap = null;

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

                var minScore = AppSettings.Instance.MinScoreF2;
                // Only heroes the team can pick: known, and not already played by a teammate.
                var suggestions = (current.SwapSuggestions ?? Enumerable.Empty<HeroAnalysis>())
                    .Where(s => CompositionPolicy.IsCandidate(ourTeam, s))
                    .OrderByDescending(s => s.HeroScore)
                    .ToList();
                bool grouped = AppSettings.Instance.GroupSwapsByComposition;

                // The best swap, from the whole list: it must reach the minimum score, beat yours, and
                // not be bad in the composition to fit.
                var best = suggestions.FirstOrDefault(s =>
                    s.HeroScore >= minScore &&
                    s.HeroScore > current.HeroScore &&
                    (!target.HasValue || CompositionPolicy.Fit(s.Hero, target.Value) != CompositionFit.Weak));
                BestSwap = best?.Hero.Name;

                var others = suggestions.Where(s => s != best).ToList();
                var selection = SwapSuggestionPolicy.SelectSuggestions(others, minScore, MaxOptions);
                int eligible = others.Count(s => s.HeroScore >= minScore);

                Logger.Log(
                    $"F2 swap suggestions for {playerHero}: total={selection.TotalCount}, eligible={eligible}, shown={selection.ShownCount}, minScore={minScore}, grouped={grouped}, target={target}, best={BestSwap}");

                ShowVerdict(playerHero, current, best, target, IsEnemy);

                var options = selection.Suggestions;
                OptionsHint.Text = $"· {RoleName(current.Hero?.Role)} · BEST FIRST";

                int index = 0;
                void AddRow(HeroAnalysis swap) => Rows.Add(CreateRow(swap, target, index++, IsEnemy));
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
                NoSwapsText.Text = best == null
                    ? $"NO SWAP SCORES {HudFormat.Score(minScore)} OR MORE"
                    : $"NO OTHER SWAP SCORES {HudFormat.Score(minScore)} OR MORE";

                int hidden = eligible - selection.ShownCount;
                MoreCard.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
                MoreText.Text = $"+{hidden} MORE";
            }
            catch (Exception ex)
            {
                Logger.Log($"Error updating SwapSuggestionsPanel: {ex.Message}");
            }
        }

        private void ShowVerdict(
            HeroName playerHero,
            HeroAnalysis current,
            HeroAnalysis? best,
            CompositionType? target,
            Func<HeroName, bool> isEnemy)
        {
            CurrentAvatar.Source = Board.Portrait(playerHero);
            CurrentAvatar.Accent = HudPalette.ScoreBrush(current.HeroScore);
            CurrentName.Text = HudFormat.HeroName(playerHero).ToUpperInvariant();
            CurrentScore.Text = HudFormat.Score(current.HeroScore);
            CurrentScore.Foreground = HudPalette.ScoreBrush(current.HeroScore);
            CurrentFits.ItemsSource = Board.Fits(current.Hero, target);
            var counteredBy = Board.Relations(current.HardCounteredBy, current.SoftCounteredBy, isEnemy, HudPalette.Bad);
            CurrentCounteredBy.ItemsSource = counteredBy;
            CurrentRelations.Visibility = counteredBy.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            bool hasBest = best?.Hero != null;
            SwapArrow.Visibility = hasBest ? Visibility.Visible : Visibility.Collapsed;
            BestAvatar.Visibility = hasBest ? Visibility.Visible : Visibility.Collapsed;
            BestPanel.Visibility = hasBest ? Visibility.Visible : Visibility.Collapsed;
            NoBetterText.Visibility = hasBest ? Visibility.Collapsed : Visibility.Visible;
            if (!hasBest) return;

            var hero = best!.Hero.Name;
            BestAvatar.Source = Board.Portrait(hero);
            BestName.Text = HudFormat.HeroName(hero).ToUpperInvariant();
            BestScore.Text = HudFormat.Score(best.HeroScore);
            BestFits.ItemsSource = Board.Fits(best.Hero, target);
            var counters = Board.Relations(best.HardCounters, best.SoftCounters, isEnemy, HudPalette.Good);
            BestCounters.ItemsSource = counters;
            BestRelations.Visibility = counters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static SwapRowViewModel CreateRow(
            HeroAnalysis swap,
            CompositionType? target,
            int index,
            Func<HeroName, bool> isEnemy)
        {
            var hero = swap.Hero?.Name ?? HeroName.Unknown;
            return new SwapRowViewModel
            {
                Icon = Board.Portrait(hero),
                Name = HudFormat.HeroName(hero).ToUpperInvariant(),
                ScoreText = HudFormat.Score(swap.HeroScore),
                ScoreBrush = HudPalette.ScoreBrush(swap.HeroScore),
                Background = index % 2 == 0 ? HudPalette.RowA : HudPalette.RowB,
                Counters = Board.Relations(swap.HardCounters, swap.SoftCounters, isEnemy, HudPalette.Good),
                CounteredBy = Board.Relations(swap.HardCounteredBy, swap.SoftCounteredBy, isEnemy, HudPalette.Bad),
                Fits = Board.Fits(swap.Hero, target)
            };
        }

        private static string RoleName(HeroRole? role) => role switch
        {
            HeroRole.Tank => "TANKS",
            HeroRole.Damage => "DAMAGE",
            HeroRole.Support => "SUPPORTS",
            _ => "HEROES"
        };
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
        public required List<BoardIcon> Counters { get; init; }
        public required List<BoardIcon> CounteredBy { get; init; }
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
