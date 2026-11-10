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

    /// <summary>A hero on the other side of a counter relationship.</summary>
    public sealed record CounterLink(HeroName Hero, bool IsHard)
    {
        public int Weight => IsHard ? CompositionPolicy.HardWeight : 1;
    }

    /// <summary>
    /// An enemy and how he lines up against the team: the teammates he counters, the teammates who
    /// counter him, and the difference (hard counters count double). A weight of zero or less means
    /// the team already handles him.
    /// </summary>
    public sealed record EnemyThreat(
        HeroName Enemy,
        int HeroScore,
        IReadOnlyList<CounterLink> Victims,
        IReadOnlyList<CounterLink> AnsweredBy)
    {
        public int Weight => Victims.Sum(v => v.Weight) - AnsweredBy.Sum(a => a.Weight);
        public bool IsHandled => Weight <= 0;
    }

    /// <summary>A teammate swapping to another hero, and how much his score would rise.</summary>
    public sealed record TeamSwap(HeroName From, HeroAnalysis To, int Gain);

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

        /// <summary>A hard counter weighs this many soft ones when ranking threats.</summary>
        public const int HardWeight = 2;

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

        /// <summary>
        /// The composition the team's heroes should fit. When the enemy leans clearly towards one
        /// composition, it is the one that beats it. Otherwise it is the team's own clear tendency,
        /// computed without <paramref name="without"/>: the hero being replaced must not decide what his
        /// replacement should fit (a tank otherwise drags the whole team towards his own composition).
        /// </summary>
        public static CompositionType? FitTarget(
            IReadOnlyDictionary<HeroName, HeroAnalysis>? team,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemies,
            HeroName? without)
        {
            var enemy = Tendency(enemies);
            if (enemy.Recommended.HasValue) return enemy.Recommended;
            if (team == null) return null;

            var mates = without.HasValue
                ? team.Where(kvp => kvp.Key != without.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : team;
            var own = Tendency(mates);
            return own.IsClear ? own.Leading[0] : null;
        }

        /// <summary>
        /// Enemies who counter at least one teammate, most dangerous first: the teammates they counter
        /// minus the teammates who counter them (hard counters count double), then the number of
        /// teammates they counter, then their own score.
        /// </summary>
        public static IReadOnlyList<EnemyThreat> RankThreats(
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemies,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? team)
        {
            if (enemies == null || team == null) return Array.Empty<EnemyThreat>();

            var mates = Ordered(team);
            var threats = new List<EnemyThreat>();
            foreach (var (enemy, analysis) in Ordered(enemies))
            {
                var victims = new List<CounterLink>();
                var answers = new List<CounterLink>();
                foreach (var (mate, mateAnalysis) in mates)
                {
                    var beats = Strongest(CounterOf(analysis, mate), CounteredByOf(mateAnalysis, enemy));
                    if (beats.HasValue) victims.Add(new CounterLink(mate, beats.Value));
                    var beaten = Strongest(CounteredByOf(analysis, mate), CounterOf(mateAnalysis, enemy));
                    if (beaten.HasValue) answers.Add(new CounterLink(mate, beaten.Value));
                }
                if (victims.Count > 0) threats.Add(new EnemyThreat(enemy, analysis.HeroScore, victims, answers));
            }

            return threats
                .OrderByDescending(t => t.Weight)
                .ThenByDescending(t => t.Victims.Count)
                .ThenByDescending(t => t.HeroScore)
                .ToList();
        }

        /// <summary>
        /// The best swap for <paramref name="from"/>: the highest-scoring suggestion that raises his score
        /// by at least <paramref name="minGain"/> (and at least 1), is not already on the team, is not bad
        /// in the composition the rest of the team should fit, and passes <paramref name="accept"/>.
        /// </summary>
        public static TeamSwap? BestSwap(
            IReadOnlyDictionary<HeroName, HeroAnalysis> team,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemies,
            HeroName from,
            int minGain,
            Func<HeroAnalysis, bool>? accept = null)
        {
            if (!team.TryGetValue(from, out var current) || current.SwapSuggestions == null) return null;

            var target = FitTarget(team, enemies, from);
            int threshold = Math.Max(1, minGain);
            return current.SwapSuggestions
                .Where(s => s.Hero != null && IsKnownHero(s.Hero.Name) && !team.ContainsKey(s.Hero.Name))
                .Where(s => s.HeroScore - current.HeroScore >= threshold)
                .Where(s => !target.HasValue || Fit(s.Hero, target.Value) != CompositionFit.Weak)
                .Where(s => accept == null || accept(s))
                .OrderByDescending(s => s.HeroScore)
                .Select(s => new TeamSwap(from, s, s.HeroScore - current.HeroScore))
                .FirstOrDefault();
        }

        /// <summary>The best swap on the whole team that counters <paramref name="enemy"/>, hard counters first on a tie.</summary>
        public static TeamSwap? BestAnswer(
            IReadOnlyDictionary<HeroName, HeroAnalysis> team,
            IReadOnlyDictionary<HeroName, HeroAnalysis>? enemies,
            HeroName enemy)
        {
            return Ordered(team)
                .Select(kvp => BestSwap(team, enemies, kvp.Key, 1, s => CounterOf(s, enemy).HasValue))
                .Where(s => s != null)
                .Select(s => s!)
                .OrderByDescending(s => s.Gain)
                .ThenByDescending(s => CounterOf(s.To, enemy) == true)
                .FirstOrDefault();
        }

        /// <summary>Whether <paramref name="analysis"/> counters <paramref name="target"/>: null if not, true if hard.</summary>
        public static bool? CounterOf(HeroAnalysis? analysis, HeroName target) =>
            Find(analysis?.HardCounters, analysis?.SoftCounters, target);

        /// <summary>Whether <paramref name="analysis"/> is countered by <paramref name="target"/>: null if not, true if hard.</summary>
        public static bool? CounteredByOf(HeroAnalysis? analysis, HeroName target) =>
            Find(analysis?.HardCounteredBy, analysis?.SoftCounteredBy, target);

        private static bool? Find(IEnumerable<HeroCounter>? hard, IEnumerable<HeroCounter>? soft, HeroName target)
        {
            if (hard?.Any(c => c.HeroName == target) == true) return true;
            if (soft?.Any(c => c.HeroName == target) == true) return false;
            return null;
        }

        private static bool? Strongest(bool? a, bool? b) =>
            a == true || b == true ? true : a.HasValue || b.HasValue ? false : null;

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
