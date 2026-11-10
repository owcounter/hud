using System.Runtime.Serialization;

namespace Owmeta.Model
{
    // The API's data model (owmeta api/ochmodel.proto), written by hand: the HUD only reads and
    // writes it as JSON with Newtonsoft. Enum values are the API's numbers; EnumMember gives the
    // API names so string enums parse too. Lists are never null, nested objects can be.

    /// <summary>Hero identifiers as the API sends them. UNKNOWN (1): unreadable portrait; HIDDEN (2): not shown yet.</summary>
    public enum HeroName
    {
        [EnumMember(Value = "NAME_UNSPECIFIED")] NameUnspecified = 0,
        [EnumMember(Value = "UNKNOWN")] Unknown = 1,
        [EnumMember(Value = "HIDDEN")] Hidden = 2,
        [EnumMember(Value = "ANA")] Ana = 3,
        [EnumMember(Value = "ASHE")] Ashe = 4,
        [EnumMember(Value = "BAPTISTE")] Baptiste = 5,
        [EnumMember(Value = "BASTION")] Bastion = 6,
        [EnumMember(Value = "BRIGITTE")] Brigitte = 7,
        [EnumMember(Value = "CASSIDY")] Cassidy = 8,
        [EnumMember(Value = "DVA")] Dva = 9,
        [EnumMember(Value = "DOOMFIST")] Doomfist = 10,
        [EnumMember(Value = "ECHO")] Echo = 11,
        [EnumMember(Value = "GENJI")] Genji = 12,
        [EnumMember(Value = "HANZO")] Hanzo = 13,
        [EnumMember(Value = "ILLARI")] Illari = 14,
        [EnumMember(Value = "JUNKER_QUEEN")] JunkerQueen = 15,
        [EnumMember(Value = "JUNKRAT")] Junkrat = 16,
        [EnumMember(Value = "JUNO")] Juno = 17,
        [EnumMember(Value = "KIRIKO")] Kiriko = 18,
        [EnumMember(Value = "LIFEWEAVER")] Lifeweaver = 19,
        [EnumMember(Value = "LUCIO")] Lucio = 20,
        [EnumMember(Value = "MAUGA")] Mauga = 21,
        [EnumMember(Value = "MEI")] Mei = 22,
        [EnumMember(Value = "MERCY")] Mercy = 23,
        [EnumMember(Value = "MOIRA")] Moira = 24,
        [EnumMember(Value = "ORISA")] Orisa = 25,
        [EnumMember(Value = "PHARAH")] Pharah = 26,
        [EnumMember(Value = "RAMATTRA")] Ramattra = 27,
        [EnumMember(Value = "REAPER")] Reaper = 28,
        [EnumMember(Value = "REINHARDT")] Reinhardt = 29,
        [EnumMember(Value = "ROADHOG")] Roadhog = 30,
        [EnumMember(Value = "SIGMA")] Sigma = 31,
        [EnumMember(Value = "SOLDIER76")] Soldier76 = 32,
        [EnumMember(Value = "SOMBRA")] Sombra = 33,
        [EnumMember(Value = "SOJOURN")] Sojourn = 34,
        [EnumMember(Value = "SYMMETRA")] Symmetra = 35,
        [EnumMember(Value = "TORBJORN")] Torbjorn = 36,
        [EnumMember(Value = "TRACER")] Tracer = 37,
        [EnumMember(Value = "VENTURE")] Venture = 38,
        [EnumMember(Value = "WIDOWMAKER")] Widowmaker = 39,
        [EnumMember(Value = "WINSTON")] Winston = 40,
        [EnumMember(Value = "WRECKING_BALL")] WreckingBall = 41,
        [EnumMember(Value = "ZARYA")] Zarya = 42,
        [EnumMember(Value = "ZENYATTA")] Zenyatta = 43,
        [EnumMember(Value = "HAZARD")] Hazard = 44,
        [EnumMember(Value = "FREJA")] Freja = 45,
        [EnumMember(Value = "WUYANG")] Wuyang = 46,
        [EnumMember(Value = "VENDETTA")] Vendetta = 47,
        [EnumMember(Value = "ANRAN")] Anran = 48,
        [EnumMember(Value = "DOMINA")] Domina = 49,
        [EnumMember(Value = "EMRE")] Emre = 50,
        [EnumMember(Value = "JETPACK_CAT")] JetpackCat = 51,
        [EnumMember(Value = "MIZUKI")] Mizuki = 52,
        [EnumMember(Value = "SIERRA")] Sierra = 53,
        [EnumMember(Value = "SHION")] Shion = 54,
        [EnumMember(Value = "DMON")] Dmon = 55,
        [EnumMember(Value = "DOCTRINE")] Doctrine = 56,
    }

