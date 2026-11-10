using Owmeta.Model;

namespace Owmeta.Services
{
    public enum CompositionFit { Neutral, Strong, Weak }

    /// <summary>How one team lines up with dive, poke and brawl.</summary>
    public sealed record TeamCompositionSummary(
        HeroName? Tank,
        IReadOnlyList<CompositionType> TankCompositions,
        IReadOnlyList<HeroName> Others,
        IReadOnlyDictionary<CompositionType, int> StrongCounts,
        IReadOnlyDictionary<CompositionType, bool> TankStrong,
        int HeroCount);

    /// <summary>
    /// The composition a team leans towards. <see cref="Recommended"/> is the composition that beats it,
    /// set only when the lean is clear.
    /// </summary>
    public sealed record CompositionTendency(
        bool IsClear,
        IReadOnlyList<CompositionType> Leading,
        CompositionType? Recommended)
    {
        public static readonly CompositionTendency None = new(false, Array.Empty<CompositionType>(), null);
    }

    /// <summary>An enemy who counters most of the team.</summary>
    public sealed record TeamThreat(HeroName Enemy, IReadOnlyList<HeroName> Victims);

    public sealed record CompositionGroup(
        CompositionType Composition,
        bool IsRecommended,
        IReadOnlyList<HeroAnalysis> Heroes);

    public static class CompositionPolicy
    {
        /// <summary>Display order of the three compositions.</summary>
        public static readonly IReadOnlyList<CompositionType> Compositions =
            new[] { CompositionType.Dive, CompositionType.Poke, CompositionType.Brawl };

        /// <summary>The team plays around its tank, so the tank's compositions count twice.</summary>
        public const int TankWeight = 2;

        /// <summary>A composition is a clear tendency when it leads the next one by this much.</summary>
        public const int ClearLead = 2;

        /// <summary>An enemy is a threat when he counters at least this many heroes of the team.</summary>
        public const int ThreatMinVictims = 4;

        /// <summary>Dive beats poke, poke beats brawl, brawl beats dive.</summary>
        public static CompositionType CounterTo(CompositionType enemy) => enemy switch
        {
            CompositionType.Poke => CompositionType.Dive,
            CompositionType.Brawl => CompositionType.Poke,
            CompositionType.Dive => CompositionType.Brawl,
            _ => CompositionType.Unspecified
        };

        public static bool IsKnownHero(HeroName hero) =>
            hero != HeroName.Unknown && hero != HeroName.Hidden && hero != HeroName.NameUnspecified;

        public static IReadOnlyList<CompositionType> StrongCompositions(Hero? hero) =>
            hero?.BestCompositions?.Where(Compositions.Contains).Distinct().ToList()
            ?? (IReadOnlyList<CompositionType>)Array.Empty<CompositionType>();

        public static CompositionFit Fit(Hero? hero, CompositionType composition)
        {
            if (hero?.BestCompositions?.Contains(composition) == true) return CompositionFit.Strong;
            if (hero?.IncompatibleCompositions?.Contains(composition) == true) return CompositionFit.Weak;
            return CompositionFit.Neutral;
        }

        /// <summary>Known heroes in scoreboard order: tank, damage, support, then name.</summary>
        public static IReadOnlyList<KeyValuePair<HeroName, HeroAnalysis>> Ordered(IReadOnlyDictionary<HeroName, HeroAnalysis>? team) =>
            team == null
                ? Array.Empty<KeyValuePair<HeroName, HeroAnalysis>>()
                : team.Where(kvp => IsKnownHero(kvp.Key))
                      .OrderBy(kvp => RoleOrder(kvp.Value.Hero?.Role))
                      .ThenBy(kvp => kvp.Key.ToString(), StringComparer.Ordinal)
                      .ToList();

