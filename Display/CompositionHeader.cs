using Owmeta.Model;
using Owmeta.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// Composition table shared by the swap and team views: one row per team (yours in blue on top,
    /// the enemy in red below), one column per composition. Each cell shows the team's heroes in
    /// scoreboard order (tank, damage, support): ringed in green when good in that composition, faded
    /// when neutral, slashed in red when bad, then the good / neutral / bad counts.
    /// The enemy's clear composition is outlined, and on your side the composition to fit is outlined
    /// in blue, or in green when it is the one that beats the enemy.
    /// </summary>
    public sealed class CompositionHeader : Border
    {
        private const double LabelWidth = 340;
        private const double RowHeight = 92;
        private const double PortraitSize = 56;

        private static readonly Brush AllyTint = TeamTint(Color.FromRgb(0x3E, 0xA6, 0xFF));
        private static readonly Brush EnemyTint = TeamTint(Color.FromRgb(0xFF, 0x46, 0x55));

        private readonly Grid _grid = new();

        public CompositionHeader()
        {
            Background = HudPalette.Strip;
            Padding = new Thickness(0, 4, 0, 8);
            Child = _grid;
            Update(null, null, null);
        }

        public CompositionTendency Tendency { get; private set; } = CompositionTendency.None;
        public CompositionType? FitTarget { get; private set; }
        public int? AllyTotal { get; private set; }
        public int? EnemyTotal { get; private set; }
        public string AllyScoreText => AllyTotal.HasValue ? HudFormat.Score(AllyTotal.Value) : "--";
        public string EnemyScoreText => EnemyTotal.HasValue ? HudFormat.Score(EnemyTotal.Value) : "--";

        /// <param name="fitTarget">The composition your team should fit, from <see cref="CompositionPolicy.FitTarget"/>.</param>
        public void Update(
            IReadOnlyDictionary<HeroName, HeroAnalysis>? ours,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? theirs,
            CompositionType? fitTarget)
        {
            Tendency = CompositionPolicy.Tendency(theirs);
            FitTarget = fitTarget;
            var allies = CompositionPolicy.Ordered(ours);
            var enemies = CompositionPolicy.Ordered(theirs);
            AllyTotal = allies.Count == 0 ? null : allies.Sum(kvp => kvp.Value.HeroScore);
            EnemyTotal = enemies.Count == 0 ? null : enemies.Sum(kvp => kvp.Value.HeroScore);

            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _grid.ColumnDefinitions.Clear();
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            foreach (var _ in CompositionPolicy.Compositions)
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            _grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

            Place(Legend(), 0, 0);
            for (int i = 0; i < CompositionPolicy.Compositions.Count; i++)
            {
                var comp = CompositionPolicy.Compositions[i];
                var label = Board.BoardText(Board.CompositionName(comp), 34, HudPalette.CompositionBrush(comp));
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Place(label, 0, 1 + i);
            }

            AddTeamRow(1, "YOU", HudPalette.Ally, AllyTint, allies, comp =>
                fitTarget != comp ? CellState.Plain
                : Tendency.Recommended == comp ? CellState.Recommended
                : CellState.Team);
            AddTeamRow(3, "ENEMY", HudPalette.Enemy, EnemyTint, enemies, comp =>
                Tendency.IsClear && Tendency.Leading.Contains(comp) ? CellState.Leading : CellState.Plain);
        }

        private enum CellState { Plain, Leading, Team, Recommended }

        private static FrameworkElement Legend()
        {
            var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            foreach (var (text, brush) in new[] { ("GOOD", HudPalette.Good), ("NEUTRAL", HudPalette.PipEmptyStrong), ("BAD", HudPalette.Bad) })
            {
                legend.Children.Add(new Border { Width = 16, Height = 16, Background = brush, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0) });
                var label = Board.Label(text, 18);
                label.Margin = new Thickness(0, 0, 14, 0);
                legend.Children.Add(label);
            }
            return legend;
        }

        private void AddTeamRow(
            int row, string label, SolidColorBrush team, Brush tint,
            IReadOnlyList<KeyValuePair<HeroName, HeroAnalysis>> heroes,
            Func<CompositionType, CellState> stateOf)
        {
            var band = new Border { Background = tint, BorderBrush = team, BorderThickness = new Thickness(10, 0, 0, 0) };
            Grid.SetColumnSpan(band, 4);
            Place(band, row, 0);

            var name = Board.BoardText(label, 40, team);
            name.Margin = new Thickness(34, 0, 0, 0);
            Place(name, row, 0);

            for (int i = 0; i < CompositionPolicy.Compositions.Count; i++)
            {
                var comp = CompositionPolicy.Compositions[i];
                Place(Cell(comp, heroes, stateOf(comp)), row, 1 + i);
            }
        }

        private static FrameworkElement Cell(CompositionType comp, IReadOnlyList<KeyValuePair<HeroName, HeroAnalysis>> heroes, CellState state)
        {
            var portraits = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int good = 0, neutral = 0, bad = 0;
            HeroRole? previousRole = null;
            foreach (var (hero, analysis) in heroes)
            {
                var role = analysis.Hero?.Role;
                if (previousRole.HasValue && role != previousRole) portraits.Children.Add(new Border { Width = 12 });
                previousRole = role;

                var fit = CompositionPolicy.Fit(analysis.Hero, comp);
                var avatar = Board.Avatar(hero, PortraitSize);
                avatar.Margin = new Thickness(0, 0, 6, 0);
                switch (fit)
                {
                    case CompositionFit.Strong:
                        good++;
                        avatar.Accent = HudPalette.Good;
                        avatar.RingThickness = 4;
                        break;
                    case CompositionFit.Weak:
                        bad++;
                        avatar.Accent = HudPalette.Bad;
                        avatar.RingThickness = 4;
                        avatar.Struck = HudPalette.Bad;
                        break;
                    default:
                        neutral++;
                        avatar.Dimmed = true;
                        break;
                }
                portraits.Children.Add(avatar);
            }

            var counts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var (count, brush) in new[] { (good, HudPalette.Good), (neutral, HudPalette.TextSecondary), (bad, HudPalette.Bad) })
            {
                var number = Board.BoardText(count.ToString(), 44, count > 0 ? brush : HudPalette.CountZero);
                number.Width = 34;
                number.TextAlignment = TextAlignment.Center;
                number.Margin = new Thickness(4, 0, 4, 0);
                counts.Children.Add(number);
            }

            var content = new DockPanel { Margin = new Thickness(20, 0, 16, 0), LastChildFill = false };
            DockPanel.SetDock(counts, Dock.Right);
            content.Children.Add(counts);
            DockPanel.SetDock(portraits, Dock.Left);
            content.Children.Add(portraits);

            var cell = new Border
            {
                Child = content,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(4),
                Background = Brushes.Transparent,
                Margin = new Thickness(6, 2, 6, 2)
            };
            switch (state)
            {
                case CellState.Leading:
                    cell.Background = HudPalette.LeadCell;
                    cell.BorderBrush = HudPalette.LeadOutline;
                    break;
                case CellState.Team:
                    cell.Background = HudPalette.AllyCell;
                    cell.BorderBrush = HudPalette.Ally;
                    break;
                case CellState.Recommended:
                    cell.Background = HudPalette.GoodCell;
                    cell.BorderBrush = HudPalette.Good;
                    break;
            }
            return cell;
        }

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
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, team.R, team.G, team.B), 0.12));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, team.R, team.G, team.B), 0.25));
            brush.Freeze();
            return brush;
        }
    }
}