    /// <summary>Hero role.</summary>
    public enum HeroRole
    {
        [EnumMember(Value = "ROLE_UNSPECIFIED")] RoleUnspecified = 0,
        [EnumMember(Value = "TANK")] Tank = 1,
        [EnumMember(Value = "DAMAGE")] Damage = 2,
        [EnumMember(Value = "SUPPORT")] Support = 3,
    }

    /// <summary>Map identifiers as the API sends them. ALL_MAPS (1) marks data that applies to every map.</summary>
    public enum MapName
    {
        [EnumMember(Value = "MAP_UNSPECIFIED")] MapUnspecified = 0,
        [EnumMember(Value = "ALL_MAPS")] AllMaps = 1,
        [EnumMember(Value = "ANTARCTIC_PENINSULA_ICEBREAKER")] AntarcticPeninsulaIcebreaker = 2,
        [EnumMember(Value = "ANTARCTIC_PENINSULA_LABS")] AntarcticPeninsulaLabs = 3,
        [EnumMember(Value = "ANTARCTIC_PENINSULA_SUBLEVEL")] AntarcticPeninsulaSublevel = 4,
        [EnumMember(Value = "BUSAN_DOWNTOWN")] BusanDowntown = 5,
        [EnumMember(Value = "BUSAN_SANCTUARY")] BusanSanctuary = 6,
        [EnumMember(Value = "BUSAN_MEKA_BASE")] BusanMekaBase = 7,
        [EnumMember(Value = "ILIOS_LIGHTHOUSE")] IliosLighthouse = 8,
        [EnumMember(Value = "ILIOS_WELL")] IliosWell = 9,
        [EnumMember(Value = "ILIOS_RUINS")] IliosRuins = 10,
        [EnumMember(Value = "LIJIANG_TOWER_NIGHT_MARKET")] LijiangTowerNightMarket = 11,
        [EnumMember(Value = "LIJIANG_TOWER_GARDEN")] LijiangTowerGarden = 12,
        [EnumMember(Value = "LIJIANG_TOWER_CONTROL_CENTER")] LijiangTowerControlCenter = 13,
        [EnumMember(Value = "NEPAL_VILLAGE")] NepalVillage = 14,
        [EnumMember(Value = "NEPAL_SHRINE")] NepalShrine = 15,
        [EnumMember(Value = "NEPAL_SANCTUM")] NepalSanctum = 16,
        [EnumMember(Value = "OASIS_CITY_CENTER")] OasisCityCenter = 17,
        [EnumMember(Value = "OASIS_GARDENS")] OasisGardens = 18,
        [EnumMember(Value = "OASIS_UNIVERSITY")] OasisUniversity = 19,
        [EnumMember(Value = "SAMOA_BEACH")] SamoaBeach = 20,
        [EnumMember(Value = "SAMOA_DOWNTOWN")] SamoaDowntown = 21,
        [EnumMember(Value = "SAMOA_VOLCANO")] SamoaVolcano = 22,
        [EnumMember(Value = "CIRCUIT_ROYAL")] CircuitRoyal = 23,
        [EnumMember(Value = "DORADO")] Dorado = 24,
        [EnumMember(Value = "HAVANA")] Havana = 25,
        [EnumMember(Value = "JUNKERTOWN")] Junkertown = 26,
        [EnumMember(Value = "RIALTO")] Rialto = 27,
        [EnumMember(Value = "ROUTE_66")] Route66 = 28,
        [EnumMember(Value = "SHAMBALI_MONASTERY")] ShambaliMonastery = 29,
        [EnumMember(Value = "WATCHPOINT_GIBRALTAR")] WatchpointGibraltar = 30,
        [EnumMember(Value = "NEW_JUNK_CITY")] NewJunkCity = 31,
        [EnumMember(Value = "SURAVASA")] Suravasa = 32,
        [EnumMember(Value = "BLIZZARD_WORLD")] BlizzardWorld = 33,
        [EnumMember(Value = "EICHENWALDE")] Eichenwalde = 34,
        [EnumMember(Value = "HOLLYWOOD")] Hollywood = 35,
        [EnumMember(Value = "KINGS_ROW")] KingsRow = 36,
        [EnumMember(Value = "MIDTOWN")] Midtown = 37,
        [EnumMember(Value = "NUMBANI")] Numbani = 38,
        [EnumMember(Value = "PARAISO")] Paraiso = 39,
        [EnumMember(Value = "COLOSSEO")] Colosseo = 40,
        [EnumMember(Value = "ESPERANCA")] Esperanca = 41,
        [EnumMember(Value = "NEW_QUEEN_STREET")] NewQueenStreet = 42,
        [EnumMember(Value = "RUNASAPI")] Runasapi = 43,
        [EnumMember(Value = "HANAOKA")] Hanaoka = 44,
        [EnumMember(Value = "THRONE_OF_ANUBIS")] ThroneOfAnubis = 45,
        [EnumMember(Value = "AATLIS")] Aatlis = 46,
        [EnumMember(Value = "NEON_JUNCTION")] NeonJunction = 47,
    }

