using Owmeta.Model;
using Owmeta.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Owmeta.Display
{
    /// <summary>
    /// Composition table shared by the swap and team views: one row per team (yours in blue on top,
    /// the enemy in red below) with the tank and his compositions, the other heroes, and how many of
    /// the team are strong in dive, poke and brawl. The enemy's clear tendency is tinted and the
    /// composition that beats it is outlined on your side.
    /// </summary>
    public sealed class CompositionHeader : Border
    {
        private const double RowHeight = 96;
        private const double CountWidth = 250;
        private const double ScoreWidth = 170;

        private static readonly Brush AllyTint = TeamTint(Color.FromRgb(0x3E, 0xA6, 0xFF));
        private static readonly Brush EnemyTint = TeamTint(Color.FromRgb(0xFF, 0x46, 0x55));

        private readonly Grid _grid = new();

        public CompositionHeader()
        {
            Background = HudPalette.Strip;
            Padding = new Thickness(0, 8, 0, 12);
            Child = _grid;
            Update(null, null, null);
        }

        /// <summary>Adds a total score column (team view).</summary>
        public bool ShowScore { get; set; }

        public CompositionTendency Tendency { get; private set; } = CompositionTendency.None;
        public string AllyScoreText { get; private set; } = "--";
        public string EnemyScoreText { get; private set; } = "--";

        public void Update(
            IReadOnlyDictionary<HeroName, HeroAnalysis>? ours,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? theirs,
            HeroName? threat)
        {
            Tendency = CompositionPolicy.Tendency(theirs);
            var allySummary = CompositionPolicy.Summarize(ours);
            var enemySummary = CompositionPolicy.Summarize(theirs);
            AllyScoreText = TeamScore(ours, allySummary);
            EnemyScoreText = TeamScore(theirs, enemySummary);

            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _grid.ColumnDefinitions.Clear();
            foreach (var width in new[] { 140.0, 470, -1, CountWidth, CountWidth, CountWidth, ShowScore ? ScoreWidth : 0 })
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

            Place(Board.Label("TANK"), 0, 1);
            Place(Board.Label("TEAM"), 0, 2);
            for (int i = 0; i < CompositionPolicy.Compositions.Count; i++)
            {
                var comp = CompositionPolicy.Compositions[i];
                var label = Board.BoardText(Board.CompositionName(comp), 28, HudPalette.CompositionBrush(comp));
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Place(label, 0, 3 + i);
            }
            if (ShowScore)
            {
                var score = Board.Label("SCORE");
                score.HorizontalAlignment = HorizontalAlignment.Center;
                Place(score, 0, 6);
            }

            AddTeamRow(1, "YOU", HudPalette.Ally, AllyTint, allySummary, threat, AllyScoreText, ours,
                comp => Tendency.Recommended == comp ? CellState.Recommended : CellState.Plain);
            AddTeamRow(3, "ENEMY", HudPalette.Enemy, EnemyTint, enemySummary, threat, EnemyScoreText, theirs,
                comp => Tendency.Leading.Contains(comp) ? CellState.Leading : CellState.Plain);
        }

        private enum CellState { Plain, Leading, Recommended }

        private void AddTeamRow(
            int row, string label, SolidColorBrush team, Brush tint,
            TeamCompositionSummary summary, HeroName? threat, string scoreText,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? analyses,
            Func<CompositionType, CellState> stateOf)
        {
            var band = new Border { Background = tint, BorderBrush = team, BorderThickness = new Thickness(10, 0, 0, 0) };
            Grid.SetColumnSpan(band, 7);
            Place(band, row, 0);

            var name = Board.BoardText(label, 32, team);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.Margin = new Thickness(10, 0, 0, 0);
            Place(name, row, 0);

            var tank = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (summary.Tank.HasValue)
            {
                tank.Children.Add(Board.Avatar(summary.Tank.Value, 80, team, 4));
                var comps = Board.BoardText(string.Empty, 38);
                comps.Margin = new Thickness(20, 0, 0, 0);
                Board.SetCompositions(comps, summary.TankCompositions);
                tank.Children.Add(comps);
            }
            else
            {
                tank.Children.Add(Board.BoardText("—", 38, HudPalette.CountZero));
            }
            Place(tank, row, 1);

            var mates = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var hero in summary.Others)
            {
                bool isThreat = hero == threat;
                var avatar = Board.Avatar(hero, 48, isThreat ? HudPalette.Enemy : null, 4);
                avatar.Margin = new Thickness(0, 0, 8, 0);
                mates.Children.Add(avatar);
            }
            Place(mates, row, 2);

            for (int i = 0; i < CompositionPolicy.Compositions.Count; i++)
            {
                var comp = CompositionPolicy.Compositions[i];
                Place(CountCell(comp, summary, stateOf(comp)), row, 3 + i);
            }

            if (ShowScore)
            {
                int? total = analyses == null || summary.HeroCount == 0 ? null : TeamTotal(analyses);
                var score = Board.BoardText(scoreText, 44, total.HasValue ? HudPalette.ScoreBrush(total.Value) : HudPalette.CountZero);
                score.HorizontalAlignment = HorizontalAlignment.Center;
                Place(score, row, 6);
            }
        }

        private static FrameworkElement CountCell(CompositionType comp, TeamCompositionSummary summary, CellState state)
        {
            var color = HudPalette.CompositionBrush(comp);
            int count = summary.StrongCounts[comp];
            bool tankStrong = summary.TankStrong[comp];

            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var number = Board.BoardText(count.ToString(), 44, count > 0 ? color : HudPalette.CountZero);
            number.Width = 34;
            number.TextAlignment = TextAlignment.Right;
            number.Margin = new Thickness(0, 0, 14, 0);
            content.Children.Add(number);

            for (int i = 0; i < 5; i++)
            {
                // The tank's bar is drawn first and in white, so you see at a glance whether he fits.
                Brush fill = i >= count ? HudPalette.PipEmpty
                    : i == 0 && tankStrong ? HudPalette.TextPrimary
                    : color;
                content.Children.Add(new Border
                {
                    Width = 17,
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = fill,
                    RenderTransform = new SkewTransform(-14, 0),
                    RenderTransformOrigin = new Point(0.5, 0.5)
                });
            }

            var cell = new Grid { Height = 80, Margin = new Thickness(8, 0, 8, 0) };
            if (state == CellState.Leading)
            {
                cell.Children.Add(new Border { Background = HudPalette.CompositionSoftBrush(comp) });
            }
            else if (state == CellState.Recommended)
            {
                cell.Children.Add(new Border
                {
                    Background = HudPalette.CompositionSoftBrush(comp),
                    BorderBrush = color,
                    BorderThickness = new Thickness(4)
                });
                cell.Children.Add(new Path
                {
                    Data = Geometry.Parse("M0,20 L12,0 L24,20 Z"),
                    Fill = color,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0)
                });
            }
            cell.Children.Add(content);
            return cell;
        }

        private static int TeamTotal(IReadOnlyDictionary<HeroName, HeroAnalysis> team) =>
            team.Where(kvp => CompositionPolicy.IsKnownHero(kvp.Key)).Sum(kvp => kvp.Value.HeroScore);

        private static string TeamScore(IReadOnlyDictionary<HeroName, HeroAnalysis>? team, TeamCompositionSummary summary) =>
            team == null || summary.HeroCount == 0 ? "--" : HudFormat.Score(TeamTotal(team));

        private void Place(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            _grid.Children.Add(element);
        }

        private static Brush TeamTint(Color team)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x4D, team.R, team.G, team.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, team.R, team.G, team.B), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, team.R, team.G, team.B), 0.7));
            brush.Freeze();
            return brush;
        }
    }
}
