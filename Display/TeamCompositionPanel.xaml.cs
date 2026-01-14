using Owmeta.Model;
using Owmeta.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Owmeta.Display
{
    public partial class TeamCompositionPanel : UserControl
    {
        private static readonly Dictionary<CompositionType, (string Color, string PathData)> CompositionStyles = new()
        {
            { CompositionType.Brawl, ("#fb923c", "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z") },
            { CompositionType.Dive, ("#38bdf8", "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z") },
            { CompositionType.Poke, ("#c084fc", "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12zm0 4a2 2 0 1 0 0 4 2 2 0 0 0 0-4z") }
        };

        // Neon glow colors
        private static readonly Color CyanGlow = Color.FromRgb(0x00, 0xf0, 0xff);      // #00f0ff
        private static readonly Color MagentaGlow = Color.FromRgb(0xff, 0x00, 0xaa);   // #ff00aa
        private static readonly Color CyanBorder = Color.FromRgb(0x38, 0xbd, 0xf8);    // #38bdf8
        private static readonly Color MagentaBorder = Color.FromRgb(0xf4, 0x3f, 0x5e); // #f43f5e

        private bool isBlueTeam;
        private bool isCompact;
        private readonly int ICON_SIZE = 54;

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

        private void UpdateTeamStyling()
        {
            // Update border color
            MainBorder.BorderBrush = new SolidColorBrush(isBlueTeam ? CyanBorder : MagentaBorder);

            // Update glow effect
            if (MainBorder.Effect is DropShadowEffect glowEffect)
            {
                glowEffect.Color = isBlueTeam ? CyanGlow : MagentaGlow;
            }

            // Update team label
            TeamLabelText.Text = isBlueTeam ? "YOUR TEAM" : "ENEMY TEAM";
            TeamLabelText.Foreground = new SolidColorBrush(isBlueTeam ? CyanBorder : MagentaBorder);
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

        private void UpdateViewMode()
        {
            if (isCompact)
            {
                CompactList.Visibility = Visibility.Visible;
                CompositionList.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompactList.Visibility = Visibility.Collapsed;
                CompositionList.Visibility = Visibility.Visible;
            }
        }

        public TeamCompositionPanel()
        {
            InitializeComponent();
            DataContext = this;
            CompositionList.ItemsSource = Compositions;
            CompactList.ItemsSource = Compositions;
        }

        public void UpdateCompositions(Dictionary<HeroName, HeroAnalysis> teamData)
        {
            if (teamData == null) return;

            try
            {
                // Calculate and display team score
                int teamScore = teamData.Values.Sum(a => a.HeroScore);
                UpdateScoreDisplay(teamScore);
                var compositions = new List<CompositionViewModel>();

                foreach (var compType in Enum.GetValues<CompositionType>().Where(c => c != CompositionType.Unspecified))
                {
                    var (color, pathData) = CompositionStyles[compType];
                    var goodHeroes = new List<ImageSource>();
                    var neutralHeroes = new List<ImageSource>();
                    var badHeroes = new List<ImageSource>();

                    foreach (var (hero, analysis) in teamData)
                    {
                        var heroImage = IconUtils.GetHeroIconAsBitmapSource(hero, ICON_SIZE);

                        if (analysis.Hero?.BestCompositions?.Contains(compType) ?? false)
                        {
                            goodHeroes.Add(heroImage);
                        }
                        else if (analysis.Hero?.IncompatibleCompositions?.Contains(compType) ?? false)
                        {
                            badHeroes.Add(heroImage);
                        }
                        else
                        {
                            neutralHeroes.Add(heroImage);
                        }
                    }

                    compositions.Add(new CompositionViewModel
                    {
                        Name = compType.ToString(),
                        Color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                        PathData = pathData,
                        GoodHeroes = goodHeroes,
                        NeutralHeroes = neutralHeroes,
                        BadHeroes = badHeroes,
                        GoodCount = goodHeroes.Count,
                        OkCount = neutralHeroes.Count,
                        BadCount = badHeroes.Count
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
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to update compositions: {ex.Message}");
            }
        }

        private void UpdateScoreDisplay(int score)
        {
            LastScore = score;
            string prefix = score > 0 ? "+" : "";
            TeamScoreText.Text = $"{prefix}{score}";

            // Score colors
            var greenColor = Color.FromRgb(0x10, 0xB9, 0x81);  // #10B981
            var redColor = Color.FromRgb(0xEF, 0x44, 0x44);    // #EF4444

            TeamScoreText.Foreground = new SolidColorBrush(score >= 0 ? greenColor : redColor);

            // Update badge background with transparency
            var bgColor = score >= 0 ? greenColor : redColor;
            ScoreBadgeBackground.Color = Color.FromArgb(0x26, bgColor.R, bgColor.G, bgColor.B); // 15% opacity
        }
    }

    public class CompositionViewModel
    {
        public required string Name { get; set; }
        public required Brush Color { get; set; }
        public required string PathData { get; set; }
        public required List<ImageSource> GoodHeroes { get; set; }
        public required List<ImageSource> NeutralHeroes { get; set; }
        public required List<ImageSource> BadHeroes { get; set; }
        public int GoodCount { get; set; }
        public int OkCount { get; set; }
        public int BadCount { get; set; }
        public Visibility HasGoodHeroes => GoodHeroes.Any() ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasNeutralHeroes => NeutralHeroes.Any() ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasBadHeroes => BadHeroes.Any() ? Visibility.Visible : Visibility.Collapsed;
    }
}