using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Owmeta.Display
{
    public partial class TeamCompositionPanel : UserControl
    {
        private const int IconSize = 128;

        private bool isBlueTeam;
        private bool isCompact;

        public ObservableCollection<CompositionViewModel> Compositions { get; } = new();
        public int LastScore { get; private set; }

        public bool IsBlueTeam
        {
            get => isBlueTeam;
            set
            {
                isBlueTeam = value;
                UpdateTeamStyling();
            }
        }

        public bool IsCompact
        {
            get => isCompact;
            set
            {
                isCompact = value;
                UpdateViewMode();
            }
        }

        public TeamCompositionPanel()
        {
            InitializeComponent();
            CompositionList.ItemsSource = Compositions;
            CompactList.ItemsSource = Compositions;
            UpdateTeamStyling();
            Clear();
        }

        private void UpdateTeamStyling()
        {
            var teamBrush = isBlueTeam ? HudPalette.Ally : HudPalette.Enemy;
            var label = isBlueTeam ? "YOUR TEAM" : "ENEMY TEAM";

            TeamAccentBar.Background = teamBrush;
            CompactAccentBar.Background = teamBrush;
            TeamLabelText.Text = label;
            TeamLabelText.Foreground = teamBrush;
            CompactTeamLabel.Text = label;
            CompactTeamLabel.Foreground = teamBrush;
        }

        private void UpdateViewMode()
        {
            CompactView.Visibility = isCompact ? Visibility.Visible : Visibility.Collapsed;
            FullHeader.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
            FullView.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
            MainBorder.Padding = isCompact ? new Thickness(22, 14, 22, 14) : new Thickness(24, 18, 24, 12);
        }

        public void Clear()
        {
            LastScore = 0;
            Compositions.Clear();
            SetScoreText("--", HudPalette.TextMuted);
            UpdateEmptyState();
        }

        public void UpdateCompositions(Dictionary<HeroName, HeroAnalysis> teamData)
        {
            if (teamData == null || teamData.Count == 0) { Clear(); return; }

            try
            {
                UpdateScoreDisplay(teamData.Values.Sum(a => a.HeroScore));

                int teamSize = teamData.Count;
                var compositions = new List<CompositionViewModel>();

                foreach (var compType in Enum.GetValues<CompositionType>().Where(c => c != CompositionType.Unspecified))
                {
                    var style = HudFormat.GetCompositionStyle(compType);
                    var good = new List<CompositionHeroFit>();
                    var neutral = new List<CompositionHeroFit>();
                    var bad = new List<CompositionHeroFit>();

                    foreach (var (hero, analysis) in teamData)
                    {
                        var icon = IconUtils.GetHeroIconAsBitmapSource(hero, IconSize);

                        if (analysis.Hero?.BestCompositions?.Contains(compType) ?? false)
                            good.Add(new CompositionHeroFit { Icon = icon, Accent = HudPalette.Good });
                        else if (analysis.Hero?.IncompatibleCompositions?.Contains(compType) ?? false)
                            bad.Add(new CompositionHeroFit { Icon = icon, Accent = HudPalette.Bad, Dimmed = true });
                        else
                            neutral.Add(new CompositionHeroFit { Icon = icon });
                    }

                    var segments = new List<Brush>();
                    segments.AddRange(good.Select(_ => (Brush)HudPalette.Good));
                    segments.AddRange(neutral.Select(_ => (Brush)HudPalette.Track));
                    segments.AddRange(bad.Select(_ => (Brush)HudPalette.Bad));

                    compositions.Add(new CompositionViewModel
                    {
                        Name = compType.ToString(),
                        Color = style.Foreground,
                        PathData = style.PathData,
                        Heroes = good.Concat(neutral).Concat(bad).ToList(),
                        Segments = segments,
                        GoodCount = good.Count,
                        OkCount = neutral.Count,
                        BadCount = bad.Count,
                        TeamSize = teamSize
                    });
                }

                var sortedComps = compositions
                    .OrderByDescending(c => c.GoodCount)
                    .ThenByDescending(c => c.OkCount)
                    .ThenBy(c => c.BadCount)
                    .ToList();

                Compositions.Clear();
                foreach (var comp in sortedComps)
                {
                    Compositions.Add(comp);
                }

                UpdateEmptyState();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to update compositions: {ex.Message}");
            }
        }

        private void UpdateEmptyState()
        {
            var empty = Compositions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CompactEmptyText.Visibility = empty;
            FullEmptyText.Visibility = empty;
        }

        private void UpdateScoreDisplay(int score)
        {
            LastScore = score;
            SetScoreText(HudFormat.Score(score), HudPalette.ScoreBrush(score));
        }

        private void SetScoreText(string text, Brush brush)
        {
            TeamScoreText.Text = text;
            TeamScoreText.Foreground = brush;
            CompactScoreText.Text = text;
            CompactScoreText.Foreground = brush;
        }
    }

    public class CompositionViewModel
    {
        public required string Name { get; set; }
        public required Brush Color { get; set; }
        public required string PathData { get; set; }
        public required List<CompositionHeroFit> Heroes { get; set; }
        public required List<Brush> Segments { get; set; }
        public int GoodCount { get; set; }
        public int OkCount { get; set; }
        public int BadCount { get; set; }
        public int TeamSize { get; set; }
        public string FitText => $"{GoodCount}/{TeamSize}";
        public string FitDescription => BadCount > 0
            ? $"{GoodCount} fit · {BadCount} conflict{(BadCount == 1 ? "" : "s")}"
            : $"{GoodCount} of {TeamSize} fit";
    }

    public class CompositionHeroFit
    {
        public required ImageSource Icon { get; set; }
        public Brush? Accent { get; set; }
        public bool Dimmed { get; set; }
    }
}
