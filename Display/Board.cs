using Owmeta.Model;
using Owmeta.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// A portrait in a counters or countered-by column. Every relationship has the same size; the ring
    /// says whether it is good (green) or bad (red) for your team, and a hard counter adds a small tag.
    /// </summary>
    public sealed class BoardIcon
    {
        public ImageSource? Icon { get; init; }
        public Brush? Ring { get; init; }
        public bool IsHard { get; init; }
        /// <summary>"+N" when this item stands for the relations that did not fit.</summary>
        public string? More { get; init; }

        public Visibility HardVisibility => IsHard ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AvatarVisibility => More == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility MoreVisibility => More == null ? Visibility.Collapsed : Visibility.Visible;
        public Brush HardText => HudPalette.OnColor;
    }

    /// <summary>A composition name in a "good in" column: colored when good, struck in red when bad, filled when it is the one to fit.</summary>
    public sealed record CompositionWord(string Text, Brush Foreground, Brush Background, TextDecorationCollection? Decorations);

    /// <summary>Shared builders for the scoreboard-style views.</summary>
    public static class Board
    {
        public const int IconSize = 128;
        public const int MaxIcons = 3;

        public static ImageSource Portrait(HeroName hero) => IconUtils.GetHeroIconAsBitmapSource(hero, IconSize);

        /// <summary>
        /// Hard relationships first, then soft ones, limited to heroes accepted by <paramref name="include"/>.
        /// <paramref name="ring"/> is green when the relationship helps your team and red when it hurts it.
        /// </summary>
        public static List<BoardIcon> Relations(
            IEnumerable<HeroCounter>? hard,
            IEnumerable<HeroCounter>? soft,
            Func<HeroName, bool> include,
            Brush ring,
            int max = MaxIcons)
        {
            var links = new List<CounterLink>();
            var seen = new HashSet<HeroName>();
            void Add(IEnumerable<HeroCounter>? list, bool isHard)
            {
                if (list == null) return;
                foreach (var c in list)
                {
                    if (!CompositionPolicy.IsKnownHero(c.HeroName) || !include(c.HeroName) || !seen.Add(c.HeroName)) continue;
                    links.Add(new CounterLink(c.HeroName, isHard));
                }
            }
            Add(hard, true);
            Add(soft, false);
            return Links(links, ring, max);
        }

        /// <summary>Teammates this hero has a synergy with, ringed in ally blue, "+N" past <paramref name="max"/>.</summary>
        public static List<BoardIcon> Synergies(IEnumerable<HeroSynergy>? synergies, Func<HeroName, bool> include, int max = MaxIcons) =>
            Links((synergies ?? Enumerable.Empty<HeroSynergy>())
                    .Select(s => s.HeroName)
                    .Where(h => CompositionPolicy.IsKnownHero(h) && include(h))
                    .Distinct()
                    .Select(h => new CounterLink(h, false)),
                HudPalette.Ally, max);

        /// <summary>Hard links first. Past <paramref name="max"/> portraits, the last slot shows "+N" for the rest.</summary>
        public static List<BoardIcon> Links(IEnumerable<CounterLink> links, Brush ring, int max = MaxIcons)
        {
            var ordered = links.OrderByDescending(l => l.IsHard).ToList();
            int shown = ordered.Count > max ? max - 1 : ordered.Count;
            var result = ordered.Take(shown)
                .Select(l => new BoardIcon { Icon = Portrait(l.Hero), IsHard = l.IsHard, Ring = ring })
                .ToList();
            if (ordered.Count > shown) result.Add(new BoardIcon { More = $"+{ordered.Count - shown}", Ring = ring });
            return result;
        }

        private static readonly TextDecorationCollection Strike = CreateStrike(HudPalette.Bad);
        private static readonly TextDecorationCollection StrikeOnFill = CreateStrike(HudPalette.OnColor);

        private static TextDecorationCollection CreateStrike(Brush brush)
        {
            var strike = new TextDecorationCollection(new[]
            {
                new TextDecoration(TextDecorationLocation.Strikethrough, new Pen(brush, 3), 0,
                    TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel)
            });
            strike.Freeze();
            return strike;
        }

        /// <summary>
        /// Dive, poke and brawl for one hero. Good: the composition color. Bad: struck in red. Neutral: faint.
        /// The composition to fit (<paramref name="target"/>) is filled: in its color when the hero is good
        /// in it, in red when he is bad in it, so a hero to avoid stands out.
        /// </summary>
        public static List<CompositionWord> Fits(Hero? hero, CompositionType? target)
        {
            var words = new List<CompositionWord>();
            foreach (var comp in CompositionPolicy.Compositions)
            {
                var name = CompositionName(comp);
                bool isTarget = target == comp;
                words.Add(CompositionPolicy.Fit(hero, comp) switch
                {
                    CompositionFit.Strong => isTarget
                        ? new CompositionWord(name, HudPalette.OnColor, HudPalette.CompositionBrush(comp), null)
                        : new CompositionWord(name, HudPalette.CompositionBrush(comp), Brushes.Transparent, null),
                    CompositionFit.Weak => isTarget
                        ? new CompositionWord(name, HudPalette.OnColor, HudPalette.Bad, StrikeOnFill)
                        : new CompositionWord(name, HudPalette.Bad, Brushes.Transparent, Strike),
                    _ => new CompositionWord(name, HudPalette.CompositionFaintBrush(comp), Brushes.Transparent, null)
                });
            }
            return words;
        }

        public static string CompositionName(CompositionType comp) => comp switch
        {
            CompositionType.Dive => "DIVE",
            CompositionType.Poke => "POKE",
            CompositionType.Brawl => "BRAWL",
            _ => "OTHER"
        };

        /// <summary>"BRAWL / DIVE" with each name in its composition color.</summary>
        public static void SetCompositions(TextBlock target, IReadOnlyList<CompositionType> comps, string separator = " / ")
        {
            target.Inlines.Clear();
            if (comps.Count == 0)
            {
                target.Inlines.Add(new Run("—") { Foreground = HudPalette.CountZero });
                return;
            }
            for (int i = 0; i < comps.Count; i++)
            {
                if (i > 0) target.Inlines.Add(new Run(separator) { Foreground = HudPalette.PipEmpty });
                target.Inlines.Add(new Run(CompositionName(comps[i])) { Foreground = HudPalette.CompositionBrush(comps[i]) });
            }
        }

        public static TextBlock BoardText(string text, double size, Brush? foreground = null)
        {
            var block = new TextBlock { Text = text, FontSize = size };
            block.SetResourceReference(FrameworkElement.StyleProperty, "Hud.Board");
            if (foreground != null) block.Foreground = foreground;
            return block;
        }

        public static TextBlock Label(string text, double size = 22, Brush? foreground = null)
        {
            var block = new TextBlock { Text = text, FontSize = size, Margin = new Thickness(0) };
            block.SetResourceReference(FrameworkElement.StyleProperty, "Hud.StripLabel");
            if (foreground != null) block.Foreground = foreground;
            return block;
        }

        public static HeroAvatar Avatar(HeroName hero, double size, Brush? ring = null, double ringWidth = 3) => new()
        {
            Source = Portrait(hero),
            Width = size,
            Height = size,
            CornerRatio = 0.06,
            ShowPip = false,
            Accent = ring,
            RingThickness = ringWidth
        };
    }
}