        public static TeamCompositionSummary Summarize(IReadOnlyDictionary<HeroName, HeroAnalysis>? team)
        {
            var heroes = Ordered(team);
            var tank = heroes.Where(kvp => kvp.Value.Hero?.Role == HeroRole.Tank)
                             .Select(kvp => (HeroName?)kvp.Key)
                             .FirstOrDefault();
            var tankComps = tank.HasValue ? StrongCompositions(team![tank.Value].Hero) : Array.Empty<CompositionType>();

            var counts = Compositions.ToDictionary(
                c => c,
                c => heroes.Count(kvp => StrongCompositions(kvp.Value.Hero).Contains(c)));
            var tankStrong = Compositions.ToDictionary(c => c, c => tankComps.Contains(c));

            return new TeamCompositionSummary(
                tank,
                tankComps,
                heroes.Select(kvp => kvp.Key).Where(h => h != tank).ToList(),
                counts,
                tankStrong,
                heroes.Count);
        }

        public static CompositionTendency Tendency(IReadOnlyDictionary<HeroName, HeroAnalysis>? team)
        {
            var summary = Summarize(team);
            if (summary.HeroCount == 0) return CompositionTendency.None;

            var weights = Compositions
                .Select(c => (Comp: c, Weight: summary.StrongCounts[c] + (summary.TankStrong[c] ? TankWeight - 1 : 0)))
                .OrderByDescending(x => x.Weight)
                .ToList();

            int top = weights[0].Weight;
            if (top == 0) return CompositionTendency.None;

            var leading = weights.Where(x => x.Weight == top).Select(x => x.Comp).ToList();
            bool clear = top - weights[1].Weight >= ClearLead;
            return new CompositionTendency(clear, leading, clear ? CounterTo(weights[0].Comp) : null);
        }

        public static TeamThreat? FindThreat(
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemies,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? team)
        {
            if (enemies == null || team == null) return null;

            TeamThreat? best = null;
            int bestScore = int.MinValue;
            foreach (var (enemy, analysis) in Ordered(enemies))
            {
                var victims = (analysis.HardCounters ?? Enumerable.Empty<HeroCounter>())
                    .Concat(analysis.SoftCounters ?? Enumerable.Empty<HeroCounter>())
                    .Select(c => c.HeroName)
                    .Where(h => IsKnownHero(h) && team.ContainsKey(h))
                    .Distinct()
                    .ToList();

                if (victims.Count < ThreatMinVictims) continue;
                if (best != null && (victims.Count < best.Victims.Count ||
                                     (victims.Count == best.Victims.Count && analysis.HeroScore <= bestScore)))
                    continue;

                best = new TeamThreat(enemy, victims);
                bestScore = analysis.HeroScore;
            }
            return best;
        }

        /// <summary>
        /// Groups swaps by the composition each hero is strong in, keeping their order inside a group.
        /// A hero strong in the recommended composition goes to that group, which comes first; the other
        /// groups follow their best score. Heroes with no known composition go last.
        /// </summary>
        public static IReadOnlyList<CompositionGroup> Group(IEnumerable<HeroAnalysis> swaps, CompositionType? recommended)
        {
            var groups = new Dictionary<CompositionType, List<HeroAnalysis>>();
            foreach (var swap in swaps)
            {
                var strong = StrongCompositions(swap.Hero);
                var home = recommended.HasValue && strong.Contains(recommended.Value)
                    ? recommended.Value
                    : strong.Count > 0 ? strong[0] : CompositionType.Unspecified;

                if (!groups.TryGetValue(home, out var list))
                    groups[home] = list = new List<HeroAnalysis>();
                list.Add(swap);
            }

            return groups
                .OrderByDescending(g => recommended.HasValue && g.Key == recommended.Value)
                .ThenBy(g => g.Key == CompositionType.Unspecified)
                .ThenByDescending(g => g.Value.Max(s => s.HeroScore))
                .ThenBy(g => g.Key)
                .Select(g => new CompositionGroup(g.Key, recommended.HasValue && g.Key == recommended.Value, g.Value))
                .ToList();
        }

        private static int RoleOrder(HeroRole? role) => role switch
        {
            HeroRole.Tank => 0,
            HeroRole.Damage => 1,
            HeroRole.Support => 2,
            _ => 3
        };
    }
}