    /// <summary>Team composition styles.</summary>
    public enum CompositionType
    {
        [EnumMember(Value = "COMPOSITION_TYPE_UNSPECIFIED")] Unspecified = 0,
        [EnumMember(Value = "DIVE")] Dive = 1,
        [EnumMember(Value = "BRAWL")] Brawl = 2,
        [EnumMember(Value = "POKE")] Poke = 3,
    }

    /// <summary>Strength of a counter or synergy.</summary>
    public enum Strength
    {
        [EnumMember(Value = "STRENGTH_UNSPECIFIED")] Unspecified = 0,
        [EnumMember(Value = "SOFT")] Soft = 1,
        [EnumMember(Value = "HARD")] Hard = 2,
        [EnumMember(Value = "NULLED")] Nulled = 3,
    }

    /// <summary>Tier derived from the hero score.</summary>
    public enum HeroTier
    {
        [EnumMember(Value = "HERO_TIER_UNSPECIFIED")] Unspecified = 0,
        [EnumMember(Value = "HERO_TIER_S")] S = 1,
        [EnumMember(Value = "HERO_TIER_A")] A = 2,
        [EnumMember(Value = "HERO_TIER_B")] B = 3,
        [EnumMember(Value = "HERO_TIER_C")] C = 4,
        [EnumMember(Value = "HERO_TIER_D")] D = 5,
    }

    public sealed class Hero
    {
        public HeroName Name { get; set; }
        public HeroRole Role { get; set; }
        public List<HeroCounter> CounteredBy { get; } = new();
        public List<HeroSynergy> Synergies { get; } = new();
        public List<CompositionType> BestCompositions { get; } = new();
        public List<CompositionType> IncompatibleCompositions { get; } = new();
        public List<MapName> StrongMaps { get; } = new();
        public List<MapName> WeakMaps { get; } = new();
    }

    public sealed class HeroCounter
    {
        public HeroName HeroName { get; set; }
        public string Comment { get; set; } = string.Empty;
        public Strength Strength { get; set; }
        public MapName SpecificMap { get; set; }
        public long CustomCounterId { get; set; }
    }

    public sealed class HeroSynergy
    {
        public HeroName HeroName { get; set; }
        public string Comment { get; set; } = string.Empty;
        public Strength Strength { get; set; }
        public MapName SpecificMap { get; set; }
        public long CustomSynergyId { get; set; }
    }

    /// <summary>One hero in a match: who he counters and is countered by, synergies, score and swap suggestions.</summary>
    public sealed class HeroAnalysis
    {
        public Hero? Hero { get; set; }
        public List<HeroCounter> HardCounters { get; } = new();
        public List<HeroCounter> HardCounteredBy { get; } = new();
        public List<HeroCounter> SoftCounters { get; } = new();
        public List<HeroCounter> SoftCounteredBy { get; } = new();
        public List<HeroSynergy> Synergies { get; } = new();
        public List<HeroAnalysis> SwapSuggestions { get; } = new();
        public int HeroScore { get; set; }
        public HeroTier HeroTier { get; set; }
    }

    public sealed class MatchState
    {
        public TeamComposition? BlueTeam { get; set; }
        public TeamComposition? RedTeam { get; set; }
        public HeroName PlayerHero { get; set; }
        public MapName Map { get; set; }
    }

    public sealed class TeamComposition
    {
        public HeroName Tank { get; set; }
        public HeroName Damage1 { get; set; }
        public HeroName Damage2 { get; set; }
        public HeroName Support1 { get; set; }
        public HeroName Support2 { get; set; }
    }
}
