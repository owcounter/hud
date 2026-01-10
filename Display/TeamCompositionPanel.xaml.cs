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
        private static readonly Dictionary<CompositionType, (string Color, string PathData)> CompositionStyles = new()
        {
            { CompositionType.Brawl, ("#fb923c", "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z") },
            { CompositionType.Dive, ("#38bdf8", "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z") },
            { CompositionType.Poke, ("#c084fc", "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12zm0 4a2 2 0 1 0 0 4 2 2 0 0 0 0-4z") }
        };

        private bool isBlueTeam;
        private bool isCompact;
        private readonly int ICON_SIZE = 54;

        public ObservableCollection<CompositionViewModel> Compositions { get; } = new();

        public bool IsBlueTeam
        {
            get => isBlueTeam;
            set
            {
                isBlueTeam = value;
                MainBorder.BorderBrush = new SolidColorBrush(
                    isBlueTeam ?
                    Color.FromRgb(82, 133, 255) :
                    Color.FromRgb(255, 65, 65));
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