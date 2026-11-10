using Owmeta.Model;
using System.Text;
using System.Windows.Media;

namespace Owmeta.Display
{
    /// <summary>
    /// Single source of truth for HUD colors. XAML references these through x:Static,
    /// so every panel uses the same frozen brushes.
    /// </summary>
    public static class HudPalette
    {
        // Surfaces
        public static readonly SolidColorBrush Panel = Create(0xF0, 0x0B, 0x10, 0x18);
        public static readonly SolidColorBrush PanelBorder = Create(0x24, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Card = Create(0x0F, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush CardBorder = Create(0x14, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Divider = Create(0x1A, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush AvatarBackground = Create(0xFF, 0x1C, 0x24, 0x31);
        public static readonly SolidColorBrush Track = Create(0x26, 0xFF, 0xFF, 0xFF);

        // Text
        public static readonly SolidColorBrush TextPrimary = Create(0xFF, 0xF4, 0xF6, 0xFA);
        public static readonly SolidColorBrush TextSecondary = Create(0xFF, 0xA7, 0xB1, 0xC2);
        public static readonly SolidColorBrush TextMuted = Create(0xFF, 0x6B, 0x77, 0x8C);
        public static readonly SolidColorBrush TextLabel = Create(0xFF, 0x86, 0x92, 0xA6);

        // Semantics
        public static readonly SolidColorBrush Good = Create(0xFF, 0x3D, 0xDC, 0x97);
        public static readonly SolidColorBrush GoodSoft = Create(0x29, 0x3D, 0xDC, 0x97);
        public static readonly SolidColorBrush Bad = Create(0xFF, 0xFF, 0x5C, 0x6C);
        public static readonly SolidColorBrush BadSoft = Create(0x29, 0xFF, 0x5C, 0x6C);
        public static readonly SolidColorBrush Warn = Create(0xFF, 0xFF, 0xB8, 0x3D);
        public static readonly SolidColorBrush WarnSoft = Create(0x29, 0xFF, 0xB8, 0x3D);
        public static readonly SolidColorBrush Info = Create(0xFF, 0x6C, 0xA8, 0xFF);
        public static readonly SolidColorBrush InfoSoft = Create(0x29, 0x6C, 0xA8, 0xFF);
        public static readonly SolidColorBrush Neutral = Create(0xFF, 0x8A, 0x94, 0xA6);
        public static readonly SolidColorBrush NeutralSoft = Create(0x1F, 0xFF, 0xFF, 0xFF);

        // Teams (Overwatch's own ally blue and enemy red)
        public static readonly SolidColorBrush Ally = Create(0xFF, 0x3E, 0xA6, 0xFF);
        public static readonly SolidColorBrush Enemy = Create(0xFF, 0xFF, 0x46, 0x55);

        // Scoreboard surfaces, modelled on the in-game Tab scoreboard
        public static readonly SolidColorBrush TabActive = Create(0xFF, 0xF3, 0xF4, 0xF6);
        public static readonly SolidColorBrush TabActiveText = Create(0xFF, 0x1B, 0x23, 0x35);
        public static readonly SolidColorBrush TabIdle = Create(0xD9, 0x14, 0x1C, 0x2E);
        public static readonly SolidColorBrush TabIdleText = Create(0xFF, 0x9A, 0xA6, 0xBD);
        public static readonly SolidColorBrush ColumnBand = Create(0xF0, 0xE6, 0xE9, 0xF0);
        public static readonly SolidColorBrush ColumnText = Create(0xFF, 0x1B, 0x23, 0x35);
        public static readonly SolidColorBrush Strip = Create(0xF0, 0x0E, 0x14, 0x22);
        public static readonly SolidColorBrush GroupBand = Create(0xF0, 0x08, 0x0C, 0x16);
        public static readonly SolidColorBrush RowA = Create(0xE6, 0x16, 0x23, 0x3E);
        public static readonly SolidColorBrush RowB = Create(0xE6, 0x1A, 0x29, 0x48);
        public static readonly SolidColorBrush RowBest = Create(0xF0, 0x1F, 0x96, 0xD6);
        public static readonly SolidColorBrush AllyRowA = Create(0xEB, 0x16, 0x30, 0x56);
        public static readonly SolidColorBrush AllyRowB = Create(0xEB, 0x1A, 0x38, 0x62);
        public static readonly SolidColorBrush EnemyRowA = Create(0xEB, 0x46, 0x16, 0x22);
        public static readonly SolidColorBrush EnemyRowB = Create(0xEB, 0x52, 0x1A, 0x28);
        public static readonly SolidColorBrush ThreatBand = Create(0xE6, 0x78, 0x12, 0x20);
        public static readonly SolidColorBrush ThreatText = Create(0xFF, 0xFF, 0xD6, 0xDA);
        public static readonly SolidColorBrush PipEmpty = Create(0x1A, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush PipEmptyStrong = Create(0xFF, 0x4A, 0x54, 0x68);
        public static readonly SolidColorBrush CountZero = Create(0xFF, 0x4B, 0x54, 0x68);
        public static readonly SolidColorBrush BoardLabel = Create(0xFF, 0x8F, 0x9A, 0xB0);
        public static readonly SolidColorBrush HardOutline = Create(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush BestNeutralText = Create(0xB3, 0xFF, 0xFF, 0xFF);

        // Accent used for the "best pick" highlight
        public static readonly SolidColorBrush Accent = Create(0xFF, 0xFF, 0xB8, 0x3D);
        public static readonly SolidColorBrush AccentSoft = Create(0x1A, 0xFF, 0xB8, 0x3D);

        // Compositions (match the owmeta.io colors)
        public static readonly SolidColorBrush Dive = Create(0xFF, 0x38, 0xBD, 0xF8);
        public static readonly SolidColorBrush DiveSoft = Create(0x26, 0x38, 0xBD, 0xF8);
        public static readonly SolidColorBrush Brawl = Create(0xFF, 0xFB, 0x92, 0x3C);
        public static readonly SolidColorBrush BrawlSoft = Create(0x26, 0xFB, 0x92, 0x3C);
        public static readonly SolidColorBrush Poke = Create(0xFF, 0xC0, 0x84, 0xFC);
        public static readonly SolidColorBrush PokeSoft = Create(0x26, 0xC0, 0x84, 0xFC);

        // Faint composition names, for a hero neutral in that composition
        public static readonly SolidColorBrush DiveFaint = Create(0x47, 0x38, 0xBD, 0xF8);
        public static readonly SolidColorBrush BrawlFaint = Create(0x47, 0xFB, 0x92, 0x3C);
        public static readonly SolidColorBrush PokeFaint = Create(0x47, 0xC0, 0x84, 0xFC);

        // Dark text on a filled color tag
        public static readonly SolidColorBrush OnColor = Create(0xFF, 0x0B, 0x0F, 0x18);

        // Composition header cells: the team composition to fit, and a clear enemy composition
        public static readonly SolidColorBrush AllyCell = Create(0x2E, 0x3E, 0xA6, 0xFF);
        public static readonly SolidColorBrush GoodCell = Create(0x2E, 0x3D, 0xDC, 0x97);
        public static readonly SolidColorBrush LeadCell = Create(0x1C, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush LeadOutline = Create(0x80, 0xFF, 0xFF, 0xFF);

        // Rows of the swap and team views
        public static readonly SolidColorBrush RowTop = Create(0xF0, 0x15, 0x3A, 0x34);
        public static readonly SolidColorBrush RowYou = Create(0xF2, 0x0A, 0x0E, 0x18);
        public static readonly SolidColorBrush RowChange = Create(0xF0, 0x3E, 0x18, 0x26);
        public static readonly SolidColorBrush RowThreat = Create(0xF0, 0x4A, 0x16, 0x24);
        public static readonly SolidColorBrush RowHandled = Create(0xF0, 0x26, 0x1A, 0x28);

        public static SolidColorBrush CompositionFaintBrush(CompositionType comp) => comp switch
        {
            CompositionType.Dive => DiveFaint,
            CompositionType.Brawl => BrawlFaint,
            CompositionType.Poke => PokeFaint,
            _ => CountZero
        };

        // Opaque composition tints for bands drawn straight over the game
        public static readonly SolidColorBrush DiveBand = Mix(GroupBand, Dive, 0.24);
        public static readonly SolidColorBrush BrawlBand = Mix(GroupBand, Brawl, 0.24);
        public static readonly SolidColorBrush PokeBand = Mix(GroupBand, Poke, 0.24);

        public static SolidColorBrush CompositionBandBrush(CompositionType comp) => comp switch
        {
            CompositionType.Dive => DiveBand,
            CompositionType.Brawl => BrawlBand,
            CompositionType.Poke => PokeBand,
            _ => GroupBand
        };

        public static SolidColorBrush CompositionBrush(CompositionType comp) => comp switch
        {
            CompositionType.Dive => Dive,
            CompositionType.Brawl => Brawl,
            CompositionType.Poke => Poke,
            _ => TextSecondary
        };

        public static SolidColorBrush CompositionSoftBrush(CompositionType comp) => comp switch
        {
            CompositionType.Dive => DiveSoft,
            CompositionType.Brawl => BrawlSoft,
            CompositionType.Poke => PokeSoft,
            _ => NeutralSoft
        };

        public static SolidColorBrush ScoreBrush(int score) =>
            score > 0 ? Good : score < 0 ? Bad : Neutral;

        public static SolidColorBrush ScoreSoftBrush(int score) =>
            score > 0 ? GoodSoft : score < 0 ? BadSoft : NeutralSoft;

        private static SolidColorBrush Mix(SolidColorBrush background, SolidColorBrush tint, double amount)
        {
            Color a = background.Color, b = tint.Color;
            byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
            return Create(a.A, Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
        }

        private static SolidColorBrush Create(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public static class HudIcons
    {
        public const string Map = "M15,19L9,16.89V5L15,7.11M20.5,3C20.44,3 20.39,3 20.34,3L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21C3.55,21 3.61,21 3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3Z";
        public const string Brawl = "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z";
        public const string Dive = "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z";
        public const string Poke = "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 4a6 6 0 1 1 0 12 6 6 0 0 1 0-12zm0 4a2 2 0 1 0 0 4 2 2 0 0 0 0-4z";
    }

    public readonly record struct CompositionStyle(SolidColorBrush Foreground, SolidColorBrush Background, string PathData);

    public static class HudFormat
    {
        private static readonly string[] MapPrefixes =
        {
            "Antarctic Peninsula", "Busan", "Ilios", "Lijiang Tower", "Nepal", "Oasis", "Samoa"
        };

        public static CompositionStyle GetCompositionStyle(CompositionType comp) => comp switch
        {
            CompositionType.Dive => new(HudPalette.Dive, HudPalette.DiveSoft, HudIcons.Dive),
            CompositionType.Brawl => new(HudPalette.Brawl, HudPalette.BrawlSoft, HudIcons.Brawl),
            CompositionType.Poke => new(HudPalette.Poke, HudPalette.PokeSoft, HudIcons.Poke),
            _ => new(HudPalette.TextSecondary, HudPalette.NeutralSoft, HudIcons.Poke)
        };

        public static CompositionInfo CreateCompositionInfo(CompositionType comp)
        {
            var style = GetCompositionStyle(comp);
            return new CompositionInfo
            {
                Name = comp.ToString(),
                Foreground = style.Foreground,
                Background = style.Background
            };
        }

        public static List<CompositionInfo> CreateCompositionInfos(IEnumerable<CompositionType>? comps) =>
            comps?.Select(CreateCompositionInfo).ToList() ?? new List<CompositionInfo>();

        public static string Score(int score) => score > 0 ? $"+{score}" : score < 0 ? $"−{-score}" : "0";

        public static string HeroName(HeroName hero) => hero switch
        {
            Model.HeroName.Dva => "D.Va",
            Model.HeroName.Soldier76 => "Soldier: 76",
            Model.HeroName.Lucio => "Lúcio",
            Model.HeroName.Torbjorn => "Torbjörn",
            _ => SplitWords(hero.ToString())
        };

        public static string MapName(MapName map)
        {
            var name = map switch
            {
                Model.MapName.KingsRow => "King's Row",
                Model.MapName.Route66 => "Route 66",
                Model.MapName.Esperanca => "Esperança",
                Model.MapName.Paraiso => "Paraíso",
                Model.MapName.WatchpointGibraltar => "Watchpoint: Gibraltar",
                Model.MapName.ThroneOfAnubis => "Throne of Anubis",
                _ => SplitWords(map.ToString())
            };

            foreach (var prefix in MapPrefixes)
            {
                if (name.StartsWith(prefix + " ", StringComparison.Ordinal))
                    return $"{prefix}: {name[(prefix.Length + 1)..]}";
            }

            return name;
        }

        private static string SplitWords(string value)
        {
            var sb = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '_')
                {
                    sb.Append(' ');
                    continue;
                }

                if (i > 0 && char.IsUpper(c) && value[i - 1] != '_' && !char.IsUpper(value[i - 1]))
                    sb.Append(' ');
                else if (i > 0 && char.IsDigit(c) && !char.IsDigit(value[i - 1]))
                    sb.Append(' ');

                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Legacy brush names kept for older panels; values come from <see cref="HudPalette"/>.
    /// </summary>
    internal static class BrushCache
    {
        public static readonly SolidColorBrush Green = HudPalette.Good;
        public static readonly SolidColorBrush Red = HudPalette.Bad;
        public static readonly SolidColorBrush White = HudPalette.TextPrimary;
        public static readonly SolidColorBrush Blue = HudPalette.Info;
        public static readonly SolidColorBrush Teal = HudPalette.Brawl;
        public static readonly SolidColorBrush Pink = HudPalette.Dive;
        public static readonly SolidColorBrush Purple = HudPalette.Poke;

        public static readonly SolidColorBrush GreenBg = HudPalette.GoodSoft;
        public static readonly SolidColorBrush RedBg = HudPalette.BadSoft;
        public static readonly SolidColorBrush TealBg = HudPalette.BrawlSoft;
        public static readonly SolidColorBrush PinkBg = HudPalette.DiveSoft;
        public static readonly SolidColorBrush BlueBg = HudPalette.InfoSoft;
        public static readonly SolidColorBrush PurpleBg = HudPalette.PokeSoft;
        public static readonly SolidColorBrush WhiteBg = HudPalette.NeutralSoft;
    }
}
