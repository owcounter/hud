using Owmeta.Model;
using Owmeta.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// A portrait in a scoreboard column. Hard relationships get a white outline, soft ones are dimmed,
    /// and the team threat is ringed in enemy red wherever it appears.
    /// </summary>
    public sealed class BoardIcon
    {
        public required ImageSource Icon { get; init; }
        public bool IsHard { get; init; }
        public bool IsThreat { get; init; }
        public bool Plain { get; init; }

        public Brush? Ring => IsThreat ? HudPalette.Enemy : IsHard ? HudPalette.HardOutline : null;
        public double RingWidth => IsThreat ? 4 : 3;
        public bool Dimmed => !Plain && !IsHard && !IsThreat;
    }

    /// <summary>Shared builders for the scoreboard-style views.</summary>
    public static class Board
    {
        public const int IconSize = 128;
        public const int MaxIcons = 3;

        public static ImageSource Portrait(HeroName hero) => IconUtils.GetHeroIconAsBitmapSource(hero, IconSize);

        /// <summary>Hard relationships first, then soft ones, limited to heroes accepted by <paramref name="include"/>.</summary>
        public static List<BoardIcon> Relations(
            IEnumerable<HeroCounter>? hard,
            IEnumerable<HeroCounter>? soft,
            Func<HeroName, bool> include,
            HeroName? threat,
            int max = MaxIcons)
        {
            var result = new List<BoardIcon>();
            var seen = new HashSet<HeroName>();
            void Add(IEnumerable<HeroCounter>? list, bool isHard)
            {
                if (list == null) return;
                foreach (var c in list)
                {
                    if (result.Count >= max) return;
                    if (!CompositionPolicy.IsKnownHero(c.HeroName) || !include(c.HeroName) || !seen.Add(c.HeroName)) continue;
                    result.Add(new BoardIcon { Icon = Portrait(c.HeroName), IsHard = isHard, IsThreat = c.HeroName == threat });
                }
            }
            Add(hard, true);
            Add(soft, false);
            return result;
        }

        public static List<BoardIcon> Synergies(IEnumerable<HeroSynergy>? synergies, Func<HeroName, bool> include, int max = MaxIcons) =>
            synergies?.Where(s => CompositionPolicy.IsKnownHero(s.HeroName) && include(s.HeroName))
                      .Select(s => s.HeroName).Distinct().Take(max)
                      .Select(h => new BoardIcon { Icon = Portrait(h) })
                      .ToList() ?? new List<BoardIcon>();

        public static List<BoardIcon> Heroes(IEnumerable<HeroName> heroes, HeroName? threat) =>
            heroes.Select(h => new BoardIcon { Icon = Portrait(h), Plain = true, IsThreat = h == threat }).ToList();

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
